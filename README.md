# Ravel

一门**解释型编程语言**,C# 写的,求值器是**显式持久帧栈**(不是递归、也不是 CPS 蹦床;
为什么这么选见 [ADR-0002](docs/adr/0002-explicit-stack-evaluator.md))。

它的野心不在"语法多漂亮",而在**引擎尽可能小**:凡是用这门语言自己写得出来的东西,
就不写在 C# 里 —— `if` / `while` / `foreach` / `try` / `match`、整套序列方法、
`?.` / `??` 那几条糖,全是库或者解析期的语法糖,引擎只管"求值"这一件事。
(有一处例外是**故意的**:报错的类型族在引擎里建 —— 报错的是引擎,分类也该它说了算。)
注释、文档、报错信息全是中文。

## 长什么样

```ravel
# 没有关键字:`if` 是个库函数(true/false 可调用,收两个块)
fact := (n: int) => { if { n <= 1; } { 1; } { n * (fact (n - 1)); } }
print (fact 5)                       # 120
```

```ravel
# 数据类:写几个字段,自动拿到打印、相等、按位置构造
using "dataclass.rav"
Point ::= dataclass {
    x: int = 0
    y: int = 0
}
p := Point 1 2
print (p.Text ())                    # Point(x=1, y=2)
print (p == (Point 1 2))             # true
print (p == (Point 3 4))             # false
```

```ravel
# 序列是接口给的:`Where` / `Map` / `Fold` … 全是 IEnumerable 的**默认实现**,
# 实现者只要交得出一个枚举器,整套白拿(列表、区间、生成器、Option、自己的类都算)
Users := ["ada" "bob" "cyd"]
print (Users.Where ((s: string) => { s == "bob"; }) |> .ToList ())    # [bob]
print (Users.Map ((s: string) => { s + "!"; }) |> .ToList ())        # [ada! bob! cyd!]
```

```ravel
# 没有 null:可能没有值的地方交回 Option,`??` 收口、`?.` 穿透
Users := ["ada" "bob" "cyd"]
print ((Users.TryFind ((s: string) => { s == "bob"; })) ?? "没人")   # bob
print ((Users.TryFind ((s: string) => { s == "zed"; })) ?? "没人")   # 没人

# 报错按类分,可以按类型接
try { 1 + "a"; } (e: TypeError) => { print ("类型错: " + e.Message); }
# 类型错: 运算符 '+' 不支持 String 操作数
```

（上面每一段都是真跑出来的输出。）

## 它特别在哪儿

**语言**

- **没有关键字**:`class` / `interface` / `if` / `while` / `foreach` 都是普通名字(库函数或别名),
  被覆盖掉语言就变样 —— 这是有意的。
- **柯里化**:`f a b` ≡ `(f a) b`,多参函数就是"一层层收一个";`_` 占位符、`+.1` 运算符节、
  `<|` 喂参、`|>` 括号糖,都是围绕这一条长出来的(`$` 只在字符串的插值里)。
