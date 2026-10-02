'use strict';

// 「跳转定义」那半的**纯逻辑**(不依赖 vscode,node 能直接跑):
//     node vscode-ravel/test/definitions.test.js
//
// 喂它源码文本 + 光标偏移,它交回「定义在哪个文件、哪一行、哪一列」。
// 找文件、读文件那些留着 `extension.js` 去干 —— 和 `folding.js` 一个分法。
//
// **为什么这件事在这门语言上特别好做**:标准库全在 `lib/*.rav`,而且**是可读的
// Ravel 源码**;定义又只有几种固定的写法(见 `DEFINITION`)。不用类型推断、
// 不用符号表、不用语言服务器,按文本认就够了。换成库是编译产物的语言,这一步
// 得先有一个 LSP。

/** 名字用 `\p{L}\p{N}_` —— Ravel 的标识符可以是中文(`名字 := 1` 是合法的) */
const IDENT = /[\p{L}\p{N}_]/u;

/** 定义前面允许出现的修饰符,和语法文件里那张表同源 */
const MODIFIERS = 'readonly|public|private|protected|outdated|unreadable|by|core|thistype|block';
const NAME = '[\\p{L}\\p{N}_][\\p{L}\\p{N}_]*';

/**
 * 一行是不是定义,以及名字在第几列。
 *
 * 认三种:
 *     [修饰符…] 名字 := …        普通定义(含局部变量、类成员)
 *     [修饰符…] 名字 ::= …       类 / 接口 / 数据类型 / 模块那一族
 *     [修饰符…] 名字 : 类型 = …   带注解的定义(`cond : bool = c ()`)
 *
 * **`^` 锚在行首** —— 这一条同时挡掉了 `a.b := v`(成员定义):`a` 后面跟着 `.`,
 * 三个分支都不认,于是不会被当成"定义了 `b`"。
 */
const DEFINE = new RegExp(
    `^(\\s*(?:(?:${MODIFIERS})\\s+)*)(${NAME})\\s*(?:::?=|:\\s*[^=]+?=)`,
    'u',
);

/**
 * `名字 = 值`。**和 `:=` 分开认**,因为它的身份是**两可的**:
 *
 * * 类体里 `init = () => { … }` —— 那是**定义**(`=` 就是"换掉继承来的那条");
 * * 别处 `x = 5` —— 那是**赋值**,定义在别的地方。
 *
 * 文本上看不出是哪一种(要看在不在类体里,那得有嵌套信息),所以两条都收,
 * 但这一条标成 `assign`,`pick` 会优先挑 `def`。跳错了也还有个去处,比跳不动强。
 *
 * `=(?!=|>)` 是为了不把 `==`(比较)和 `=>`(lambda 箭头)当成赋值。
 */
const ASSIGN = new RegExp(
    `^(\\s*(?:(?:${MODIFIERS})\\s+)*)(${NAME})\\s*=(?!=|>)`,
    'u',
);

/** 模块名:`ravel "Seqs"` 那一句。没有(或者写的是 `""`)就是没有 */
function moduleNameOf(source) {
    const m = /^\s*ravel\s+"([^"]*)"/mu.exec(source);
    return m && m[1] ? m[1] : null;
}

/** 把一份源码里所有定义扫出来,按出现先后 */
function indexDefinitions(source) {
    const out = [];
    const lines = source.split('\n');
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trimStart();
        if (trimmed === '' || trimmed.startsWith('#')) continue;   // 注释行整行跳过
        const d = DEFINE.exec(line);
        if (d) { out.push({ name: d[2], line: i, character: d[1].length, kind: 'def' }); continue; }
        const a = ASSIGN.exec(line);
        if (a) out.push({ name: a[2], line: i, character: a[1].length, kind: 'assign' });
    }
    return out;
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
 * 找到名字定义在哪儿。
 *
 * `docs` 是 `[{ key, source }]`(`key` 是调用方认得的东西,比如文件路径);
 * `currentKey` 是光标所在那份,`preferLine` 是光标所在行 —— 两个都用来决定**优先看谁**。
 *
 * 优先级(和读代码时的直觉一致):
 *   1. `Seqs.Map` 这种带模块名的:**只在那个模块里找**,不去别的文件瞎撞;
 *   2. 裸名字:**当前文件优先**,而且优先**光标之前最近的那一处**(局部定义常有好几处,
 *      取第一个会跳到文件顶上那个同名的);
 *   3. 都找不到,再挨个模块文件找一遍。
 *
 * 交回 `{ key, line, character }`,找不到交回 `null`。
 */
function findDefinition(ref, docs, currentKey, preferLine) {
    if (!ref || !ref.word) return null;

    if (ref.qualifier) {
        const mod = docs.find((d) => moduleNameOf(d.source) === ref.qualifier);
        if (!mod) return null;
        const hit = pick(definitionsNamed(mod.source, ref.word), preferLine);
        return hit ? { key: mod.key, line: hit.line, character: hit.character } : null;
    }

    const here = docs.find((d) => d.key === currentKey);
    if (here) {
        const hit = pick(definitionsNamed(here.source, ref.word), preferLine, true);
        if (hit) return { key: here.key, line: hit.line, character: hit.character };
    }

    for (const d of docs) {
        if (d.key === currentKey) continue;
        const hit = pick(definitionsNamed(d.source, ref.word), preferLine);
        if (hit) return { key: d.key, line: hit.line, character: hit.character };
    }
    return null;
}

/**
 * 同名有好几处时挑哪一个。
 *
 * `sameFile` 为真时优先"光标之前最近的那一处" —— 局部变量、循环里反复定义的
 * 辅助函数都是这样:取文件里第一处会把人送到一个八竿子打不着的地方去。
 * 本文件里没有更靠前的,就退回第一处。
 */
function pick(hits, preferLine, sameFile) {
    if (hits.length === 0) return null;
    // `:=` 那一族优先 —— `=` 那条只可能是赋值,跳过去是"退而求其次"
    const defs = hits.filter((h) => h.kind !== 'assign');
    const pool = defs.length > 0 ? defs : hits;
    if (sameFile && typeof preferLine === 'number') {
        for (let i = pool.length - 1; i >= 0; i--) {
            if (pool[i].line <= preferLine) return pool[i];
        }
    }
    return pool[0];
}

/** 定义那一行原文 —— 悬停时显示给人看 */
function lineOf(source, line) {
    return source.split('\n')[line] ?? '';
}

module.exports = { indexDefinitions, definitionsNamed, moduleNameOf, wordAt, findDefinition, lineOf };
