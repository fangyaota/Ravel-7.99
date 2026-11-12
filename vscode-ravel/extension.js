const vscode = require('vscode');
const cp = require('child_process');
const fs = require('fs');
const path = require('path');
const os = require('os');
const { foldingRanges } = require('./folding');
const { wordAt, findDefinition, lineOf, indexDefinitions } = require('./definitions');
const hints = require('./hints');

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

    // 全量那一趟已经取消:`ravel test` 不给词直接报错,所以这里先问一个词
    const pick = await vscode.window.showInputBox({
        prompt: '挑个沾相对路径的词(例如 lang、net、367);全量测试已取消',
        placeHolder: 'lang',
        validateInput: (v) => (v && v.trim() ? undefined : '至少给一个词'),
    });
    if (!pick || !pick.trim()) return;

    const limit = vscode.workspace.getConfiguration('ravel').get('heapHardLimit');
    runToChannel(dll, ['test', pick.trim()], '测试', limit ? { DOTNET_GCHeapHardLimit: limit } : undefined);
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
 * 把当前文件**去糖之后**的样子摊出来:C# 那边 `Lowering` 跑完、`AstPrinter` 印回 .rav 的那棵树。
 *
 * 走 `ast -s -o <临时目录>` 而不是读 stdout:
 *   * stdout 那份受**控制台码页**影响(中文 Windows 上是 CP936),注释里的中文会变乱码;
 *     `-o` 落盘的是 UTF-8;
 *   * `-o` 那一趟还会把印出来的东西**再解析一遍**自验 —— 印错了它会喊,而不是默默给你一份坏的。
 *
 * 出来的文件是临时目录里的 `…src.rav`,跟平时读的 .rav 长得一样(签名还是 `(a: int b: int)`)。
 */
