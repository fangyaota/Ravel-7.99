'use strict';

// 「跳转定义」那半的**纯逻辑**(不依赖 vscode,node 能直接跑):
//     node vscode-ravel/test/definitions.test.js
//
// 喂它源码文本 + 光标偏移,它交回「定义在哪个文件、哪一行、哪一列」。
// 找文件、读文件那些留着 `extension.js` 去干 —— 和 `folding.js` 一个分法。
//
// **为什么这件事在这门语言上特别好做**:标准库全在 `lib/*.rav`,而且**是可读的
// Ravel 源码**;绑名字的位置又只有几种固定的写法(见 `indexDefinitions`)。不用
// 类型推断、不用符号表、不用语言服务器,按文本认就够了。换成库是编译产物的语言,
// 这一步得先有一个 LSP。
//
// **"变量"包括哪些**:`x := …`、`x ::= …`、`x : T = …`、`x = …`(类体里是"换掉继承
// 来的那条")、lambda 的参数、推导式 `for` 后面那格、解构 `[a b] := …`。这几处都是
// **绑名字的位置**;漏掉任何一处,读代码时就有一半的变量跳不动。

/** 名字用 `\p{L}\p{N}_` —— Ravel 的标识符可以是中文(`名字 := 1` 是合法的) */
const IDENT = /[\p{L}\p{N}_]/u;

/** 定义前面允许出现的修饰符,和 `Runtime/Attr.cs` 的 `Attr.All` 同源 */
const MODIFIERS = 'readonly|public|private|protected|internal|outdated|unreadable|by|core|local';
const NAME = '[\\p{L}_][\\p{L}\\p{N}_]*';
const NAME_RE = new RegExp(NAME, 'gu');

/** 模块名:`ravel "Seqs"` 那一句。没有(或者写的是 `""`)就是没有 */
function moduleNameOf(source) {
    const m = /^\s*ravel\s+"([^"]*)"/mu.exec(source);
    return m && m[1] ? m[1] : null;
}

/**
 * 把字符串内容与注释抹成空格,长度与换行位置原样保留。
 *
 * 所有规则都在抹过的文本上跑,但**名字也从抹过的文本里取**(标识符本身没被动过),
 * 偏移也一致 —— 于是后面算出来的行列可以直接用。
 *
 * 三引号要先于单引号判:`"""` 里的那个 `"` 不该把状态机带出字符串。
 */
function mask(source) {
    const out = source.split('');
    const n = source.length;
    const blank = (a, b) => {
        for (let j = a; j < b; j++) if (out[j] !== '\n') out[j] = ' ';
    };
    let i = 0;
    while (i < n) {
        const c = source[i];
        if (c === '#') {
            const e = source.indexOf('\n', i);
            const end = e < 0 ? n : e;
            blank(i, end);
            i = end;
            continue;
        }
        if (source.startsWith('"""', i)) {
            const e = source.indexOf('"""', i + 3);
            const end = e < 0 ? n : e + 3;
            blank(i, end);
            i = end;
            continue;
        }
        if (c === '"' || c === "'") {
            let j = i + 1;
            while (j < n && source[j] !== c && source[j] !== '\n') {
                if (source[j] === '\\') j++;
                j++;
            }
            blank(i, Math.min(j + 1, n));
            i = j + 1;
            continue;
        }
        i++;
    }
    return out.join('');
}

/** `at` 左边紧挨着的那个名字(中间可以有空格);前面是 `.` 的算成员访问,不算定义 */
function nameBefore(text, at) {
    let j = at - 1;
    while (j >= 0 && (text[j] === ' ' || text[j] === '\t')) j--;
    if (j < 0 || !IDENT.test(text[j])) return null;
    const end = j + 1;
    while (j >= 0 && IDENT.test(text[j])) j--;
    const start = j + 1;
    if (start > 0 && text[start - 1] === '.') return null;
    return { name: text.slice(start, end), start };
}

/** `from` 左边最近的 `[`,配平后交回中间那段(解构、模式用) */
function bracketBack(text, from) {
    let depth = 0;
    for (let i = from; i >= 0; i--) {
        const c = text[i];
        if (c === ']') depth++;
        else if (c === '[') {
            depth--;
            if (depth === 0) return { inner: text.slice(i + 1, from), start: i + 1 };
        } else if (c === '(' && depth > 0) {
            return null;                     // `(` 先出现说明这对括号没闭合,别硬猜
        }
    }
    return null;
}

/** `from` 左边最近的 `(`,配平后交回中间那段(lambda 参数表用) */
function parenBack(text, from) {
    let depth = 0;
    for (let i = from; i >= 0; i--) {
        const c = text[i];
        if (c === ')') depth++;
        else if (c === '(') {
            depth--;
            if (depth === 0) return { inner: text.slice(i + 1, from), start: i + 1 };
        }
    }
    return null;
}

