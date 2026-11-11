'use strict';

// 补全/签名那半的用例(纯函数,不依赖 vscode):
//     node vscode-ravel/test/hints.test.js

const {
    codeOnly, namesIn, importsOf, paramsOfLine, callAt, classMembers, allMembers,
} = require('../hints');

let failed = 0;
let passed = 0;

function check(name, got, want) {
    const g = JSON.stringify(got);
    const w = JSON.stringify(want);
    if (g === w) {
        passed++;
        return;
    }
    failed++;
    console.log(`FAIL ${name}\n  得到 ${g}\n  期望 ${w}`);
}

// 注释和字符串里的字不算名字,不然补全里全是说明文字
check('codeOnly 长度不变',
    codeOnly('a := 1  # 说明\nc := "x"').length,
    'a := 1  # 说明\nc := "x"'.length);
check('codeOnly 抹掉注释内容', /\S/.test(codeOnly('a := 1  # 说明').split('#')[1] ?? ''), false);
check('codeOnly 抹掉字符串内容', codeOnly('a := "说明"').includes('说明'), false);

check('namesIn 不算字符串里的字', namesIn('a := "b c"\nd := e', null), ['a', 'd', 'e']);
check('namesIn 跳过数字开头的词', namesIn('5n + x', null), ['x']);
check('namesIn 认中文', namesIn('名字 := 1', null), ['名字']);
check('namesIn exclude', namesIn('a := b', new Set(['a'])), ['b']);

check('importsOf', importsOf('ravel "A"\nusing "seqs.rav"\nusing "x.rav"'), ['seqs.rav', 'x.rav']);

check('paramsOfLine 空表', paramsOfLine('f := () => { 0; }'), { text: '()', params: [] });
check('paramsOfLine 空格分隔', paramsOfLine('Map := (init: object f: function) => { 0; }').params,
    ['init: object', 'f: function']);
check('paramsOfLine 嵌套括号不切', paramsOfLine('g := (a: (x, y) => z b: int) => { 0; }').params,
    ['a: (x, y) => z', 'b: int']);
check('paramsOfLine 逗号分隔也认', paramsOfLine('h := (a: int, b: int) => { 0; }').params,
    ['a: int', 'b: int']);

check('callAt 第二个实参', callAt('print (g 3, 4)\n', 'print (g 3, '.length),
    { name: 'print', open: 6, index: 1 });
check('callAt 不在调用里', callAt('a := 1\n', 6), null);

check('classMembers', classMembers('C ::= class {\n    x := 1\n    m := () => { 0; }\n}', 'C'),
    ['x', 'm']);
check('classMembers 找不到', classMembers('C ::= class { x := 1 }', 'D'), null);
check('allMembers 并两个类',
    allMembers('A ::= class { a := 1 }\nB ::= class { b := 2 }').sort(),
    ['a', 'b']);

console.log(`\n${passed} passed, ${failed} failed`);
process.exit(failed === 0 ? 0 : 1);
