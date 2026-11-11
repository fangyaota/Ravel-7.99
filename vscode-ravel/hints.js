'use strict';

// 「补全 / 签名提示 / 大纲」那半的**纯逻辑**(不依赖 vscode,node 能直接跑):
//     node vscode-ravel/test/hints.test.js
//
// 和 definitions.js 一个分法:这里只认文本,读文件、找 `lib/` 那些留给 extension.js。
//
// **为什么能只靠文本**:这门语言没有关键字(`Syntax/TokenType.cs` 第一行就写着
// "Ravel 没有关键字,while/if 等是内置函数"),定义又只有几种固定写法 ——
// 补全要的那点信息(这个名字是什么、参数几个、类里有什么)全在源码文本里。

const { indexDefinitions } = require('./definitions');

/** 标识符字符集:和 definitions.js 一样,Ravel 的标识符可以是中文 */
const IDENT = /[\p{L}\p{N}_]/u;

/** 语句可以怎么起头 —— 这几句是**上下文词**,不是关键字,所以只在行首提示 */
const STARTERS = [
    { label: 'ravel "…"', insertText: 'ravel "${1:Name}"', detail: '给这个文件一个模块名' },
    { label: 'using "…"', insertText: 'using "${1:file.rav}"', detail: '把另一个文件的名字拿进来' },
    { label: 'class 名字 { … }', insertText: '${1:Name} ::= class {${0}}', detail: '定义一个类' },
    { label: 'interface 名字 { … }', insertText: '${1:IName} ::= interface {${0}}', detail: '定义一个接口' },
    { label: 'impl (接口 类型 { … })', insertText: 'impl (${1:IName} ${2:Type} {${0}})', detail: '给一个类型实现接口' },
    { label: '两个块(if 那种写法)', insertText: '${1:条件} {${2}} {${0}}', detail: '`if` 不是关键字:条件后面跟两个块' },
    { label: 'try { … } (e: Exception) => { … }', insertText: 'try {${1}} (${2:e}: Exception) => {${0}}', detail: '接住错误' },
];

/**
 * 去掉注释和字符串**内容** —— 补全不该把说明里的词、字符串里的字当成名字。
 * 长度和换行位置原样保留,这样算出来的偏移还能对回去。
 */
function codeOnly(source) {
    const out = [];
    let i = 0;
    let mode = 'code';   // code | line | string | char | triple
    while (i < source.length) {
        const c = source[i];
        if (mode === 'code') {
            if (c === '#') { mode = 'line'; out.push(' '); i++; continue; }
            if (c === '"') { mode = source.startsWith('"""', i) ? 'triple' : 'string'; out.push(' '); i++; continue; }
            if (c === "'") { mode = 'char'; out.push(' '); i++; continue; }
            out.push(c);
            i++;
            continue;
        }
        if (mode === 'line') {
            if (c === '\n') { mode = 'code'; out.push('\n'); } else out.push(' ');
            i++;
            continue;
        }
        if (mode === 'triple') {
            if (source.startsWith('"""', i)) { mode = 'code'; i += 3; out.push('   '); continue; }
            out.push(c === '\n' ? '\n' : ' ');
            i++;
            continue;
        }
        if (c === '\\') { out.push('  '); i += 2; continue; }
        const close = mode === 'string' ? '"' : "'";
        if (c === close) { mode = 'code'; out.push(' '); i++; continue; }
        out.push(c === '\n' ? '\n' : ' ');
        i++;
    }
    return out.join('');
}

/** 这份源码里出现过的名字,按第一次出现的先后;`exclude` 里的不要 */
function namesIn(source, exclude) {
    const bare = codeOnly(source);
    const seen = new Map();
    let i = 0;
    while (i < bare.length) {
        if (!IDENT.test(bare[i])) { i++; continue; }
        const start = i;
        while (i < bare.length && IDENT.test(bare[i])) i++;
        const name = bare.slice(start, i);
        if (/^\p{N}/u.test(name)) continue;          // `5n` 这种开头是数字的是数,不是名字
        if (exclude && exclude.has(name)) continue;
        if (!seen.has(name)) seen.set(name, start);
    }
    return [...seen.keys()];
}

/** `using "x.rav"` 引进来哪些文件名 */
function importsOf(source) {
    const out = [];
    const re = /^\s*using\s+"([^"]*)"/gmu;
    let m;
    while ((m = re.exec(source))) out.push(m[1]);
    return out;
}

/** 从 `open`(左括号)找到配对的右括号,找不到 -1 */
function matchParen(text, open) {
    let depth = 0;
    for (let i = open; i < text.length; i++) {
        if (text[i] === '(') depth++;
        else if (text[i] === ')') { depth--; if (depth === 0) return i; }
    }
    return -1;
}