/**
 * 参数表 / 模式里那些**绑名字**的格子。
 *
 * 规则只有一条:`:` 后面紧跟的那个名字是**类型**,其余都是绑定的(所以 `(x: int)`
 * 只贡献 `x`)。`(x > 0)` 这种守卫位置也照此办 —— `x` 是绑定的,`0` 不是名字。
 */
function bindNames(segment, base, out, kind) {
    for (const m of segment.matchAll(NAME_RE)) {
        const at = m.index;
        const before = segment.slice(0, at).trimEnd();
        const prev = before.length ? before[before.length - 1] : '';
        if (prev === ':' || prev === '.') continue;      // 类型名、成员访问
        const after = segment.slice(at + m[0].length).trimStart();
        if (after.startsWith('(') || after.startsWith('::')) continue;   // 调用、类名
        out.push({ name: m[0], index: base + at, kind });
    }
}

/**
 * 扫一遍源码,把所有**绑名字**的位置按出现先后交回来。
 *
 * 认的位置:
 *     [修饰符…] 名字 := / ::= …      普通定义、类成员、类/接口/数据类型
 *     [修饰符…] 名字 : 类型 = …       带注解的定义
 *     [修饰符…] 名字 = …              `=` 两可(类体里是"换掉继承那条"),标 `assign`
 *     [a b] := …                     解构:每一格都是定义
 *     ( … ) => …                     参数
 *     for 模式 in 源                  推导式的循环变量
 *
 * `=` 那条单独标一个 kind:`order` 会把 `:=` 一族排在前面;文本上分不清类体还是
 * 普通赋值(要知道在不在类体里,那得有嵌套信息),所以宁可两条都收。
 */
function indexDefinitions(source) {
    const text = mask(source);
    const found = [];
    const add = (name, index, kind) => {
        if (name) found.push({ name, index, kind });
    };

    // ── `:=` 与 `::=` ──
    for (const m of text.matchAll(/::=|:=/g)) {
        const head = nameBefore(text, m.index);
        if (head) {
            add(head.name, head.start, 'def');
            continue;
        }
        // 左边是 `]` —— 解构:`[a b] := …`
        const b = bracketBack(text, m.index - 1);
        if (b) bindNames(b.inner, b.start, found, 'def');
    }

    // ── `名字 : 类型 = …`(类型那段不能有 `=` / `;` / 换行,也不能是 `::=`) ──
    for (const m of text.matchAll(/:\s*(?![=:])[^=;\n]+?=(?![=>])/g)) {
        const head = nameBefore(text, m.index);
        if (head) add(head.name, head.start, 'def');
    }

    // ── 裸 `=`:类体里是定义、别处是赋值;两种都收,标 `assign` ──
    for (const m of text.matchAll(/=(?![=>])/g)) {
        const prev = text.slice(Math.max(0, m.index - 1), m.index);
        if ('+-*/%!=<>:?'.includes(prev)) continue;       // += -= ??= == <= …
        const head = nameBefore(text, m.index);
        // `x: int = 0` 这里挨着 `=` 的名字是**类型**;真正的定义是 `x`,上面那条规则已经收了
        if (head && text.slice(0, head.start).trimEnd().slice(-1) !== ':') {
            add(head.name, head.start, 'assign');
        }
    }

    // ── lambda 的参数:紧挨着的 `( … ) =>` ──
    for (const m of text.matchAll(/=>/g)) {
        const p = parenBack(text, m.index - 1);
        if (p) bindNames(p.inner, p.start, found, 'param');
    }

    // ── 推导式的循环变量:`for 模式 in …` ──
    for (const m of text.matchAll(/\bfor\s+([^\n]*?)\s+in\b/gu)) {
        const seg = m[1];
        const base = m.index + m[0].indexOf(seg);
        bindNames(seg, base, found, 'def');
    }

    // 同一格被两条规则各收一次时(`[a b] :=` 也可能被裸 `=` 认出来)留一个,`def` 优先
    const byPos = new Map();
    for (const f of found) {
        const key = `${f.index}:${f.name}`;
        const old = byPos.get(key);
        if (!old || (old.kind === 'assign' && f.kind !== 'assign')) byPos.set(key, f);
    }
    const hits = [...byPos.values()].sort((a, b) => a.index - b.index);

    // 偏移换算成行列
    const lineStarts = [0];
    for (let i = 0; i < source.length; i++) if (source[i] === '\n') lineStarts.push(i + 1);
    const lineOfIndex = (index) => {
        let lo = 0;
        let hi = lineStarts.length - 1;
        while (lo < hi) {
            const mid = (lo + hi + 1) >> 1;
            if (lineStarts[mid] <= index) lo = mid;
            else hi = mid - 1;
        }
        return { line: lo, character: index - lineStarts[lo] };
    };
    return hits.map((h) => {
        const { line, character } = lineOfIndex(h.index);
        return { name: h.name, line, character, kind: h.kind };
    });
}

