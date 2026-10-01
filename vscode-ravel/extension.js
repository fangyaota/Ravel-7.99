const vscode = require('vscode');
const cp = require('child_process');
const fs = require('fs');
const path = require('path');
const { foldingRanges } = require('./folding');

/** @type {vscode.OutputChannel | undefined} */
let channel;

function getChannel() {
    if (!channel) channel = vscode.window.createOutputChannel('Ravel');
    return channel;
}

/** 跑 ravel 时的工作目录。必须是工作区根——Ravel 靠 cwd 找 lib/predefined.rav */
function workspaceCwd() {
    const folder = vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders[0];
    return folder ? folder.uri.fsPath : process.cwd();
}

/**
 * 找 ravel.dll：先看配置，再在工作区里搜。
 * @returns {Promise<string | {error: string}>} 路径，或带 error 的对象
 */
async function findRavelDll() {
    const configured = vscode.workspace.getConfiguration('ravel').get('ravelDll');
    if (configured) {
        return fs.existsSync(configured)
            ? configured
            : { error: `ravel.ravelDll 指向的文件不存在：${configured}` };
    }

    const found = await vscode.workspace.findFiles('**/ravel.dll', '**/{obj,node_modules}/**');
    if (found.length === 0) {
        return { error: '找不到 ravel.dll。先编译一次（Ctrl+Shift+B，或 dotnet build）。' };
    }

    // 取**最新的**那个。产物常常同时在 out/ 和 bin/Debug 里，按固定顺序挑会挑到陈旧的：
    // 只跑 dotnet build 时 bin/ 是新的、out/ 还是上次 publish 留下的；
    // 只跑 dotnet publish -o out 时反过来。比时间戳才不会跑到旧解释器上。
    const mtime = (p) => { try { return fs.statSync(p.fsPath).mtimeMs; } catch { return 0; } };
    found.sort((a, b) => mtime(b) - mtime(a) || a.fsPath.length - b.fsPath.length);
    return found[0].fsPath;
}

/** 在 Ravel 输出面板里跑一个子进程，stdout/stderr 实时流进去 */
function runToChannel(dll, args, title, env) {
    const ch = getChannel();
    ch.show(true);
    ch.appendLine(`$ dotnet "${dll}" ${args.join(' ')}`);

    const proc = cp.spawn('dotnet', [dll, ...args], {
        cwd: workspaceCwd(),
        env: env ? { ...process.env, ...env } : process.env,
    });
    proc.stdout.on('data', (d) => ch.append(d.toString()));
    proc.stderr.on('data', (d) => ch.append(d.toString()));
    proc.on('error', (err) => ch.appendLine(`\n启动失败：${err.message}`));
    proc.on('close', (code) => ch.appendLine(`\n[${title}结束，退出码 ${code}]`));
}

/** 取当前编辑器里要跑的 .rav 文件，必要时先存盘 */
async function currentRavelFile() {
    const editor = vscode.window.activeTextEditor;
    if (!editor) {
        vscode.window.showWarningMessage('先打开一个 .rav 文件。');
        return null;
    }
    if (editor.document.languageId !== 'ravel') {
        vscode.window.showWarningMessage('当前文件不是 Ravel 文件（.rav）。');
        return null;
    }
    if (editor.document.isDirty && !(await editor.document.save())) {
        vscode.window.showWarningMessage('文件保存失败，跑的还是磁盘上的旧内容。');
        return null;
    }
    return editor.document.uri.fsPath;
}

async function runFile() {
    const file = await currentRavelFile();
    if (!file) return;

    const dll = await findRavelDll();
    if (typeof dll !== 'string') {
        vscode.window.showErrorMessage(dll.error);
        return;
    }
    runToChannel(dll, [file], '运行');
}

