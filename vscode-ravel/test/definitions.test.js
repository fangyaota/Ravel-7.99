'use strict';

// 跳转定义那半的用例(纯函数,不依赖 vscode):
//     node vscode-ravel/test/definitions.test.js

const { indexDefinitions, moduleNameOf, wordAt, findDefinition } = require('../definitions');

let failed = 0;
let passed = 0;

function check(name, got, want) {
    const g = JSON.stringify(got);
    const w = JSON.stringify(want);
    if (g === w) {
        passed++;
        console.log(`  ok   ${name}`);
    } else {
        failed++;
        console.log(`  FAIL ${name}\n       期望 ${w}\n       得到 ${g}`);
    }
}

// ── 扫定义 ──
const src = [
    '# 注释里的 := 不算',
    'readonly Seqs := (xs: object) => {',
    '    inner := 1',                       // 局部
    '    xs;',
    '}',
    'Point ::= dataclass {',
    '    x: int = 0',                      // 带注解的定义
    '}',
    'App ::= interface {',
    '    Run := () => { 0; }',             // 类成员
    '}',
    'cond : bool = true',                  // 带注解的顶层定义
    'a.b := 5',                            // **不是**定义(成员定义,行首是 a)
    'if { true; } { 1; } { 2; }',          // 不是定义
].join('\n');

check('扫出来的定义', indexDefinitions(src), [
    { name: 'Seqs', line: 1, character: 9, kind: 'def' },
    { name: 'inner', line: 2, character: 4, kind: 'def' },
    { name: 'Point', line: 5, character: 0, kind: 'def' },
    { name: 'x', line: 6, character: 4, kind: 'def' },
    { name: 'App', line: 8, character: 0, kind: 'def' },
    { name: 'Run', line: 9, character: 4, kind: 'def' },
    { name: 'cond', line: 11, character: 0, kind: 'def' },
]);

check('模块名', moduleNameOf('// x\nravel "Seqs"\n'), 'Seqs');
check('没有模块名', moduleNameOf('readonly a := 1\n'), null);
check('空模块名也算没有', moduleNameOf('ravel ""\n'), null);

// ── 光标底下那个名字 ──
check('词中间', wordAt('a := bcd + 1', 7), { word: 'bcd', qualifier: null, start: 5, end: 8 });
check('词的右边一格(点完最后一下)', wordAt('a := bcd + 1', 8), { word: 'bcd', qualifier: null, start: 5, end: 8 });
check('点在空白上', wordAt('a := bcd + 1', 4), null);
check('中文标识符', wordAt('名字 := 1', 1), { word: '名字', qualifier: null, start: 0, end: 2 });
check('带模块前缀', wordAt('x := Seqs.Map f', 9), { word: 'Seqs', qualifier: null, start: 5, end: 9 });
check('点后面那个', wordAt('x := Seqs.Map f', 11), { word: 'Map', qualifier: 'Seqs', start: 10, end: 13 });
check('两个点也只是取最后一段', wordAt('a.b.c', 4), { word: 'c', qualifier: 'b', start: 4, end: 5 });

// ── 落到哪一处 ──
const cur = [
    'f := () => {',
    '    n := 1',
    '    n;',                             // 光标在这儿:要跳到第 2 行那个 n,不是别处
    '}',
    'n := 99',                            // 文件后面还有一个同名
].join('\n');
const mod = 'ravel "Seqs"\nMap := (f: function) => { f; }\n';

const docs = [
    { key: 'cur.rav', source: cur },
    { key: 'lib/seqs.rav', source: mod },
];

check('本文件里的局部:取光标之前最近那处',
    findDefinition({ word: 'n' }, docs, 'cur.rav', 2), { key: 'cur.rav', line: 1, character: 4 });

check('本文件里没有就往后找',
    findDefinition({ word: 'Map', qualifier: 'Seqs' }, docs, 'cur.rav', 0),
    { key: 'lib/seqs.rav', line: 1, character: 0 });

check('带模块名只在那个模块里找',
    findDefinition({ word: 'n', qualifier: 'Seqs' }, docs, 'cur.rav', 0), null);

check('找不到就是找不到',
    findDefinition({ word: '没这个' }, docs, 'cur.rav', 0), null);

// ── 和语言对得上:这几行的形状是真从 lib/ 里抄的 ──
const real = [
    'readonly Seq := (xs: object) => { xs; }',
    'Editor ::= class {',
    '    Pages: list = [[]]',
    '    Lines := () => { Pages.At Z; }',
    '    init = () => { this; }',
    '}',
    'readonly while := (c: function body: function) => {',
    '    again := (x: int) => { x; }',
    '}',
].join('\n');

check('库里那几种形状都认得', indexDefinitions(real).map((d) => d.name),
    ['Seq', 'Editor', 'Pages', 'Lines', 'init', 'while', 'again']);

// `init = …` 是**赋值**那一类(`=` 在这里是"换掉继承来的那条"),得认出来,只是排在 `:=` 后面
check('init = 那条标成 assign',
    indexDefinitions(real).find((d) => d.name === 'init'),
    { name: 'init', line: 4, character: 4, kind: 'assign' });

check('== 不被当成赋值',
    indexDefinitions('x == 5\n').map((d) => d.name), []);

check('=> 不被当成赋值',
    indexDefinitions('(x: int) => { x; }\n').map((d) => d.name), []);

check('同一个名字 := 优先于 =',
    findDefinition({ word: 'v' }, [{ key: 'a.rav', source: 'v := 1\nv = 2\nv = 3\n' }], 'a.rav', 2),
    { key: 'a.rav', line: 0, character: 0 });

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
