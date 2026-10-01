'use strict';

/**
 * Ravel 的折叠区间 —— **纯函数**(不碰 vscode),好拿 node 直接测(见 test/folding.test.js)。
 *
 * 折两样东西:
 *
 *  1. **跨行的 `{}` / `[]` / `()`** —— `{}` 是块/类/字面量的体,`()` 是长实参表,`[]` 是长表。
 *     没有 provider 时 VS Code 的兜底是**按缩进折**,而 Ravel 按括号分块:一个 `class {`
 *     和它后面的缩进行对不上,折出来的边界是错的。所以这一份是必需的,不是锦上添花。
 *  2. **连着两行以上的整行注释** —— 这门语言注释多,一段说明常常十几行,折起来才看得清结构。
 *
 * `#region` / `#endregion` 不在这儿管:`language-configuration.json` 的 `folding.markers`
 * 已经报了,两边都报会多出一份重叠的。
 *
 * 判据必须和词法器一致,否则字符串里的 `{` 会被当成块开始。所以扫描带着一个**模式栈**:
 * `code` / `string` / `interp`(`"${…}"` 里那段是代码,而它里面还能再写字符串,所以得压栈)。
 *
 * @param {string} text
 * @returns {{start: number, end: number, kind: string}[]} 行号 0 基,`end` 是**含**的
 */
function foldingRanges(text) {
    const lines = text.split(/\r\n|\r|\n/);
    const ranges = [];

    /** 模式栈,栈顶是当前模式 */
    const modes = ['code'];
    /** 还没合上的括号:`{line, ch, interp}` —— `interp` 记"这个 `{` 是不是 `${`" */
    const open = [];

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];

        for (let c = 0; c < line.length; c++) {
            const ch = line[c];
            const mode = modes[modes.length - 1];

            if (mode === 'string') {
                if (ch === '\\') { c++; continue; }              // 转义:下一个不当分隔符
                if (ch === '"') { modes.pop(); continue; }
                if (ch === '$' && line[c + 1] === '{') {         // 插值:进代码态,并替它记一个 `{`
                    modes.push('interp');
                    open.push({ line: i, ch: '{', interp: true });
                    c++;
                }
                continue;
            }

            // code / interp
            if (ch === '#') break;                               // 注释到行尾
            if (ch === '"') { modes.push('string'); continue; }

            if (ch === '{' || ch === '[' || ch === '(') {
                open.push({ line: i, ch, interp: false });
                continue;
            }

            if (ch === '}' || ch === ']' || ch === ')') {
                const top = open.pop();
                if (!top) continue;                              // 多出来的右括号:交给词法器去报
                if (top.interp) modes.pop();                     // `${…}` 收尾,回字符串
                if (top.line < i) ranges.push({ start: top.line, end: i, kind: 'region' });
            }
        }

        // 行末:字符串与注释不跨行,把它们收回去(插值那层会跨行,留着)
        if (modes[modes.length - 1] === 'string') modes.pop();
    }

    ranges.push(...commentBlocks(lines));

    // 同起同止的只留一份:`"${ f { … } }"` 里插值那个 `{` 与 `f {` 会折出同一段,
    // 摆两份给 VS Code 没意义(面板上还是那一个三角)。
    const seen = new Set();
    return ranges.filter((r) => {
        const key = `${r.start}-${r.end}`;
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
    });
}

/**
 * 连着两行以上的整行注释 —— 一段折起来。
 * `#region` / `#endregion` 那两行不参与(它们由 markers 管,卷进来会多一份重叠的)。
 */
function commentBlocks(lines) {
    const out = [];
    let start = -1;

    const flush = (endExclusive) => {
        if (start >= 0 && endExclusive - start >= 2) {
            out.push({ start, end: endExclusive - 1, kind: 'comment' });
        }
        start = -1;
    };

    for (let i = 0; i < lines.length; i++) {
        const t = lines[i].trim();
        const isComment = t.startsWith('#') && !/^#\s*(end)?region\b/.test(t);
        if (isComment) {
            if (start < 0) start = i;
        } else {
            flush(i);
        }
    }
    flush(lines.length);
    return out;
}

module.exports = { foldingRanges };