/**
 * 在**外面那个命令行窗口**里跑 —— 真的弹一个独立控制台出来,不在 VS Code 里。
 *
 * 为什么不用 `TerminalLocation.External`:那条路试过了,**不吭声地回退** —— 外部终端要
 * `terminal.external` 那族设置配对了才认,配不对它不报错,直接在 VS Code 底部的终端面板里
 * 开一个,看起来就跟没用一样。所以这里自己起进程,走的是 cmd 自己那套。
 *
 * `cmd /c start` 让 Windows 开一个新的控制台窗口(Win11 默认终端是 Windows Terminal,
 * 所以开出来多半就是它);`/k` 让窗口跑完**留着**,你能接着敲下一句。
 *
 * `start` 后面那个**带引号的第一个参数是窗口标题** —— 这条规矩必须喂一个,不然 cmd 会把
 * 后面的 `cmd /k …` 整串当标题吃掉,结果什么都不开。顺带把文件名放进去,开多了一眼认得出。
 *
 * 和 `runFile` 的分工:那个把输出流进输出面板,不抢焦点、能一直翻;这个给你一个**真的
 * shell 窗口** —— 脚本里 `input ()` 能敲、`examples/repl.rav` 那种交互的跑得起来。
 * (这套是 Windows 的写法,别的系统上得另写。)
 *
 * @param {vscode.Uri | undefined} uri 资源管理器右键会把这个传进来;编辑器右键没有
 */
async function runFileInTerminal(uri) {
    // 资源管理器那条路:文件就在眼前,不必存盘;编辑器那条路得先存(跑的是磁盘上的那份)
    const file = uri && uri.fsPath ? uri.fsPath : await currentRavelFile();
    if (!file) return;

    const dll = await findRavelDll();
    if (typeof dll !== 'string') {
        vscode.window.showErrorMessage(dll.error);
        return;
    }

    // 路径带空格是常事(这个仓库自己就叫 `Ravel 7.99`),两处引号都不能省
    const line = `dotnet "${dll}" "${file}"`;

    // windowsVerbatimArguments:这串要**原样**交给 cmd,不能让 Node 好心再包一层引号 ——
    // 它会给每个参数都套引号,`cmd /k dotnet "…"` 那样一散,cmd 就解析错了。
    // 代价是自己写的引号(比如上面那个标题)得自己带上。
    cp.spawn('cmd.exe', [
        '/c', 'start',
        `"Ravel: ${path.basename(file)}"`,
        'cmd', '/k', line,
    ], {
        cwd: workspaceCwd(),    // Ravel 靠 cwd 找 lib/predefined.rav
        detached: true,
        stdio: 'ignore',
        windowsVerbatimArguments: true,
    }).unref();
}

async function runTests() {
    const dll = await findRavelDll();
    if (typeof dll !== 'string') {
        vscode.window.showErrorMessage(dll.error);
        return;
    }

    const limit = vscode.workspace.getConfiguration('ravel').get('heapHardLimit');
    runToChannel(dll, ['test'], '测试', limit ? { DOTNET_GCHeapHardLimit: limit } : undefined);
}

async function openRepl() {
    const dll = await findRavelDll();
    if (typeof dll !== 'string') {
        vscode.window.showErrorMessage(dll.error);
        return;
    }

    // REPL 要交互，所以走集成终端而不是输出面板
    const term = vscode.window.createTerminal({ name: 'Ravel REPL', cwd: workspaceCwd() });
    term.show();
    term.sendText(`dotnet "${dll}"`);
}

/**
 * 折叠:算区间那半在 `folding.js`(纯函数,node 能直接测),这里只把它接到 VS Code 上。
 *
 * 为什么非要有它:VS Code 对没有 provider 的语言按**缩进**折,而 Ravel 按 `{}` 分块 ——
 * 一个 `class {` 和它后面的缩进行对不上,折出来的边界是错的。有了 provider 才按括号来,
 * 顺带把"连着两行以上的整行注释"也折起来(这门语言注释多)。
 */
const foldingProvider = {
    provideFoldingRanges(document, context) {
        const ranges = foldingRanges(document.getText())
            .sort((a, b) => a.start - b.start || a.end - b.end)
            .map((r) => new vscode.FoldingRange(
                r.start,
                r.end,
                r.kind === 'comment' ? vscode.FoldingRangeKind.Comment : vscode.FoldingRangeKind.Region,
            ));

        // 客户端给了上限就照办(大文件上它只想要前 N 个)
        const limit = context && context.rangeLimit;
        return limit && ranges.length > limit ? ranges.slice(0, limit) : ranges;
    },
};

function activate(context) {
    context.subscriptions.push(
        vscode.commands.registerCommand('ravel.runFile', runFile),
        vscode.commands.registerCommand('ravel.runFileInTerminal', runFileInTerminal),
        vscode.commands.registerCommand('ravel.runTests', runTests),
        vscode.commands.registerCommand('ravel.openRepl', openRepl),
        vscode.languages.registerFoldingRangeProvider({ language: 'ravel' }, foldingProvider)
    );
}

function deactivate() {
    if (channel) channel.dispose();
}

module.exports = { activate, deactivate };
