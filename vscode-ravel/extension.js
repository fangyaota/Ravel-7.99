const vscode = require('vscode');
const cp = require('child_process');
const fs = require('fs');

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

    // out/ 和 bin/Debug 是常用的产物位置，优先；其余按路径短优先
    const rank = (p) => {
        const s = p.fsPath.replace(/\\/g, '/');
        if (s.includes('/out/')) return 0;
        if (s.includes('/bin/Debug/')) return 1;
        if (s.includes('/bin/Release/')) return 2;
        return 3;
    };
    found.sort((a, b) => rank(a) - rank(b) || a.fsPath.length - b.fsPath.length);
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

function activate(context) {
    context.subscriptions.push(
        vscode.commands.registerCommand('ravel.runFile', runFile),
        vscode.commands.registerCommand('ravel.runTests', runTests),
        vscode.commands.registerCommand('ravel.openRepl', openRepl)
    );
}

function deactivate() {
    if (channel) channel.dispose();
}

module.exports = { activate, deactivate };
