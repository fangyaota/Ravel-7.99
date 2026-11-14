'use strict';

// 跳转定义那半的用例(纯函数,不依赖 vscode):
//     node vscode-ravel/test/definitions.test.js

const { indexDefinitions, moduleNameOf, wordAt, findDefinition, findDefinitions, classBlocks } = require('../definitions');

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
    { name: 'xs', line: 1, character: 18, kind: 'param' },
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

check('库里那几种形状都认得(含参数)', indexDefinitions(real).map((d) => d.name),
    ['Seq', 'xs', 'Editor', 'Pages', 'Lines', 'init', 'while', 'c', 'body', 'again', 'x']);

// `init = …` 是**赋值**那一类(`=` 在这里是"换掉继承来的那条"),得认出来,只是排在 `:=` 后面
check('init = 那条标成 assign',
    indexDefinitions(real).find((d) => d.name === 'init'),
    { name: 'init', line: 4, character: 4, kind: 'assign' });

check('== 不被当成赋值',
    indexDefinitions('x == 5\n').map((d) => d.name), []);

check('=> 后面不是赋值,前面的括号里是参数',
    indexDefinitions('(x: int) => { x; }\n').map((d) => d.name), ['x']);

check('同一个名字 := 优先于 =',
    findDefinition({ word: 'v' }, [{ key: 'a.rav', source: 'v := 1\nv = 2\nv = 3\n' }], 'a.rav', 2),
    { key: 'a.rav', line: 0, character: 0 });

// ── 这几处也是"变量":参数、推导式的循环变量、解构 ──
check('多参数 + 带类型的那格不算名字',
    indexDefinitions('f := (a b: int c) => { a; }').map((d) => d.name),
    ['f', 'a', 'b', 'c']);

check('模式参数:解构出来的都是名字',
    indexDefinitions('[[k v] for [k v] in [[1 2] [3 4]]]').map((d) => d.name),
    ['k', 'v']);

check('推导式:for 后面那格是定义',
    indexDefinitions('xs := [x * 2 for x in [1 2 3] if (x > 1)]').map((d) => d.name),
    ['xs', 'x']);

check('推导式:字典那两格',
    indexDefinitions('d := {k -> v for [k v] in [[1 2]]}').map((d) => d.name),
    ['d', 'k', 'v']);

check('解构赋值:每一格都是定义',
    indexDefinitions('[a b] := Pair 1 2').map((d) => d.name),
    ['a', 'b']);

check('解构 + 带注解',
    indexDefinitions('[a: int b] := Pair 1 2').map((d) => d.name),
    ['a', 'b']);

check('一行两句都要认',
    indexDefinitions('a := 1; b := 2').map((d) => d.name),
    ['a', 'b']);

check('字符串里的 := 不算',
    indexDefinitions('s := "x := 1"').map((d) => d.name),
    ['s']);

check('三引号里的 := 也不算',
    indexDefinitions('t := """\ny := 2\n"""\n').map((d) => d.name),
    ['t']);

check('注释里的 for … in 不算',
    indexDefinitions('# [x for x in xs]').map((d) => d.name),
    []);

check('守卫位里的名字是参数',
    indexDefinitions('g := (n > 0) => { n; }').map((d) => d.name),
    ['g', 'n']);

// ── 同名好几处:全交出去,最近的排第一 ──
const many = [
    'v := 0',
    'f := () => {',
    '    v := 1',
    '    v;',
    '}',
].join('\n');
const hs = findDefinitions({ word: 'v' }, [{ key: 'm.rav', source: many }], 'm.rav', 3);
check('多候选:光标之前最近那处排第一', hs[0], { key: 'm.rav', line: 2, character: 4 });
check('多候选:剩下的也交出去', hs.map((h) => h.line), [2, 0]);


// ── 成员字段:类体里的那几格 ──
const cls = [
    'Point ::= dataclass {',
    '    x: int = 0',
    '    y: int = 0',
    '    len := () => { x; }',
    '    by tag := _',
    '    show := () => {',
    '        print (self.x)',
    '        print (this.y)',
    '        print (self.len)',
    '    }',
    '}',
    'p := Point ()',
    'q := Point ()',
    'print (p.x)',
    'print (self.x)',
].join('\n');

const clsDocs = [{ key: 'cur.rav', source: cls }];

check('类体按 {} 配平切出来', classBlocks(cls).map((b) => [b.name, b.line]), [['Point', 0]]);

check('p.x:顺着 p 的定义找到类,再找成员 x',
    findDefinition({ word: 'x', qualifier: 'p' }, clsDocs, 'cur.rav', 13),
    { key: 'cur.rav', line: 1, character: 4 });

check('p.y:同一类里的另一个字段',
    findDefinition({ word: 'y', qualifier: 'p' }, clsDocs, 'cur.rav', 13),
    { key: 'cur.rav', line: 2, character: 4 });

check('q.len:方法也是成员',
    findDefinition({ word: 'len', qualifier: 'q' }, clsDocs, 'cur.rav', 13),
    { key: 'cur.rav', line: 3, character: 4 });

check('q.tag:槽(by)也是成员',
    findDefinition({ word: 'tag', qualifier: 'q' }, clsDocs, 'cur.rav', 13),
    { key: 'cur.rav', line: 4, character: 7 });

check('self.x:去外面那层类体里找',
    findDefinition({ word: 'x', qualifier: 'self' }, clsDocs, 'cur.rav', 6),
    { key: 'cur.rav', line: 1, character: 4 });

check('this.y:别名一样认',
    findDefinition({ word: 'y', qualifier: 'this' }, clsDocs, 'cur.rav', 7),
    { key: 'cur.rav', line: 2, character: 4 });

check('self.len:方法也认',
    findDefinition({ word: 'len', qualifier: 'self' }, clsDocs, 'cur.rav', 8),
    { key: 'cur.rav', line: 3, character: 4 });

check('认不出类名:交回 <builtin>',
    findDefinition({ word: 'x', qualifier: 'nope' }, clsDocs, 'cur.rav', 13),
    { key: '<builtin>', line: 0, character: 0, builtin: true, name: 'x' });

check('类里没有这个成员:也交回 <builtin>',
    findDefinition({ word: 'zz', qualifier: 'p' }, clsDocs, 'cur.rav', 13),
    { key: '<builtin>', line: 0, character: 0, builtin: true, name: 'zz' });

check('内置成员(Length 这种)标成 <builtin>',
    findDefinitions({ word: 'Length', qualifier: 'p' }, clsDocs, 'cur.rav', 13),
    [{ key: '<builtin>', line: 0, character: 0, builtin: true, name: 'Length' }]);

check('self 在类体外什么也不给',
    findDefinition({ word: 'x', qualifier: 'self' }, clsDocs, 'cur.rav', 14), null);

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