async function showDesugared() {
    const file = await currentRavelFile();
    if (!file) return;

    const dll = await findRavelDll();
    if (typeof dll !== 'string') {
        vscode.window.showErrorMessage(dll.error);
        return;
    }

    // 每次都换一个目录：同一个路径重开时 VS Code 会拿出**已经开着的**那份旧文档，
    // 看着就像"去糖结果没变"。换个 URI 就没这个缓存问题（临时目录由系统回收）。
    const dir = path.join(os.tmpdir(), 'ravel-desugar', String(Date.now()));
    fs.mkdirSync(dir, { recursive: true });

    const code = await new Promise((resolve) => {
        cp.spawn('dotnet', [dll, 'ast', '-s', '-o', dir, file], { cwd: workspaceCwd() })
            .on('error', () => resolve(-1))
            .on('close', (c) => resolve(c));
    });

    // 落点按"相对当前目录"还原(见 AstDump),所以别猜路径,直接找那一份
    const produced = fs.readdirSync(dir, { recursive: true, withFileTypes: true })
        .filter((e) => e.isFile() && e.name.endsWith('.src.rav'))
        .map((e) => path.join(e.parentPath || e.path, e.name))[0];

    if (!produced) {
        vscode.window.showErrorMessage(`去糖失败（退出码 ${code}）—— 看 Ravel 输出面板里的报错。`);
        return;
    }

    const doc = await vscode.workspace.openTextDocument(produced);
    await vscode.window.showTextDocument(doc, {
        preview: true,
        viewColumn: vscode.ViewColumn.Beside,
    });
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

/**
 * 跳转定义 / 悬停:算"定义在哪"那半在 `definitions.js`(纯函数,node 能直接测),
 * 这里只负责**找文件、读文件**,再把它接到 VS Code 上。
 *
 * 为什么这件事在这门语言上特别好做:标准库全在 `lib/*.rav`,而且是**可读的 Ravel
 * 源码** —— 名字怎么写、定义在哪一行,翻开就有。不用类型推断、不用符号表。
 */
let libDocs;                  // `lib/*.rav` 的源码缓存:`[{ key, source }]`

async function collectDocs(document) {
    const here = { key: document.uri.toString(), source: document.getText() };
    if (!libDocs) {
        libDocs = [];
        const uris = await vscode.workspace.findFiles('**/lib/*.rav', '**/{obj,bin,node_modules}/**');
        for (const uri of uris) {
            try {
                const bytes = await vscode.workspace.fs.readFile(uri);
                libDocs.push({ key: uri.toString(), source: Buffer.from(bytes).toString('utf8') });
            } catch {
                // 读不动就跳过这一个 —— 为一份读不了的文件把整个跳转定义废掉不值当
            }
        }
    }
    return [here, ...libDocs];
}

/** 库改了就把缓存丢掉 —— 只在保存 `.rav` 时判一下,不必盯每一次按键 */
function invalidateLibDocs(document) {
    if (libDocs && document.uri.fsPath.includes(`${path.sep}lib${path.sep}`)) libDocs = undefined;
}

async function locate(document, position) {
    const ref = wordAt(document.getText(), document.offsetAt(position));
    if (!ref) return null;
    const docs = await collectDocs(document);
    const hit = findDefinition(ref, docs, document.uri.toString(), position.line);
    if (!hit) return null;
    return { hit, docs };
}

const definitionProvider = {
    async provideDefinition(document, position) {
        const found = await locate(document, position);
        if (!found) return undefined;
        const { hit } = found;
        return new vscode.Location(vscode.Uri.parse(hit.key), new vscode.Position(hit.line, hit.character));
    },
};

/**
 * 悬停:定义那一行原文 + (类/接口就)成员名单 + 定义在哪个文件第几行。
 *
 * 最后那行是给"要不要跳"用的 —— 跳过去之前就知道它在 `lib/seqs.rav` 第 12 行,
 * 比跳过去再发现不是想找的那个省一次来回。
 */
const hoverProvider = {
    async provideHover(document, position) {
        const found = await locate(document, position);
        if (!found) return undefined;
        const { hit, docs } = found;
        const doc = docs.find((d) => d.key === hit.key);
        if (!doc) return undefined;
        const md = new vscode.MarkdownString();
        md.appendCodeblock(lineOf(doc.source, hit.line).trim(), 'ravel');

        const ref = wordAt(document.getText(), document.offsetAt(position));
        const members = ref ? hints.classMembers(doc.source, ref.word) : null;
        if (members && members.length > 0) {
            const list = members.map((m) => '`' + m + '`').join(' ');
            md.appendMarkdown('\n\n**' + members.length + ' 个成员**:' + list);
        }

        md.appendMarkdown('\n\n' + shortKey(hit.key) + ':' + (hit.line + 1));
        return new vscode.Hover(md);
    },
};

/** 悬停里那个文件名:工作区里的写相对路径,别处的写 basename */
function shortKey(key) {
    const file = String(key).replace(/^file:\/\//, '');
    const folder = vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders[0];
    if (folder && file.startsWith(folder.uri.fsPath)) {
        return path.relative(folder.uri.fsPath, file).split(path.sep).join('/');
    }
    return path.basename(file);
}

/** 光标前面刚敲过 `.`(或者正在 `.` 后面那个词里) */
function memberPosition(document, position) {
    const line = document.getText(new vscode.Range(new vscode.Position(position.line, 0), position));
    return /[\p{L}\p{N}_]\.\s*[\p{L}\p{N}_]*$/u.test(line);
}

/**
 * 补全。分三种场合,而不是一股脑给同一张表:
 *
 *   * `using "` 后面 —— 给 `lib/` 里的文件名(这一处最容易写错,还得记名字);
 *   * `.` 后面 —— 给类体里的成员名(全文件 + 整个标准库,这是提示不是符号表);
 *   * 别处 —— 当前文件出现过的名字 + `using` 进来的文件里的名字 + 几句"怎么起头"。
 *
 * `using` 进来的那份只在真被引用时才算 —— 一个文件把整库名字都塞进来,
 * 候选里就全是自己没用到的,反而挑不动。
 */
const completionProvider = {
    async provideCompletionItems(document, position) {
        const text = document.getText();
        const offset = document.offsetAt(position);
        const lineBefore = text.slice(0, offset).split('\n').pop();

        if (/using\s+"[^"]*$/.test(lineBefore)) {
            const docs = await collectDocs(document);
            return docs
                .map((d) => String(d.key).split(/[\\/]/).pop())
                .filter((name) => name.endsWith('.rav'))
                .map((name) => {
                    const item = new vscode.CompletionItem(name, vscode.CompletionItemKind.File);
                    item.detail = 'lib/';
                    return item;
                });
        }

        const docs = await collectDocs(document);
        const here = docs[0];

        // 字符串、注释里不弹补全(上面 `using "` 那一处已经先接走了) ——
        // `codeOnly` 把那些位置抹成空格,原字符不是空格而这里是,就说明在里头
        const bare = hints.codeOnly(text);
        if (offset > 0 && /\s/.test(bare[offset - 1]) && !/\s/.test(text[offset - 1])) return undefined;

        if (memberPosition(document, position)) {
            const names = new Set(hints.allMembers(here.source));
            for (const d of docs.slice(1)) {
                for (const n of hints.allMembers(d.source)) names.add(n);
            }
            return [...names].map((n) => {
                const item = new vscode.CompletionItem(n, vscode.CompletionItemKind.Field);
                item.detail = '成员';
                return item;
            });
        }

        const items = [];
        const imported = hints.importsOf(here.source)
            .map((file) => docs.find((d) => d.key !== here.key && String(d.key).endsWith('/' + file)))
            .filter(Boolean);
        const libNames = new Set();
        for (const d of imported) for (const n of hints.namesIn(d.source, null)) libNames.add(n);

        for (const n of hints.namesIn(here.source, libNames)) {
            const def = hints.definitionLines(here.source, n)[0];
            const kind = def && /::=/.test(def.text)
                ? vscode.CompletionItemKind.Class
                : def && /=>/.test(def.text)
                    ? vscode.CompletionItemKind.Function
                    : vscode.CompletionItemKind.Variable;
            const item = new vscode.CompletionItem(n, kind);
            if (def) item.detail = def.text.trim().slice(0, 80);
            items.push(item);
        }
        for (const n of libNames) {
            const item = new vscode.CompletionItem(n, vscode.CompletionItemKind.Function);
            item.detail = 'lib';
            items.push(item);
        }

        // 行首(或者这一行还什么都没有)才给"怎么起头",别在表达式中间插 `class`
        if (/^\s*[\p{L}\p{N}_]*$/u.test(lineBefore)) {
            for (const st of hints.STARTERS) {
                const item = new vscode.CompletionItem(st.label, vscode.CompletionItemKind.Snippet);
                item.insertText = new vscode.SnippetString(st.insertText);
                item.detail = st.detail;
                item.sortText = st.sort;
                items.push(item);
            }
        }
        return items;
    },
};

/**
 * 签名提示:光标在某个调用的括号里时,把**定义那一行**的参数表摆出来。
 *
 * 名字查不到就什么都不显示,不猜。参数表是从源码那一行抠的,所以
 * `(f: function)` / `(init: object f: function)` 原样就对了。
 */
const signatureProvider = {
    async provideSignatureHelp(document, position) {
        const text = document.getText();
        const call = hints.callAt(text, document.offsetAt(position));
        if (!call) return undefined;

        const docs = await collectDocs(document);
        let def = null;
        for (const d of docs) {
            const hit = hints.definitionLines(d.source, call.name)[0];
            if (hit && /[=(]/.test(hit.text)) {
                def = hit;
                break;
            }
        }
        if (!def) return undefined;

        const ps = hints.paramsOfLine(def.text);
        if (!ps) return undefined;

        const sig = new vscode.SignatureInformation(call.name + ps.text, def.text.trim());
        sig.parameters = ps.params.map((p) => new vscode.ParameterInformation(p));
        const help = new vscode.SignatureHelp();
        help.signatures = [sig];
        help.activeSignature = 0;
        help.activeParameter = ps.params.length === 0 ? 0 : Math.min(call.index, ps.params.length - 1);
        return help;
    },
};

/** 大纲:文件里定义了什么;`::=` 是类/接口,带 `=>` 的是函数 */
const symbolProvider = {
    provideDocumentSymbols(document) {
        const source = document.getText();
        const lines = source.split('\n');
        return indexDefinitions(source).map((d) => {
            const text = lines[d.line] ?? '';
            const kind = /::=/.test(text)
                ? vscode.SymbolKind.Class
                : /=>/.test(text)
                    ? vscode.SymbolKind.Function
                    : vscode.SymbolKind.Variable;
            const range = new vscode.Range(d.line, d.character, d.line, text.length);
            return new vscode.DocumentSymbol(d.name, text.trim().slice(0, 80), kind, range, range);
        });
    },
};

function activate(context) {
    context.subscriptions.push(
        vscode.commands.registerCommand('ravel.runFile', runFile),
        vscode.commands.registerCommand('ravel.runFileInTerminal', runFileInTerminal),
        vscode.commands.registerCommand('ravel.runTests', runTests),
        vscode.commands.registerCommand('ravel.openRepl', openRepl),
        vscode.commands.registerCommand('ravel.showDesugared', showDesugared),
        vscode.languages.registerFoldingRangeProvider({ language: 'ravel' }, foldingProvider),
        vscode.languages.registerDefinitionProvider({ language: 'ravel' }, definitionProvider),
        vscode.languages.registerHoverProvider({ language: 'ravel' }, hoverProvider),
        vscode.languages.registerCompletionItemProvider({ language: 'ravel' }, completionProvider, '.', '"'),
        vscode.languages.registerSignatureHelpProvider({ language: 'ravel' }, signatureProvider, '('),
        vscode.languages.registerDocumentSymbolProvider({ language: 'ravel' }, symbolProvider),
        vscode.workspace.onDidSaveTextDocument(invalidateLibDocs)
    );
}

function deactivate() {
    if (channel) channel.dispose();
}

module.exports = { activate, deactivate };