- **原始字符串**:`"""…"""` 里一个字符都不动 —— 正则、Windows 路径那种满是反斜杠的文本
  画什么样写什么样;块按收尾引号的缩进对齐(照 C# 那套),所以能跟着代码正常缩进。
- **接口 + 默认实现**:`IEnumerable ::= interface IMonad { …一整套默认实现… }` ——
  谁 `impl` 了它,`Map` / `Where` / `Fold` / `Take` 立刻都有;`by X := …` 写的是"实现体不填时的样子"。
- **monad 进语法**:`do { x :< xs; y :< ys; [x y]; }` 折成 `.Bind` 链(`Option`、`IEnumerable`、
  `IoMonad` 都在这一族里)。
- **续延是一等值**:`callcc` 交回的东西 `typeof` 就是 `Continuation`,`while` 和生成器
  (`Generator`) 都是用它在库里写出来的。
- **数据类**:`dataclass { … }` 一个元类,自动注入 `Text ()` / `==` / 按位置构造。
- **枚举**:`enum { A := 1; B := 2 }` —— `myEnum.A.Value` 拿值、`.Name` 拿名字、
  `myEnum.Values ()` 列全、`myEnum.Of 2` 按值反查。同样是元类。
- **数据结构**:`Stack` / `Queue` / `Deque` / `Heap`(优先队列)/ `SortedDict` / `SortedSet` ——
  引擎里的类型,实现 `IEnumerable`,整套序列方法白拿。
- **出得了网**:`Http.Get "https://…"` 抓网页、`Http.Post` 发 JSON、`Http.Download` 流式落盘,
  响应对象带 `Text ()` / `Json ()` / `Header` —— 引擎只给"发一个请求"四条原语,策略都在库里。
- **错误按类分**:`TypeError` / `NameError` / `AttributeError` / `IndexError` / `KeyError` /
  `ZeroDivisionError` / `AssertionError` / `AccessError` / `ArgumentError` / `ValueError` /
  `IoError` / `RegexError`,都挂在 `Exception` 下,老的 `(e: Exception)` 一个没坏。

**工具**

- **REPL**:`dotnet out/ravel.dll` 不带参数就进,多页缓冲 + 高亮 + 光标(靠 Spectre.Console)。
- **golden 测试**:每个用例是一个 `.rav` 文件,末尾带 `# --- expected ---`,跑 `ravel test` 比对。
- **`--warn`**:少给一块的 `if`、`assert 条件` 忘了写消息 —— 这类"半成品函数被静默丢掉"的坑,
  开着它就当场提醒一句(见 `CONTEXT.md` 的「诊断」一节)。
- **VS Code 扩展**(`vscode-ravel/`):语法高亮(含 `"""` 原始字符串与 `'c'` 字符字面量)+
  折叠(按 `{}` 分块、注释段)+ **跳转定义 / 悬停**(`Ctrl+点击` 一个名字,跳到它定义的那一行)+
  跑当前文件 / 全量测试 / 开 REPL;文件上右键还有**在命令行中运行**(弹一个独立的系统命令行窗口,
  跑完留着,交互的脚本用它);括号匹配认区间的交叉括号(`[1..5)` / `(1..5]`)。
- **全中文**:注释、文档、报错、提交信息。

## 编译运行

```bash
rm -rf out && DOTNET_GCHeapHardLimit=0x10000000 dotnet publish Ravel.csproj -c Debug -o out
     # 先删 out/:增量 publish 有时不更新它,会跑到陈旧产物、得出假的结论
     # 指定 .csproj 而不是 .sln:"-o" 配 sln 会报 NETSDK1194

dotnet out/ravel.dll test                # 全量测试(有 FAIL 时退出码 1)
dotnet out/ravel.dll path/file.rav       # 单文件
dotnet out/ravel.dll path/file.rav a b   # 脚本名之后那些进 System.Args ()
dotnet out/ravel.dll                     # REPL(不带参数)

dotnet out/ravel.dll --warn file.rav     # 开「是不是忘了调用?」的提醒(stderr)
dotnet out/ravel.dll --warn test         # 整个用例库过一遍那个筛子(不影响比对结果)

# 用例里有个长循环压着 callcc,显式给 256MB 堆上限它才跑得稳(CI/VS Code 任务里都带着)
DOTNET_GCHeapHardLimit=0x10000000 dotnet out/ravel.dll test
```

**退出码**:测试有 FAIL、脚本报错、解释器自己有 bug —— 三种都是 1;`exit ""` 是
"什么都不说就结束" → 0。

## VS Code

打开仓库就有 `.vscode/` 里的任务:`Ctrl+Shift+B` 跑当前 `.rav`(先编译)、`F5` 跑当前文件
并可下断点;命令面板搜 `Ravel` 还有「运行全量测试」「打开 REPL」。它们调的都是
`bin/Debug/net10.0/ravel.dll`。

`vscode-ravel/` 那份扩展**不是开在窗口里就装上**的 —— VS Code 只认
`~/.vscode/extensions/` 里的那一份。要让它生效,把仓库里那个文件夹**接**过去
(用目录联接,改的是源码本身,以后改完重载窗口就见效,不必重装):

```bash
cd "%USERPROFILE%\.vscode\extensions"
cmd //c mklink /J "ravel.ravel-language-0.4.0" "D:\Codes\Ravel 7.99\vscode-ravel"
```

版本号在文件夹名里,`package.json` 里升了就顺手改这个名。装完
`Ctrl+Shift+P` → `Developer: Reload Window`。

**改扩展时最容易踩的坑**:明明源码改了、`git status` 里也看得见,VS Code 里却没动静 ——
多半是它加载的仍是 `~/.vscode/extensions/` 里的老副本。

## 仓库结构

```
Runtime/            求值器(按职责拆成多个 partial class 文件)+ 值类型 + 内置类
Runtime/Values/     每种值一个文件(IntVal / StringVal / ClassVal / RangeVal / …)
Lexer.cs  Parser*.cs  Ast.cs  Token*.cs        前端(词法 / 递归下降 / AST)
Program.cs          CLI 入口(REPL / test / 单文件)
Repl/               REPL 前端(多页缓冲、渲染、会话持久化)
Testing/            golden 测试运行器
Ravel.Structures/   **官方插件**之一:数据结构那一族 → `plugins/Ravel.Structures.dll`
Ravel.Extensions/   **官方扩展**:几个库的本机半边(Hash / Crypto / Net / Regex /
                    Sqlite / Random / Zip / Xml / Terminal)+ 整个 Math 模块 → `plugins/Ravel.Extensions.dll`
lib/                **标准库 —— 用 Ravel 自己写的**(控制流、序列、Option、IO、Random、
                    Regex、时间、格式化、文本、编码、位、网络(HTTP)、CSV、数据类、测试库、
                    摘要与加密、命令行参数、表格、日志、ZIP 归档、通配符、XML、终端、REPL、数据结构…)
tests/              golden 用例(.rav + 文件末尾的 `# --- expected ---`)
examples/           例子(不进测试,给你看着玩)
docs/               tutorial.md(语言教程)+ adr/(架构决策)
vscode-ravel/       VS Code 扩展
CONTEXT.md          **架构上下文** —— 想改引擎先读它
```

## 想读点别的

| 想干什么 | 读哪儿 |
|---|---|
| 学这门语言 | [`docs/tutorial.md`](docs/tutorial.md)(从 Hello World 到元类,带输出) |
| 改引擎 / 搞懂某个设计为什么是这样 | [`CONTEXT.md`](CONTEXT.md) |
| 看某个决策当时怎么权衡的 | [`docs/adr/`](docs/adr/) |
| 上手写库 | [`lib/`](lib/) —— 全是 Ravel,挑一个照着写 |

## 友情链接

[Bish](https://github.com/labbish/Bish) —— 另一门 .NET 语言(字节码 VM、LSP、插件),
感谢它的大力支持。

## 许可

[Apache-2.0](LICENSE)