/** 某个名字在本文件里定义过的所有位置 */
function definitionsNamed(source, name) {
    return indexDefinitions(source).filter((d) => d.name === name);
}

/**
 * 光标底下的那个名字。
 *
 * 交回 `{ word, qualifier, start, end }`:`qualifier` 是 `Seqs.Map` 里那个 `Seqs`,
 * 裸名字就是 `null`。光标落在词尾右边一格也算命中(点完最后一个字符是常事)。
 */
function wordAt(text, offset) {
    if (typeof text !== 'string' || offset < 0 || offset > text.length) return null;

    let i = offset;
    if (i >= text.length || !IDENT.test(text[i])) i--;   // 退一格:光标常在词的右边
    if (i < 0 || !IDENT.test(text[i])) return null;

    let start = i;
    let end = i + 1;
    while (start > 0 && IDENT.test(text[start - 1])) start--;
    while (end < text.length && IDENT.test(text[end])) end++;

    // `Seqs.Map` —— 前面那个点、和点前面的模块名
    let qualifier = null;
    if (start > 0 && text[start - 1] === '.') {
        let q = start - 2;                      // 点前面那一格
        const qEnd = q + 1;
        while (q >= 0 && IDENT.test(text[q])) q--;
        if (q + 1 < qEnd) qualifier = text.slice(q + 1, qEnd);
    }

    return { word: text.slice(start, end), qualifier, start, end };
}

/**
 * 同名有好几处时怎么排:光标之前最近的优先,其次是它后面的,最后才是 `=` 那一类
 * (那是"退而求其次",跳过去可能只是一次赋值)。
 *
 * 为什么"最近"这么重要:局部变量、循环里反复定义的辅助函数都是这样 —— 取文件里
 * 第一处会把人送到八竿子打不着的地方。
 */
function order(hits, preferLine) {
    const defs = hits.filter((h) => h.kind !== 'assign');
    const assigns = hits.filter((h) => h.kind === 'assign');
    const pool = defs.length > 0 ? defs : hits;
    const line = typeof preferLine === 'number' ? preferLine : 0;
    const before = pool.filter((h) => h.line <= line).sort((a, b) => b.line - a.line || b.character - a.character);
    const after = pool.filter((h) => h.line > line).sort((a, b) => a.line - b.line || a.character - b.character);
    const rest = defs.length > 0 ? assigns : [];
    return [...before, ...after].concat(rest);
}

/**
 * 找到名字定义在哪儿 —— **所有**候选,按优先级排好。
 *
 * `docs` 是 `[{ key, source }]`(`key` 是调用方认得的东西,比如文件路径);
 * `currentKey` 是光标所在那份,`preferLine` 是光标所在行 —— 两个都用来决定**优先看谁**。
 *
 * 顺序:
 *   1. `Seqs.Map` 这种带模块名的:**只在那个模块里找**,不去别的文件瞎撞;
 *   2. 裸名字:**当前文件优先**(光标之前最近的在前,再是后面的);
 *   3. 本文件没有,再挨个模块文件找一遍(每个文件取一处)。
 *
 * 交回 `[{ key, line, character }]`,找不到交回空数组。调用方通常只取第一个,
 * 但"同名好几处"时把剩下的也交出去,VS Code 会摆成候选列表让人挑。
 */
function findDefinitions(ref, docs, currentKey, preferLine, limit = 8) {
    if (!ref || !ref.word) return [];

    if (ref.qualifier) {
        const mod = docs.find((d) => moduleNameOf(d.source) === ref.qualifier);
        if (!mod) return [];
        return order(definitionsNamed(mod.source, ref.word), preferLine)
            .slice(0, limit)
            .map((h) => ({ key: mod.key, line: h.line, character: h.character }));
    }

    const out = [];
    const here = docs.find((d) => d.key === currentKey);
    if (here) {
        for (const h of order(definitionsNamed(here.source, ref.word), preferLine)) {
            if (out.length >= limit) break;
            out.push({ key: here.key, line: h.line, character: h.character });
        }
    }

    for (const d of docs) {
        if (d.key === currentKey || out.length >= limit) continue;
        const hit = order(definitionsNamed(d.source, ref.word), preferLine)[0];
        if (hit) out.push({ key: d.key, line: hit.line, character: hit.character });
    }
    return out;
}

/** 只要一处(悬停、以及老调用方用的那个口子) */
function findDefinition(ref, docs, currentKey, preferLine) {
    return findDefinitions(ref, docs, currentKey, preferLine, 1)[0] ?? null;
}

/** 定义那一行原文 —— 悬停时显示给人看 */
function lineOf(source, line) {
    return source.split('\n')[line] ?? '';
}

module.exports = {
    indexDefinitions,
    definitionsNamed,
    moduleNameOf,
    wordAt,
    findDefinition,
    findDefinitions,
    lineOf,
};
