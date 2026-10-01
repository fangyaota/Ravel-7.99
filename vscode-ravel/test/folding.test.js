'use strict';

// 折叠那半的用例(纯函数,不依赖 vscode):
//     node vscode-ravel/test/folding.test.js
// 判据要和词法器一致 —— 所以字符串 / 插值 / 注释里的括号都各有一条用例盯着。

const { foldingRanges } = require('../folding');

let failed = 0;
let passed = 0;

/** 把 `[起, 止]`(都含)与"注释块"两种期望分开写清楚 */
function check(name, text, expected) {
    const got = foldingRanges(text)
        .map((r) => `${r.start}-${r.end}:${r.kind}`)
        .sort();
    const want = expected.slice().sort();
    if (JSON.stringify(got) === JSON.stringify(want)) {
        passed++;
        console.log(`  ok   ${name}`);
    } else {
        failed++;
        console.log(`  FAIL ${name}\n       期望 ${JSON.stringify(want)}\n       得到 ${JSON.stringify(got)}`);
    }
}

// ── 大括号:跨行的才折 ──
check('多行块', 'C := class {\n    x: int = 1\n}\n', ['0-2:region']);

check('单行不折', 'C := class { x: int = 1 }\n', []);

check(
    '嵌套块各自成区间',
    'f := () => {\n    if { a; } {\n        b\n    } { 0; }\n}\n',
    ['0-4:region', '1-3:region'],
);

// ── 圆括号 / 方括号:长实参表、长表 ──
check('跨行实参表', 'f (\n    1\n    2\n)\n', ['0-3:region']);

check('跨行列表', 'xs := [\n    1\n    2\n]\n', ['0-3:region']);

// ── 字符串里的括号不算 ──
check('字符串里的花括号', 's := "{\n"\nt := {\n    1\n}\n', ['2-4:region']);

check('字符串里的转义引号', 's := "a\\"{\\\\"\nt := {\n    1\n}\n', ['1-3:region']);

// ── `${…}` 插值:里面是代码(能折),外层还是字符串 ──
check(
    '插值里的块',
    's := "${ f {\n    a\n} }"\n',
    ['0-2:region'],
);

check(
    '插值里的字符串',
    's := "${ "}" }"\nt := {\n    1\n}\n',
    ['1-3:region'],
);

// ── 注释 ──
check('注释里的花括号', '# {\nC := class {\n    x: int = 1\n}\n', ['1-3:region']);

check('连续整行注释成一段', '# 一\nd := 1\n# 二\n# 三\n# 四\n', ['2-4:comment']);

check('单行注释不折', '# 一\nd := 1\n', []);

check('region 那两行不卷进注释块', '#region\n# 一\n# 二\n#endregion\n', ['1-2:comment']);

// ── 混在一起:两块各算各的 ──
check(
    '块与注释块并存',
    'C := class {\n    # 说明一\n    # 说明二\n    x: int = 1\n}\n',
    ['0-4:region', '1-2:comment'],
);

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);