/**
 * 参数表怎么切。
 *
 * Ravel 的参数是**空格分隔**的(`(init: object f: function)`),不是逗号;
 * 但类型本身可以带空格和逗号(`(a: (x, y) => z b: int)`),所以两条都要认:
 *
 *   * 顶层有逗号 → 按逗号切;
 *   * 没有逗号 → 只在"下一个 `名字:`"前面那一格切,别的空格都算类型的;
 *   * 连冒号都没有(`(x y)`)→ 整表算一个,标签仍原样,不会骗人。
 */
function splitParams(text) {
    if (text.trim() === '') return [];
    const parts = [];
    let depth = 0;
    let cur = '';
    for (const c of text) {
        if ('([{'.includes(c)) depth++;
        else if (')]}'.includes(c)) depth--;
        if (c === ',' && depth === 0) { parts.push(cur.trim()); cur = ''; continue; }
        cur += c;
    }
    if (cur.trim()) parts.push(cur.trim());
    if (parts.length > 1) return parts;

    const one = parts[0] ?? '';
    const split = one.split(/(?<=[^\s])\s+(?=[\p{L}\p{N}_]+\s*:)/u).map((s) => s.trim()).filter(Boolean);
    return split.length > 0 ? split : [one];
}

/** 定义那一行的参数表:`(f: function)` 里那一截,和切开的参数;没有括号就 null */
function paramsOfLine(line) {
    const open = line.indexOf('(');
    if (open < 0) return null;
    const close = matchParen(line, open);
    if (close < 0) return null;
    return { text: line.slice(open, close + 1), params: splitParams(line.slice(open + 1, close)) };
}

/** 名字 → 它定义的那一行(这份源码里;重名的取第一条,和"就近生效"差不多) */
function definitionLines(source, name) {
    const lines = source.split('\n');
    return indexDefinitions(source)
        .filter((d) => d.name === name)
        .map((d) => ({ line: d.line, kind: d.kind, text: lines[d.line] ?? '' }));
}

/**
 * 光标所在那个调用:交回 `{ name, index }`,`index` 是正在填第几个实参(从 0 起)。
 *
 * 从光标往回数括号,找到**还没闭合的那个 `(`**,再往左读被调的名字;
 * 这一段里的顶层逗号数就是参数序号。
 */
function callAt(source, offset) {
    const text = codeOnly(source);
    let depth = 0;
    for (let i = offset - 1; i >= 0; i--) {
        const c = text[i];
        if (c === ')') depth++;
        else if (c === '(') {
            if (depth === 0) {
                let j = i - 1;
                while (j >= 0 && /\s/.test(text[j])) j--;
                const end = j + 1;
                while (j >= 0 && IDENT.test(text[j])) j--;
                const name = text.slice(j + 1, end);
                return name ? { name, open: i, index: countArgs(text, i + 1, offset) } : null;
            }
            depth--;
        }
    }
    return null;
}

/** `from` 到 `to` 之间有几个顶层逗号 */
function countArgs(text, from, to) {
    let depth = 0;
    let n = 0;
    for (let i = from; i < to && i < text.length; i++) {
        const c = text[i];
        if ('([{'.includes(c)) depth++;
        else if (')]}'.includes(c)) depth--;
        else if (c === ',' && depth === 0) n++;
    }
    return n;
}

/**
 * 类/接口体里的成员名 —— 悬停一个类名时列出来,补全 `.` 时也能用它。
 *
 * 只认"名字 ::= class/interface"这一种起头,嵌套结构里再同名也不管:
 * 这是提示,不是符号表。名字不拼进正则(中文名拼进去会出乱子),扫出来再比。
 */
function classMembers(source, name) {
    const text = codeOnly(source);
    const re = /([\p{L}\p{N}_]+)\s*::=\s*(?:class|interface)/gu;
    let m;
    while ((m = re.exec(text))) {
        if (m[1] !== name) continue;
        const open = text.indexOf('{', m.index + m[0].length);
        if (open < 0) return null;
        let depth = 0;
        let close = text.length;
        for (let i = open; i < text.length; i++) {
            if (text[i] === '{') depth++;
            else if (text[i] === '}') { depth--; if (depth === 0) { close = i; break; } }
        }
        return namesIn(text.slice(open + 1, close), null);
    }
    return null;
}

/** 所有类/接口体里的成员名并成一堆 —— `.` 后面那份候选 */
function allMembers(source) {
    const text = codeOnly(source);
    const re = /([\p{L}\p{N}_]+)\s*::=\s*(?:class|interface)/gu;
    const out = new Set();
    let m;
    while ((m = re.exec(text))) {
        for (const x of classMembers(text, m[1]) ?? []) out.add(x);
    }
    return [...out];
}

module.exports = {
    STARTERS,
    codeOnly,
    namesIn,
    importsOf,
    paramsOfLine,
    definitionLines,
    callAt,
    classMembers,
    allMembers,
    matchParen,
};
