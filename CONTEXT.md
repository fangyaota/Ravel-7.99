# Ravel — 上下文文档

## 项目概述

Ravel 是一个**显式持久帧栈**解释型编程语言（原 CPS trampoline 已移植替换，见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)）。C# 实现，测试全绿、**todo 清零**——具体计数看 `dotnet out/ravel.dll test` 的输出，别写死在这里。

## 编译运行

```bash
rm -rf out && DOTNET_GCHeapHardLimit=0x10000000 dotnet publish Ravel.csproj -c Debug -o out
     # 先删 out/:增量 publish 有时不更新它,会跑到陈旧产物、得出假的结论
     # 指定 .csproj 而不是 .sln:"-o" 配 sln 会报 NETSDK1194
dotnet out/ravel.dll test                # 全量测试(有 FAIL 时退出码 1)
dotnet out/ravel.dll path/file.rav      # 单文件
dotnet out/ravel.dll                    # REPL
```

VS Code 里：`Ctrl+Shift+B` 跑当前 `.rav`（会先编译）、`F5` 跑当前文件并可下断点、
命令面板搜 `Ravel` 还有「运行全量测试」「打开 REPL」。语法高亮靠 `vscode-ravel/` 扩展
（见下），它调用的同样是 `bin/Debug/net8.0/ravel.dll`。

## 文件结构

```
Runtime/                         求值器按职责拆成多个 partial class 文件
  Interpreter.cs          入口/CheckFieldAccess/As<T>/Show
  Interpreter.Stack.cs    帧栈推进循环(StepOnce/Return/PushChild + 块执行)
  Interpreter.Nodes.cs    节点状态机(每 AST 节点一个 NodeFrame,按 Results.Count 分阶段)
  Interpreter.Call.cs     CallInto 调用分派 + 合成控制帧的推帧助手
  Interpreter.Control.cs  控制帧状态机(with/callcc/using/eval/类初始化/交替/合成…)
  Interpreter.Modules.cs  模块路径解析与加载(References + 搜索目录、循环引用检测)
  Interpreter.System.cs   RegisterBuiltins + System 模块
  Interpreter.Math.cs           Math 模块(常量/三角/双曲/幂对数/取整/极值)
  ModuleSearchPath.cs     模块搜索目录(单一定义,predefined.rav 与 using 共用)
  Frame.cs / RList.cs     帧链(不可变持久) / 持久化单链表;
                          Frame.cs 还有 ControlFrame.Arg<T> 和 ArgNames(控制帧参数的类型化取值)
  RuntimeValue.cs         值基类(含 IsClosure) + 全部 Ravel 层异常:
                          RuntimeException / TypeMismatchException / ExitException /
                          SyntaxException + SourceSpot(位置)
  Attr.cs                 修饰符名常量(readonly/public/private/…/core),解析器和门禁共用;
                           **表里的每一个都得有地方读它** —— `override`/`new` 因为无人读
                           已连同修饰符一起删(`public` 是唯一例外:它是默认行为)
                           `lib/` 的 API 都标 readonly(语言级别名/模块函数);
                           **状态**故意不标(Ex.HandlerStack / References)。
                           readonly 连 `:=` 一起挡(`Scope.DefineOrReplace` 里查本层):
                           `:=` 换掉整个 Variable,attrs 会跟着老的那个没
  ErrorReport.cs          错误渲染(位置 + 源码行 + 插入符 + 调用栈);
                          `Format(Exception)` 一个入口管运行时/语法两种错误
                          (CLI/REPL/测试运行器共五处调用点因此各少一条重复的 catch)
  BuiltinClasses.cs              内置**类对象**树(建树分两趟)+ 预设类体 + 默认建类逻辑
  BuiltinClasses.{Methods,Operators,Initializers}.cs   内置方法/运算符/转换器的注册
                                 (成员直接进各自类对象的 Scope,没有单独的"方法表")
  BuiltinClasses.Interfaces.cs   接口与实现(interface / use / impl / Dispose)
  BuiltinClasses.Sequences.cs    三种容器共用的序列方法 —— 将来 `IEnumerable` 那一层
                                 (收函数的那批是控制帧,见 Interpreter.Control 的 StepSeqOp)
  Scope.cs / Variable.cs         作用域
  BoxedValue.cs                  成员访问
  Values/                        
    ClassVal.cs                  **类对象**;`ClassVal : FunctionVal`,自己就是可调用的那个东西
                                 (没有 IFunction 这类"可调用"接口了 —— 判据就是"是不是函数")
    ObjectVal.cs                 **非原子值的基类**:带一张真实的成员表(Scope)
    FunctionVal.cs               `: ObjectVal`;Body 即「参数→结果」,CaptureScope 是捕获作用域
    MemberView.cs                **取成员的唯一入口**:自己那层 → 沿类对象 parent 链兜底
    ISelfBinding.cs              自绑定成员(内置方法/类运算符工厂):读出来要绑接收者
    NativeClosure.cs             **体是 C#、但按调用点作用域跑** —— 补 LambdaVal 和
                                 FunctionVal 中间那格(`type` 的 init 靠它看见 `this`)
    MethodVal.cs                 内置方法 / 类运算符工厂 / 绑好的类运算符
    ControlFunction/ComposeVal/PartialCtor/ContinuationVal/...   各基础值
                                 BoolVal 也继承 FunctionVal(类型表里 Bool <: Function,见「两个坑」)
                                 FractionVal/BigFractionVal 构造即约分
Lexer.cs / Ast.cs / Token.cs / TokenType.cs
                                Lexer 还对外给一个 `ScanState(源码) -> (深度, 在不在字符串里)`,
                                REPL 判断"这行写完没有"用它(规则和词法共用一份,别各写一遍);
                                Token.Length 是**源码跨度**(字符串含引号、带转义),
                                和 Lexeme(给人看的文本)不是一回事
                                字符串没收到尾的引号 = 语法错误(从前静默吞掉后面全部源码)
Parser.cs                       入口 + token 辅助(Peek/Consume/ParseError)
  Parser.Statements.cs          语句:定义/赋值/运算符定义/`x :< m`(只在 do 里放行)
  Parser.Expressions.cs         优先级链(管道→逻辑→比较→加减→乘除)
  Parser.Atoms.cs               基本单元 + 括号/块/集合/字典 + `do { … }` 折成 Bind 链
  Parser.Holes.cs               `_` 占位符消糖那趟 AST 改写
                                `do` 是**纯语法糖**:解析期就地折成 `m.Bind (…)`,
                                运行时不为它添任何东西(`BindStatement` 活不到求值期)
Testing/GoldenTestRunner.cs     golden test 运行器(解析/执行/比对/汇报)
Repl/                           REPL 前端
  NeoInteractor.cs        外壳:多页缓冲 + 光标 + 主菜单(编辑/运行/读写/普通 REPL)
  ReplView.cs             单行渲染(按 token 高亮 + 光标块),纯函数
  ReplSession.cs          编辑缓冲持久化(repl_session.json),读写失败静默
Program.cs                      CLI 入口(REPL / test / 单文件)

lib/
  predefined.rav          **启动时第一个加载的文件**:别名 + 控制流(`if` / `while` / `callcc`),
                          再用 `using` 把下面几个子模块拉进来 —— 名字都落在**全局作用域**上,
                          和从前全写在一个文件里一样,用户看得见的名字一个没变。
                          `callcc` 也在这儿(库函数:包装 System.CallCC,拍/还原两份控制状态)。
  numbers.rav             `INumber`(空槽接口:谁是数)+ 五种数值类型各实现一条
  iterator.rav            `IEnumerable` / `IEnumerator` / `Enumerator` + 三种容器的实现 + `foreach`
  cacher.rav              `Cacher`(Count/Fn/Keys/Vals 四个字段,init 交出的是包装函数而不是 this)
                          + 小写别名 `cacher`;两个类都进 AllTypes(`Subtypes ()` 看得到)
  monad.rav               **Monad 这一族**:`IMonad` 接口(Map + Bind 两条槽 —— 能进 `do { … }` 的形状)
                          + 一个实例 `Option`(Some/None:Has/Inner + IsSome/Value/Bind/Map/Where…)
                          + 构造子 `Some` / `None`
  sorting.rav             `IComparable`(一条槽:`CompareTo`)+ 六种内建标量的空登记
                          + `Sorting` 模块(Compare / Sort / SortBy / Max / Min / MaxBy / MinBy)
  exceptions.rav          `Ex` 模块(HandlerStack / Throw / Try)+ **错误钩子**(引擎冒泡时叫的
                          就是它:`System.SetErrorHook`)+ 小写别名 `try` / `throw`。
                          引擎不认识 HandlerStack,那块状态归这儿管 —— 从前是单独的 try.rav,
                          并进来之后又拆成这个文件。
  io.rav                  文件系统:接口 `IEntry` / `IFile` / `IDir`(全局名)+ 磁盘实现 + 通用件。
                          要显式 `using "io.rav"`;详见「文件系统」一节
  math.rav                Math 模块(pi/e/square/cube),`using "math.rav"` 引入
  types.rav               Types 模块:`PrintTree` 打印类型树(沿 Subtypes (),带 ├──/└──),
                          `using "types.rav"` 引入;tests/125 跑的就是它
  iomonad.rav             IoMonad 模块(IO Monad —— 把效果做成值的那种,不是文件/终端 IO):
                          `Action`(Effect/Perform/Map/Bind/Then/Discard/Attempt/Catch)、
                          `Return` / `PutStrLn` / `PutStr` / `PutStrLnErr` / `PutStrErr` / `GetLine` /
                          `Foreach` / `Sequence` / `When` / `Unless`;`Action` 把"要做的效果"做成值,
                          `Perform ()` 才真跑;Return/PutStrLn/PutStr/GetLine/Foreach。
                          `using "iomonad.rav"` 引入 —— `Action` 也实现了 `IMonad`
                          (它和 `Option` 是**同一个形状的两个实例**:各自那份 Map/Bind 就是形状本身,
                          `impl (IMonad Action { () })` 只是登记一下;`impl` 是全局的,所以
                          `x is IMonad` 在哪儿都成立)。两者的 `Bind` 各干各的:这边真跑效果,
                          那边没有值就短路
  app.rav                 示例脚本(math + try 的冒烟),手动跑:
                          dotnet out/ravel.dll lib/app.rav

tests/                    golden test(普通 + expect-error + todo + fixture),个数以目录为准

.vscode/                  VS Code 工作区配置
  tasks.json              Ctrl+Shift+B 跑当前 .rav(默认)、ravel: 全量测试
  launch.json             F5 跑当前 .rav(可下断点) / 原来的 REPL 配置

vscode-ravel/             VS Code 扩展:语法高亮(TextMate) + 运行命令
                          装法:复制成 ~/.vscode/extensions/ravel.ravel-language-0.1.0/
```

## 求值器架构(显式帧栈)

见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)。要点：

- 整个程序一个块根帧,`StepOnce()` 扁平循环逐帧推进,C# 栈恒平。
- 每节点状态机按 `Results.Count` 推进;`Return` 把结果交给父帧。
- 调用分派(`Interpreter.Call.cs` 的 `CallInto`):`ControlFunction`(控制帧)/`LambdaVal`(推 body 帧)/`NativeClosure`(同上,但体是 C#)/`BlockVal`/`ClassVal`(**类对象,调它 = 实例化**)/`ComposeVal`(prepend/append)/`BoundClassOp`(类运算符)/`PartialCtor`(半成品构造器→CtorApply 帧)/`ContinuationVal`(还原帧链);其余 `FunctionVal` 走默认分支,同步调 `Body(arg)` 把值塞回 sink
(被分派掉的那些值,`Body` 是 `FunctionVal.PlaceholderBody` —— **真被调到就报错**,
不再静默交出 `()`,也不让硬转抛 C# 异常:漏一个 `case` 要当场出声)。同步函数一律是「参数→结果」,没有 Step 包装(旧 CPS 的 `Done` 壳已删除)。
- 控制内建(`with`/`callcc`/`using`/`eval`)= `ControlFunction(Kind, Arity, Args)` 纯数据,收满参数推控制帧。求值器内部还会合成 `Alternate`/`ClassInit`/`Compose`/`ClassOp`/`CallAssign`/`CallReturn`/`CtorApply` 控制帧。`ControlKind` 因此只有 11 个值。
- **构造器调用与普通函数同一条柯里化路径**:`Point 3 4` ≡ `((Point 3) 4)`。`ClassInit` 建好对象、跑完类体后把参数喂给 `init`;**交出的是 `init` 的返回值**(约定 `this`),`init` 还返回函数(参数没收齐)就交出 `PartialCtor` 半成品,由 `CtorApply` 帧继续喂。
- callcc 只有一套语义:续延 = callcc 之后的剩余计算;调用它 = 丢弃当前帧链、从捕获点继续(详见「控制流」)。
- 深度递归 20 万层安全(原 CPS ~4k 层爆栈)——但那是 **C# 栈**安全,不是内存安全:
  每层留 5~6 个帧(BlockExecFrame + 各语句/表达式),合起来约 1KB,
  20 万层要 250MB 上下。所以它在默认堆下跑得通,在测试用的
  DOTNET_GCHeapHardLimit=0x10000000(256MB)下会 OOM。没有尾调用优化,长递归就是吃内存。

## 数字字面量

`NumberLiteral` 存的是**原始文本**（`Lexeme`），不是 `double`——double 只有 15~17 位有效数字，
大整数中转一手就丢精度。求值时才定类型（`Interpreter.MakeNumber`）：

- 有小数点 → `Float`
- 没有小数点且装得下 int32 → `Integer`
- 没有小数点但超了 → `BigInt`（所以 `typeof 2147483648` 是 `BigInt`）

后缀 `bigint` 是**构造器**（`bigint 123` 把值转成 BigInt），和字面量的类型推断是两回事。

**int 算术溢出报错,不静默回绕**（`Narrow`）：`int × int` 一律在 `long` 里算再收窄，
超出 int32 就报「`100000 * 100000` 超出 int 范围（int 是 32 位，大数用 bigint）」。
不检查的话 C# 的 unchecked 会让 `100000 * 100000` 得 1410065408、
`2147483647 + 1` 得 -2147483648——「算出来了但是错的」比崩溃难查得多。
一元 `-` 同理（`-int.MinValue` 翻不过来）。要更宽就写 `bigint`：
一边是 bigint 时按宽度升级,根本不进 int 那条路。

一元 `-` 按数值类型逐个翻转（`Negate`），不走 `0 - x`——Int 的 `-` 只认 Float 右操作数。
Int 与右操作数的二元运算按宽度升级（`IntOp`）：`float > bigint > int`，
所以 `1 + bigint 2` 和 `bigint 2 + 1` 都得到 BigInt。

## 类型层次

```
Object (parent=自己)
├── ValueType
│   └── Integer / Float / String / BigInt / Fraction / BigFraction   (并列,不是链)
├── Function
│   ├── Bool          ← true/false 可调用:收两个块返回选中那个的结果
│   ├── Block         ← 没有 Ravel 别名(block 在 ReservedWords 里)
│   └── Type          ← 用户类挂这下面(类自己没名字,显示成 class)
│       └── Interface ← `interface`;它造的"接口"也是类对象,挂它下面
├── List / Set / Dict   ← 直接挂在 Object 下,不经过 Function
├── Void / Exception / Ravel(模块) / Scope / Property
├── Any (顶类型, parent=自己)
└── Every (底类型, parent=自己)
```

`parent` 是类对象 Scope 里的一个普通成员(不是 C# 字段),所以链到头的方式是**自引用**
(`object`/`Every`/`Any` 的 parent 是自己)—— 遍历这些链的地方都要在 `t.Parent == t` 处停。

这棵树在 C# 侧全是 `ClassVal`(`ClassVal : FunctionVal : ObjectVal`,见下节);
`Bool`/`Block` 的元类分别是 `Bool`/`Block`,不是 `Function`。

`Bool <: Function` 是为了 lisp 式的条件:`true {a} {b}` 执行 a、`false {a} {b}` 执行 b，
于是 `if` 退化成 `(c ()) t e`（见下面「控制流」）。

## System 模块

内置模块，解释器启动时创建。包含所有类型和核心函数：

**类型**: Integer String Bool Float BigInteger Fraction BigFraction
        List Set Dict Object Void Function Type Interface
        Any Every Exception ValueType

**函数**: WriteLine Write ReadLine Assert TypeOf Eval RandInt
        CallCC Exit With RavelMod Using Use unsafe
        property currentScope

（`if`/`while`/`foreach`/`cacher`/`Some`/`None` 不在 System 模块里——它们在
`lib/predefined.rav` 用 Ravel 写。那里也定义了库里仅有的两个类型：`Cacher`(缓存)与
`Option`(可能没有值的包)。）

**值**: True False Default NaN Inf（特殊浮点值；`-Inf` 用一元 `-`）

## Math 模块

成员是 C# 造的（`Interpreter.Math.cs`），但**不像 System 那样启动就有** ——
`Math` 要**显式引用**（`using "math.rav"`）之后才是一个名字。落点是一张
`ModuleFillers` 表：`EnterModule` 在模块**第一次被 `ravel` 到时**调它填成员，
而 `lib/math.rav` 第一行就是那句 `ravel "Math"`。

**常量**: pi e tau

**三角/双曲**: sin cos tan asin acos atan atan2
              sinh cosh tanh asinh acosh atanh

**幂/对数**: sqrt cbrt pow exp log log2 log10 logBase hypot

**取整**: floor ceil trunc round roundTo（`round` 是四舍五入，不是银行家舍入）

**保型的那几个**: abs sign min max clamp minMagnitude maxMagnitude —— 交回**原始实参**
（`abs -5` 还是 int、`max 3 bigint …` 还是 bigint），其余一律给 float。

参数收**任何数值**（int/float/bigint/fraction），内部按 double 算 —— 和 `<` 那批运算符
同一个口径。`lib/math.rav` 只在上面补 Ravel 说得清楚的几个（square/cube/deg/rad）。

## 文件系统（`lib/io.rav`，要显式 `using "io.rav"`）

分三层，**"文件"先是个形状**，磁盘只是其中一个实现：

1. **引擎侧原语**（`System`）：`ReadText` / `WriteText` / `AppendText` / `DeletePath` /
   `CreateDir` / `ListDir`(名字→是不是目录) / `PathSize` / `PathTime` / `CopyPath` / `MovePath` /
   `CurrentDir` / `ChDir` / `FileExists` / `DirExists` / `PathJoin` / `PathDir` / `PathBase` /
   `PathExt` / `PathClean` / `SplitLines`。**只做 syscall、不做判断**：失败报 Ravel 错误（中文、
   带路径，路径按用户写的那串打、不转绝对路径），"要不要先问一句"交给 `FileExists` / `DirExists`
   探针（它们不报错）；外面再套一层兜底，**不让 C# 异常漏到顶层**（和 `RandInt` 那条注释一个道理）。
   路径基准 = 进程当前目录；**不做沙箱** —— 和 `using` 找模块一个待遇。
2. **接口**（`IEntry` / `IFile` / `IDir`，**全局名**，和 `IEnumerable` / `IMonad` 一个待遇）：
   `Exists` / `IsDir` / `Delete` / `Name`；`IFile` 加 `Read` / `Write` / `Append` / `Size`；
   `IDir` 加 `List`（`名字 → IEntry`，实现自己排序）。`IFile ::= interface IEntry` ——
   **实现了 IFile 也就实现了 IEntry**（照 C#；登记只写最具体那条）。磁盘特有的（路径、修改时间、
   改名、复制）**不进接口**。实现有两条路：① 类自己就有那几条成员 + `impl (IFile 那个类 { () })`
   登记；② `impl (IFile 某类 { by Read = property … })` 现装。只读实现（zip 条目那种）让
   `Write` / `Delete` 抛一句人话即可（这轮不拆 `IReadOnlyFile`）。
3. **磁盘实现 + 通用件**（模块 `Io`）：`Io.File "a.txt"` / `Io.Dir "sub"` 造条目
   （`Child` 对不存在的名字**按文件算** —— 写新文件是常事；建目录走 `Mkdir`）；
   `List` / `Files` / `Dirs` 按名字排序；`CopyTo` / `MoveTo` / `Rename` / `LastWrite` 是磁盘特有的。
   `Io.Lines (f: IFile)` / `Io.Copy (from to)` / `Io.EachDir (d f)`（递归走一遍，每见一个条目叫一次
   `f (路径, 条目)`）**对着接口写**，任何实现都吃 —— 以后加内存文件 / zip / 远程文件就是照这个缝插。

**控制台也是文件**：`Io.Stdout` / `Io.Stderr` / `Io.Stdin` 三个单例值实现 `IFile`（模块 `Io` 里
`ConsoleOut` / `ConsoleIn` 两个类各登记一条）。于是对着接口写的代码直接能用 ——
`Io.Copy f Io.Stdout` 把文件倒进终端、`Io.Copy Io.Stdin f` 把输入倒进文件。取舍写明白：
只写的那两个 `Read` 报错、只读的那个 `Write` 报错、控制台没有 `Size` 也删不掉（报错说人话）。
`Stdin.Read ()` 是**读到 EOF**（终端上 Ctrl+Z/D 收），读一行用 `ReadLine ()`（就是 `input`）；
`print` / `input` 照旧是日常那两个，这里是"抽象的视角"。

**和 `IoMonad` 搭台**：`Io.ReadAction` / `WriteAction` / `AppendAction` / `WriteLineAction` /
`EachLineAction` 把文件操作包成 `Action`（"先拼好、之后 `Perform ()`"）。所以 `io.rav` 开头
`using "iomonad.rav"` —— 反过来不行（`iomonad.rav` 由 predefined 加载,那时 `IFile` 还不存在,
而参数注解建 lambda 时就要值）。

链式调用用 `@`（或括号）：`x.f () @ .g ()` ≡ `(x.f ()).g ()` —— `.成员` 比并列的调用绑得紧。

两个坑（写在 `lib/io.rav` 里）：① 类体里给方法起名 `File` / `Dir` 会把**类名遮住**，
`File (…)` 变成调自己（实测无限递归）；② 实参位置上的 `raw.Get n` 会被读成 `((f …) raw.Get) n`。

测试 —— 229（磁盘：读写/列目录/复制移动改名/报错文案/换目录、临时目录跑完删干净）、
230（抽象那一层：测试里现写一个内存实现 + `Io.Copy` 两边跑 + `Io.EachDir` 递归）。

## 全局变量

仅 1 个：`System`（模块自身）。

其他所有变量通过 `predefined.rav` 别名定义（`int := System.Integer` 等）。

## 类型与对象：类就是 ClassVal

**没有 `RuntimeType`、没有 `TypeVal`、也没有单独的 Class 类型。** 值阶层（`Runtime/Values/`）：

```
RuntimeValue                          MemberScope（虚）→ 伪 / 真 Scope
├── IntVal FloatVal BigIntVal FractionVal BigFractionVal
│   StringVal ExceptionVal VoidVal DefaultVal      ← 原子值：无字段，MemberScope = 伪 Scope
└── ObjectVal                         Scope 字段 = 真实成员表（取成员的落点）
    ├── ListVal SetVal DictVal ModuleVal PropertyVal ScopeVal
    └── FunctionVal                   + CaptureScope（捕获作用域）
        ├── ClassVal                  ← **类对象就是它**
        └── LambdaVal BlockVal BoolVal NativeClosure BuiltinMethodVal …
```

**一个类是 `ClassVal`，而 `ClassVal : FunctionVal`** —— 于是"能不能调"和"是不是类"
都是**类型关系**，不再是"某个成员名在不在"：

- **能不能调用** ⟺ `is FunctionVal`。类对象自己就是那个可调用的东西：`CallInto` 见
  `case ClassVal` 就推 ClassInit 帧。没有 `call` 成员、也没有中间的 `BoundCall` 转发
  （那套连同用户自定义 `call` 的能力一起删掉了）。
- **`IsClass`**（我本身是不是一个类）⟺ **元类链上有 `type`**。
  注意它和"实例化造出来的是什么"走**两条不同的链**：`IsClass` 走**元类**链，
  而 `StepClassInit` 造 `ClassVal` 还是 `ObjectVal` 看的是**被实例化的那个类**的
  **parent** 链（`type { body }` 里 `type <: type` 自反 → 造出来的就是类对象）。

类对象的成员（类性由 Scope 里的普通成员表达）：

| 成员 | 含义 |
|---|---|
| `parent` | 父类对象（原型链上游；自引用 = 链到头） |
| `block` | 类体——实例化时重跑的配方 |
| `name` | 类名（`C := class {...}` 的**成员值是空串**，只有 `::=` 会命名；但 `C.name` 读出来是显示名，空名字退化成 `class` —— 读写不对称，见 BoxedValue 的 `name` 伪成员） |

`RuntimeValue.Type` 返回**元类**（创建它的那个类对象），`typeof X` 就是取它。
`type` 的元类是它自己（自指，链的起点）。

### 原子值与伪 Scope：取成员只有一条路

`Value.MemberScope.LookupField(name)` —— 由 `MemberView` 一次走完两段：

```
① 值自己那层（对象是它的实例作用域，扁平）
② 沿**类对象**的 parent 链兜底（方法住在类那层，`Fields` 就定义在 `object` 上）
```

- **非原子值**（对象、容器、函数、模块…）有**自己真实的成员表**（`ObjectVal.Scope`），
  两段都有。表是**每个实例一张**的空表：`l.tag := 1` 挂得上，又不会像"共用类型那张表"
  那样一改就改掉整个类型。
- **原子值**（`IntVal`/`StringVal`/… 这些按值比较的）**没有自己那层** —— 它们是 record，
  多一个字段就毁掉值相等。它们取到的是**伪 Scope**：只有第 ② 段，而那段是**算出来的视图**，
  不落地。所以 `IntVal(1) == IntVal(1)` 照旧成立，集合去重也照常。

第 ② 段**只认方法名**、且只收 `FunctionVal`：类对象的成员表里还躺着
`parent`/`block`/`name`/`this`/`init`，那些是**类自己的数据**，不是"这个值的成员"。
不挡的话 `(5).parent` 会从报错变成返回 `ValueType`。

⚠️ **运算符是个例外：它问的是"类型"那张表**（`值.Type.MemberScope`）。
运算符的语义是「这个**类型**怎么把两个操作数合起来」，和"这个值有哪些成员"不是同一个问题 ——
混起来的话 `typeof 1 == int` 会撞上 `Integer` 那层的数值 `==`（把类对象当数字算）。

### 四条统一规则

```
① 查成员   X.member   →  X.MemberScope 一次查找（自己那层 → 沿类对象 parent 链兜底）
② 判类型   A <: B     →  沿 A 的 parent 链能否走到 B（Every 全局特判）
③ 实例化   X args     →  新建 scope（this = 新 ClassVal / ObjectVal，ClassType = X）
                        沿 X 的 parent 链逐层跑各层 block
                        在 scope 里 LookupField("init") 找构造器
                        调它，并把【它返回什么就是什么】作为结果
④ 建类     X := class { body }        ← 父类默认 object
           X := class Parent { body }  ← 父类 Parent(必须已经存在)
           ≡ 把 class 换成 type 完全等价(两者是同一个值)
```

### 内置类也有类体

内置类的成员是 C# 造好的值，所以它们的**预设类体**用 `LiteralExpr`（AST 节点：直接求值成
一个 C# 值）拼出来，里面只定义 `init`：

| 类对象 | 预设类体 |
|---|---|
| `object` | `init := () => { this; }`（**默认构造器**，见下）|
| `type` | `init := (parent: type body) => 装 parent/block 并返回 this` \| `(body) => parent 默认 object` |
| `Integer` / `String` / … | `init := <CastToXxx>` |
| `List` / `Set` / `Dict` | `init := MakeDefaultCaster`（只收 `default`） |
| 用户类 | 用户写的 block |

`object` 那份跑在**最前**（`CollectBodies` 顶祖先优先），所以任何类只要不写 `init` 就继承它 ——
"什么都不做、把对象交出来"该是默认，和 `function default` 给空函数是一回事。
它是 `NativeClosure`（要交出的 `this` 只在调用点作用域里）。
**`StepClassInit` 调 init 前必须 `cf.Scope = inst.Scope`**（改在帧自己身上，不能只 `with` 一份）：
`CallInto` 里取调用点作用域的两条路（NativeClosure / 类运算符）走的是 `_top.Scope`。

于是**没有"转换器"这个特设概念**：`int 42` 得 42，走的就是「跑类体 → 找 init → 调它 →
交出返回值」，和用户类一模一样。`CallClassInto` 也只有一条路。

### 构造交出 init 的返回值

`init` 约定以 `this` 收尾，所以普通类拿到的还是实例；而**元类的 init 可以建出一个类再交出来**。
init 若还返回函数（多参构造器只喂了一部分）就交 `PartialCtor` 半成品，和普通函数一样柯里化。

⚠️ **忘写 `this` 会静默拿到 `()`**（块的值就是最后一条语句的值，赋值语句的值是 `()`）。
这是这条规则的代价。

### 元类

**一个父类是 `type` 的类就是元类**：它继承了「建类」那层的 init，所以它建出来的东西是**类**。

```ravel
MyMeta ::= class type {
    private parentInit := init;          # 覆盖之前先把继承来的那层存下来
    init = (parent: type body: function) => { parentInit parent body }
         | (body: function) => { parentInit object body }
}
MyClass ::= MyMeta { init := () => { 0; this; }; x: int = 42; }
```

- **不需要 `base`**：super 调用退化成「覆盖之前先把继承来的 `init` 取出来」。
- 两分支让 `MyMeta { ... }`（不带父类）和 `MyMeta parent { ... }` 都能用。
- **`init` 在类体里读到的是继承来的那个**：靠"预设类体先跑、把 `init` 落进同一个实例 scope"。
  而 `init = ...` 改的是**这个实例 scope 的副本**（预设类体每次实例化都重跑），`type` 本身不受影响。
- **引擎找构造器用 `LookupField`（一层、不走原型链）**：各层类体都跑进同一个实例 scope，
  谁写了 `init` 谁就在那一格里，所以拿到的是**最具体**的那份（没写就是继承来的）。
  它不会掉进 `type` 那层的建类逻辑 —— 那层只在 `type` 及其子类被实例化时才收集。
- **`object` 的类体里有一份默认构造器**（`init := () => { this; }`）：每个类的祖先链都到它，
  所以谁都没写就落回它，`C ()` 不再报「没有构造器」。它跑在最前（祖先优先），
  内建那些更具体的（`int` 的转换器、`List` 的 default 构造器）照样覆盖它。
  没有自己类体的类型（Void/Every/Any/Scope/Property/…）仍然不能当构造器调 ——
  那道护栏在 `CallClassInto`，看的是被实例化的那个类**自己**有没有类体。
- 用例：`tests/117`（基本）、`118`（挂钩建类过程）、`121`（拿到类再交出去）、
  `122`（typeof 链）、`193`（用户手写的那份）、`211`（默认构造器）。

### 其余

- **继承是平铺的**：实例化时沿 parent 链从顶祖先到自身依次跑每层类体，所有层的字段落在
  **同一个 instance scope** 里，所以字段查找只需一层（`Scope.LookupField` 不走链、不走词法链）。
  实例 scope 的父是**类定义处的词法作用域**（类体里能引用外部变量），不是父类的实例 scope。
- **父类的 init 不会自动调用**，初始值要写在字段声明上（`a: int = 1`）；父类 init 里写的赋值不生效。
  子类重声明同名字段是覆盖。
- 用户类会被登记进 `AllTypes`，`Subtypes ()` 才反射得到它们。`AllTypes` 是静态表，
  而一个进程里会跑多个 Interpreter，所以每个 Interpreter 构造时调 `ResetUserTypes ()`
  清掉上一个留下的——否则上一个建过的类会出现在下一个的 `Subtypes ()` 里。
- **`parent` / `block` / `name` / `init` / `this` 是机制成员**。注意两个问题它们是两个答案：
  - **查找**时它们**不沿类链继承**（`ObjectVal.IsMethodName`）：不挡的话 `(5).parent` 会从报错
    变成返回 `ValueType`。
  - **`Fields ()`** 不按名字挡 —— 它列的就是"这个 Scope 里的成员",`init` / `parent` / `block`
    都在里面（它们读得到、调得动）。唯一排掉的是 `this`：它不是成员，是**这个值自己**的别名。
  `print obj` 是另一回事：它是**数据快照**，方法（含 `init`）不出现在里面（显示选择，不是成员定义）。
  两条容易漏的：**`init`** —— 类体就跑在类对象自己的实例作用域里，所以类对象的 Scope 里
  **装着它自己的构造器**；**`this`** —— 类对象就是 `type` 的实例，而实例化时那句
  `instanceScope.Define("this", …)` 写进去的**正是这个类对象的成员表**。
- **自绑定成员**（`ISelfBinding`：`BuiltinMethodVal`、`ClassOperatorFactory`）读出来要先
  绑接收者 —— 漏了的话 `type.Parent ()` 会把未绑定的内置方法当结果返回。
  它和"同步快路径"标记（`BuiltinMethodVal`）**不是一回事**：类运算符工厂也要绑，
  但绑完是 `BoundClassOp`（推帧的标记），不能直接算 —— 合并会让 `a + 5` 交出标记而不是数。

## 控制流

`if`/`while`/`foreach` 是**库函数**（`lib/predefined.rav`），不是 C# 内建。它们靠两个机制写出来：

- **`Bool <: Function`**：`true {a} {b}` 执行 a 并返回其结果，`false {a} {b}` 执行 b。
  于是 `if {c} {t} {e}` ≡ `c {t} {e}`，`if` 只是 `(c t e) => { (c ()) t e; }`。
  实现上不需要新的 `ControlKind`：`CallInto` 里给 `BoolVal` 加一个分支，收到第一个块返回 `PartialBool` 半成品，收到第二个块就执行选中的那个。
- **callcc 当跳转**：续延被调用时丢弃当前帧链、从捕获点继续，所以可以拿它做循环：

  ```ravel
  while := (c: function body: function) => {
      again := (x: int) => { x; }
      callcc (k: function) => { again = k; }     # 独立语句:恢复时只是「这条语句完成」,不重跑它
      cond := c ()                               # 必须在恢复点之后,否则 again 0 跳回来时不重求
      assert (typeof cond == bool) ("while 的条件必须是 bool，得到 " + string (typeof cond))
      if { cond; } { body (); again 0; } { 0; }  # again 0 跳回 callcc 之后
  }
  ```

  这样帧链不随迭代增长（100 万轮实测跑得完）。**写成 `again := callcc (k) => { k; }` 不行**：恢复时会重跑那条赋值、`again` 被覆盖成 `0`，循环建立不起来。

`with`/`using`/`eval` 仍是 C# 内建——`with` 需要「用副本的 scope 执行块」，`using`/`eval` 需要文件 IO 与词法/语法分析，Ravel 层做不到。

**控制状态跟着续延走,而"什么时候拍、什么时候还原"是库的策略**：帧链之外还有两样状态 ——
`Ex.HandlerStack` 与模块加载栈 `_loading`。它们都是**副作用式**的、不在帧链里:帧链一丢,
"跑完收尾去摘 handler / 退 `_loading`"那一步就再也不会执行。逃出 `try` 体会留下**僵尸 handler**
(之后没人接的错误被它接住,而它的续延停在早被丢弃的链上;和 tests/215 修过的正常收尾泄漏同类),
逃出**模块体**会让那个模块永远"正在加载"(之后 `using` 被误报「检测到循环引用」),而且它已记进
`_loaded`,于是 `using` 变成**静默空操作** —— 所以被中断的那个加载还要从 `_loaded` 里摘掉。
反过来也顺:调一个在 `try` 体里捕获的续延,那个 try 的 handler 回来,重进的那段照样受保护。
**普通数据不回滚**(累加器、字段原地改):恢复 = 接着从中断处往下算。

分层:**引擎只管它自己那两样** —— `_loading` / `_loaded`(原语 `System.LoadingState ()` 拍、
`System.RestoreLoading snap` 还原,并在还原时把"被甩掉的那些加载"从 `_loaded` 里摘掉;
快照只收 `_loading`,**不拷 `_loaded`** —— 被中断的加载按"此刻还在 `_loading` 里、快照里没有"算出来,
否则每次 `callcc` 都要拷一遍已加载表)。**`Ex.HandlerStack` 不归引擎管**:它是库的状态、
就在 Ravel 里那个 list 上,库自己拍自己还原 —— 引擎**任何地方**都不认识它,连报错冒泡那条路
也只是一句"把注册的钩子调起来"(见下)。
**策略在库里**:`predefined.rav` 的 `callcc` 是**库函数**(包装 `System.CallCC`,写在 `Ex` 那一段之后):
捕获时拍两份快照(自己的 handler 栈 + 引擎的模块加载状态),交给用户的续延被调时先还原这两份、再跳。
于是控制流整套(`while` / `if` / `try` / `callcc`)都住在库里,引擎不知道 `try` 是什么。
代价三条:**① 直接用 `System.CallCC` 就没这层保护**(库的 `callcc` 才是入口);
**② 还原那两句必须是一条内置调用** —— 别在"续延被调时要跑的代码"里用 `while` / `foreach` / `try`
(它们自己都要经过续延,会互相踩,实测死循环);**③ 每次续延跳转都要走一遍还原**(循环里就是每次迭代
一次),实测 20 万轮 `while` 偏慢几个百分点(噪声内)。
测试 223(try)、224(循环/枚举/多发射/累加器)、225(eval/with/接口槽的 `instance`)、226(模块)。

**异常处理整套也在库里**。引擎只有一个"错误钩子":冒泡上来一个 `RuntimeException` 时,它把
`System.SetErrorHook` 注册的那个函数调起来(库启动时注册的是 predefined.rav 的 `onError`),
剩下的事库自己定 —— 有人接就弹掉 `Ex.HandlerStack` 栈顶那个 handler 再调它(`try` 的 handler 会
escape 回它那个 callcc),没人接就 `System.Unhandled e` 把控制交回引擎去报告(引擎**原样**抛出
原来那个异常,位置与调用栈都不变,也不会混进库的帧)。非函数的栈顶(手写 `Ex.HandlerStack = [1]`)
照样当"没人接"。于是引擎对"handler 栈"这件事**零知识** —— 和它不知道 `while` / `try` 是什么一样。

**callcc 只有一套语义**：续延 = callcc 之后的剩余计算；调用它 = 丢弃当前帧链、把值当作 callcc 的返回值、从捕获点继续。因此 **callcc 之后的语句会被重新执行**——`x := 1 + callcc (k) => { saved = k; 0; }` 之后再 `saved 10`，会让 `x` 变成 11 并继续往下走。想「跳出去」就调用续延（旧的 escape 用例行为不变）。

## 运算符

类体里**直接用符号**定义，符号本身就是实例里的成员名：

```ravel
Vec := class {
    init := () => { 0; }
    x: int = 0
    + := (o: Vec) => { ... }    # 定义
    == := (o: Vec) => { ... }
}
```

- `+ := f` **定义**；`+ = f` **覆盖**从父类层继承来的那个（父类自己不受影响）。旧写法 `operator+ add := ...` 已废弃，会报语法错误。
- 可用符号见 `Ast.cs` 的 `OperatorSymbols.All`（`+ - * / % == != < > <= >= & | ^`，**词形运算符** `is` / `isnot`，以及类型之间的 `<:` / `:>`），与 `BuiltinClasses` 注册的内置一致。一元 `!`、短路 `&&`/`||` 是求值器特判的，不能自定义。
- **`<:` / `:>` 是类型之间的关系**：`A <: B`（A 是不是 B 的子类型）/ `A :> B`（父类型），
  **两边都得是类型对象**（接口也是类型）——值那一边用 `is`。两个都注册在 `Object` 上，
  这样 `1 <: int` 报的是「'<:' 的左边得是个类型，得到 Integer 的实例」而不是「类型不支持运算符」。
  判据 = `IsAssignableTo`；接口那半（`myClass <: myTrait`）在求值器里补，见「接口与实现」一节。
- **`is` / `isnot` 是词形运算符**：不是标点，所以解析器在**运算符位置**按词认
  （`Parser.IsWordOperator` / `IsInfixWordOperator`），别处照样能当标识符与成员名用 ——
  于是 `1.is`（等右操作数）和 `is.int`（等左操作数）与 `a.+` / `+.2` 完全对称。
  实现挂在 `Object` 上（每个类的 parent 链都到它），判据就是 `IsAssignableTo`：
  `1 is int` ✓、`1 is float` ✗（兄弟）、`1 is object` ✓、`int is type` ✓。
  节的脱糖必须**就地**做（`ParsePrimary` 里 `DesugarHoles`），否则 `isnot.string "a"`
  会变成 `(_0) => { (_0 isnot string) "a"; }` —— 把实参也吞进体里，而不是"应用节"。
- 分派是**两跳**：`a + b` 先在**类型**那张表里找（`a.Type.MemberScope`）；类运算符那格装的是 `ClassOperatorFactory`，绑完得 `BoundClassOp`，于是推 `ClassOp` 帧到**实例作用域**里按符号名找实现（各层类体平铺在同一 scope、子类覆盖父类，所以只有一个）。
- 成员访问同构：`a.+` 取到绑好 self 的函数，`1.+` 取内置的。

## by 属性

```ravel
by age := property (() => { _age; }) ((v: int) => { _age = v; })
```

`by` 是**两半拼**的：`Variable` 上的一个 attr（门禁问的是它）+ 值是个 `PropertyVal`，
靠两个约定成员名 `Get` / `Set` 接上（`ObjectVal.GetterMember` / `SetterMember`，**不在作用域里**，
`BoxedValue.GetMember` 对 `PropertyVal` 特判）。所以 `by` 和 `property` 互相独立：
只写 `property` 不写 `by`，读出来就是那个 `<property>` 值本身。

三条路、三件事（别混）：

| 写法 | 干什么 |
|---|---|
| `by a := property g s` / `by a: int = …` | **定义槽**：`a` 从此是个属性（注解管写进来的值）|
| `a = v` / `obj.a = v` | 给属性赋值：过 setter |
| `by a = X` / `by a.x = X`（`SlotAssign`，语句）| **换掉槽里的那份 property**：`Variable.ReplaceSlot`，不过旧 setter、不查约束 |
| `by a.x := X`（同上，`Define`）| **在那个对象上建槽**：`DefineOrReplace` + `SetAttr(By)`，成员不存在也行（`:=` 定义 / `=` 换，和别处一个规矩）|
| `by a` / `by a.x`（`SlotExpr`，表达式）| **取出那份 property 本身**：不过 getter，拿到的是普通值 |

后两条是"属性本身"那半边（绕开 getter/setter），前两条是"属性值"那半边。

**`default` 是"属性的默认值"**（`BuiltinClasses.DefaultProperty`，五个写入点共用
`Nodes.cs` 的 `SlotValue`）：一对什么都不做的 `FunctionVal` —— `Get = () => { (); }`、
`Set = (_: object) => { (); }`。和 `int default` 给 0、`function default` 给空函数同一个道理。
所以 `by a: int = default` 的槽里是个**能读能写、都不做事**的属性（读 `()`、写丢掉），
它**没有状态**，因此 `CopyScope` 让副本和原件共享同一个是对的。别的非 `property` 值
（`by bad := 5`）不在此列 —— 第一次读/写照旧当场报错。
`SlotExpr` **不能把整条路径当普通表达式求** —— `by c.n` 求 `c.n` 就走 getter 了，
那正是要绕开的；所以 `by a` 直接查变量、`by a.x` 只求接收者 `c`。门禁和成员读同一套。

四条读写路各问一次那个 attr：裸读（`StepIdent`）、成员读（`TryGetByGetter`）、
写（`WriteVariable`）、复合赋值（`StepByCompoundAssign`，先过 getter 读、算完过 setter 写）。
**取函数一律走 `Interpreter.PropertyGetter/Setter`**：标了 `by` 而值不是 `property`
就是报错（从前四条路三个答案，其中裸读会静默交出那个值、裸写什么都不发生）。

attrs 只有一份，在 `Variable` 上（`PropertyVal.Var` 指回去）——`Attrs ()` 也读那一份。
**`readonly` 对 by 槽也管**：`Variable.CheckWritable` 是写入侧的公共门，
`Assign` / `ReplaceSlot` / `WriteVariable` 的 by 分支 / `StepByCompoundAssign` 的写那步
四处都问它（by 那半走 setter，不经过 `Assign`，漏一处"只读"就漏一个口子）。

**`SlotAssign` 那条路只接 `by` 一个修饰符**（`Parser.Statements.cs` 的
`attrs.Count == 1 && attrs.Contains(Attr.By) && IsSlotAssignStart()`）：混着别的修饰符
（`readonly by r := property …`）会落回 `ParseDefinition`，那边才把 attrs 一个个装上 ——
曾经不查这一条时，`readonly` 会被 SlotAssign 静默丢掉。

**类型注解**（`by n: int = …`）管的是**写进来的值**：`StepVarDef` 对 `by` 声明**不**拿注解去比
那份值（比了就是「无法将 Property 赋值给 Integer」），而是把注解记成约束，由写入那侧
（`WriteVariable` / `StepByCompoundAssign` 过 `Variable.CheckAssignable`）执行 ——
和普通字段一样，约束在赋值侧，读侧不管（Ravel 从不检查函数返回什么）。
`by a := …` 不带注解时约束记 `Any`（别把 `PropertyVal` 自己的 `Property` 当约束，那会把写入全挡回去）。

## 接口(interface)与实现(use)

**库里已经在用的四个:`INumber`** —— 最简单的那个,**一个槽都没有**,只是"这个类型是数"的标记
(五种数值类型各 `impl` 一条,于是 `(x: INumber)` 收得下 `int 5` 也收得下 `float 5.0`;
`lib/math.rav` 那四个函数用它,从前标的是 `ValueType` —— 那个连 String 都收)。

**`IComparable`**(`lib/sorting.rav`)—— 一条槽 `CompareTo`,"这个类型自己讲了怎么比大小"。
底层那条协议**每个值都有**:引擎在 `Object` 上放了一条 `CompareTo`(内建那把尺子,数值/字符串),
要换比法就在类里自己写一条盖掉它(和 `ToString` 一个规矩)。详见「比较与排序」一节。

**`IEnumerable` / `IEnumerator`**(`lib/predefined.rav` 末尾一段)。形状照 C#:

```ravel
IEnumerable ::= interface { by GetEnumerator : function = default }
IEnumerator ::= interface { by MoveNext : function = default
                            by Current  : object   = default }
```

`GetEnumerator ()` 交回一个**枚举器**;枚举器 `MoveNext ()` 往前走一步(返回还有没有),
`Current` 是当前那个(C# 里是属性,这边也做成属性)。库里的 `Enumerator` 就是"拿一串值"的
通用枚举器,谁有现成的一串值谁就能拿它当枚举器。三种容器各 `impl` 一遍
(用 `impl` 而不是 `use`:全局登记,库加载时就生效),于是:

- `[1 2 3] is IEnumerable` / `{1 2 3} is IEnumerable` / `{a: 1} is IEnumerable` 都成立;
- `(xs: IEnumerable) => …` 收得下它们(注解也认接口);
- **`foreach` 改走这条接口**:`e := xs.GetEnumerator ()` + `while { e.MoveNext (); } { f e.Current }`
  —— 就是 C# 里那个循环。从前它只吃 list(`assert (typeof xs == list)`),现在 set / dict
  一样能遍历(字典遍历的是值);**每次进来新开一个枚举器**,所以嵌套遍历同一串值互不打扰;
- 用户自己的类实现一条 `use (IEnumerable MyClass { by GetEnumerator = property … })` 就能进 `foreach`。
- **接口继承一个接口,外加一串要求**:

  ```ravel
  supTrait    ::= interface myTrait { by c : int = default }   # 一个父(继承)
  masterTrait ::= interface supTrait [IEnumerable] { () }      # 父 + 要求
  ```

  **父是继承**:槽取并集(父的 + 自己的;同名以自己写的为准),`<:` 沿着继承走
  (`masterTrait <: myTrait` 成立),而**实现了子接口就等于实现了它的父接口**:
  `u is supTrait` / 注解 / `foreach` / 两个查询全认。
  **要求是前置条件**:实现这个接口的类必须**已经**有那些接口的实现 —— 槽**不并**进来、
  `<:` 也**不**成立(`masterTrait <: IEnumerable` 是 false),只在造实现那一步查有没有
  (`StepImplMake`,报「`masterTrait` 要求 C 已经实现了 IEnumerable（先给它 impl/use 一条）」)。
  所以要求者和实现者的关系是"你得自己另外 impl 一条",不是"我替你带上"。

  实现上:链上挂那一个父(`parent`,类型树/`Subtypes ()`/成员查找/实例化全照旧),
  父的**声明**在造子接口时抄进它的类体(`BakeInterfaceInit`),于是"接口的形"自足 ——
  `StepImplMake` 那两段照旧;`interface` 的 init 走 `Alternate`:接口与类合成一支
  (都声明 `Type`,进到体里靠"谁在造"再分;排在代码块那支前面,不然 `ClassVal : FunctionVal`
  会把它们吃掉)、代码块一支、光有要求的一支(专门说"要求得跟在父后面")、兜底一支
  —— 接口那支交出去的又是**一个小分流器**(要求表 / 代码块):先后两次应用,不是嵌套。

- **两个方向的查询**(都在 `Type` 上,所以任何类型对象、接口对象都有):
  - `T.GetImplements ()` —— 这个类型**现在**实现了哪些接口(接口对象组成的 list);
  - `I.GetImplementors ()` —— **现在**哪些类型实现了这个接口(目标类组成的 list);
  - 都是"当下"的快照:沿当前作用域找生效中的实现,判据同 `u is I`(目标收得下 `T` /
    trait 就是 `I`),所以出了作用域 / `Dispose` 之后就列不出来。`GetImplements` 里子类算
    (实例收得下目标);`GetImplementors` 里普通类永远是空的(接口槽是"实现"挂上去的,
    而实现的 trait 只能是接口)。同一项只列一次;顺序照查找来(由内到外、后 `use` 的先),
    所以 `GetImplements` 打头的是**当下生效**的那个。
  - 入口在 `CallInto` 的 `BoundTraitQuery` 一格 —— 这活儿要当前作用域,而内置方法的体
    拿不到解释器(和 `is` / 注解那两处同一个理由)。成员值本身是 `TraitQuery`(带方向):
    和 `ClassOperatorFactory` 一个路子,读出来先绑接收者。

**接口对象继承自 `object`,但类型是 `interface`** —— 和 C# 一样,接口不是"继承了一个叫
`Interface` 的基类",所以 `IEnumerable.Parent ()` 是 `object`;而 `typeof IEnumerable` 是
`Interface`(`is interface` 成立)。为此 `BuiltinClasses.BakeInterfaceInit` 把接口自己的
`init` 塞进**每个接口的类体**:原样靠"parent 链上有 `Interface`"是继承不到那个预设类体的,
而 `myTrait myClass { … }` 是实例化 `myTrait`,必须在自己类体里找得到 `init`。

**接口只承诺它自己那几条槽**。序列方法(`Map`/`Where`/`Fold`…)挂在**具体容器**上,所以通用
函数里按接口的契约写(`GetEnumerator` / `MoveNext` / `Current`),或先落到那串值再往下用 ——
这样任何实现者都吃得住(`tests/217` 就是这么写的)。

库里的用例先放上面,下面从形状讲起:

```ravel
myTrait ::= interface {
    by a : int = default
    by b : function = default
}
myImplement := myTrait myClass {
    by a = property (() => { instance.x; }) ((v: int) => { instance.x = v; })
}
use myImplement          # 只在这个作用域里生效
u := myClass ()
u.a = 1
myImplement.Dispose ()   # 提前取消
```

机制全在 `Runtime/BuiltinClasses.Interfaces.cs`,**求值器只多了一个控制帧**:

- `interface` 是内置类对象、`parent` 是 `type`(`Link(Interface, Type, Type)`)⇒ `interface is type`,
  而 `interface { … }` 造出来的是**类对象** `myTrait`(元类是 `Interface`)。它的**类体**就是接口的
  "形"(那批 `by a : int = default`)。`Interface.ClassBody` 的 `init` 是 `Alternate(twoArg, oneArg)`,
  **twoArg 必须排在前面**:`ClassVal : FunctionVal` 且 `Type <: Function`,反了的话
  `(body: Function)` 会先把 `myTrait myClass` 里的类对象吃掉(然后报「class 需要代码块参数」)。
- `myTrait myClass { … }` 推 `ControlKind.ImplMake` 帧(`StepImplMake`):**接口类体与实现块依次跑在
  同一个 scope 里**(先摆槽、再换槽),那个 scope 就是实现对象的成员表 —— **槽住在实现里**,
  建一次一直用。收尾(`FinishImplementation`)在那个 scope 里装上一条 `instance` **by 槽**
  (`Attr.By` + `Attr.Readonly`),它的 getter 是原生闭包:**读的那一刻**现场解析"这一次调用在服务谁"。
  两段都要推帧才能跑,而原生闭包的体是同步的 —— 这就是它非要一个控制帧的原因。
- 读写 `x.a` 的兜底在 `BuiltinClasses.TraitSlot`:常规成员表(`MemberScope`,含类链)里没有这个名字时
  才去沿 `CurrentScope` 的词法链找生效中的实现(同 scope 里后 use 的先试、目标类收得下就认),
  找到返回一份**命中**(哪条槽、哪个实现、这次服务谁)。真正要用它的调用点再走 `Activate`:**临场开一层
  作用域**(`instance$active` = 这次服务谁,`impl$active` = 哪个实现),把槽的 getter/setter 绑到那层上。
  `TraitSlot` 自己**不建**激活格 —— 有一半调用点只是问一句"有没有"(`HasTraitOperator`,每次二元运算
  类型表落空都会问)。落点:读 `TryGetByGetter`;写 `StepMemberAssign`(count2)/ `StepCompoundAssign` /
  `StepTraitOp` 的第二支;`StepSlot` / `StepSlotAssign` 那两支**只取槽、不绑**(`by` 取的就是槽本身)。
  **`myClass` 这个名字一个字没动**:不建子类、不换绑定、不往它身上加成员
  (所以 `x : myClass = u`、`print u`、`u.Fields ()` 照旧,`Fields ()` 里也**没有** a/b)。
- **`instance` 是"这一次调用"的事,不是实现身上的一格共享变量**(从前是 —— 于是留存下来的闭包,
  比如 getter 返回的那个,会跟着后一次访问改意思,静默错值)。读它走两层:**先**沿当前调用点作用域链
  找最近的激活格(槽体自己、以及槽体里造的闭包 —— 闭包捕获创建处的 scope,靠这一层记住自己属于谁),
  **再**沿帧链往外找(槽体里调用的辅助函数,它的词法链上没有激活格但调用链上有)。
  两格都只认**本次那个实现**(`impl$active` 记的是它的**号**,`impl$id`:拿值当身份证而不是拿实现
  对象本身,所以 `Copy ()` 出来的副本 —— 另一个对象、同一个实现 —— 认的是同一枚,照旧好使);
  跨实现边界宁可报「没有正在被服务的实例」也不错绑。
  没有激活格就报错、`instance = x` 报只读(`Attr.Readonly`)。它**不进**"`u.x` 能读到什么":
  `TraitSlot` 见到这个名字直接返回 null,否则 `u.instance` / `(5).instance`(全局 INumber 实现的
  目标类就是 int)都成了能读的新成员。
  那条槽自己还挂着 **`protected`**(两道闸各管一半):`im.instance` / `by im.instance` 从外面碰报
  「变量 'instance' 是受保护的」("实现这一族的东西"),实现自己的代码照旧读得到;而 `u.instance`
  报的仍是「类型 'C' 没有方法 'instance'」—— 它的语义是"u 身上没这个成员",那是名字那道闸给的。
  两道判据是分开的:同一个接口的**另一个实现**在可见性上让过(protected),但它那次分发不在这儿,
  于是落到「此刻没有正在被服务的实例」。
- `use impl`(`System.Use`)/ `impl 实现`(`System.Impl`)把实现登记进**一个作用域**的成员里
  (键 `use$impls`;`$` 不在标识符字符集里,用户写不出这个名字,永远不会撞):`use` 给的是
  **当前作用域**(随作用域在/不在),`impl` 给的是**全局作用域**(每个作用域链都到全局,于是处处生效 ——
  顶层 `use` 和 `impl` 等价,差别只在函数体/模块里写的时候)。登记在 scope 上而不是类上,所以效果
  **随作用域在/不在**。
  表里每条是 `[实现, 登记时代号]`;实现的成员 `generation` 每 `Dispose` 一次 +1,于是"取消"是
  O(1) 的作废(老条目全失效),之后在哪 `use` 就在哪重新登记一条(那个作用域又活了)—— 不用记
  (实现 × 作用域) 那笔账,也就不怕在循环里 `use`。
- `x is myTrait` 的兜底是 `BuiltinClasses.HasTrait`,同一个判据。它挂在 `StepBinaryOp` 里而**不是**
  运算符的 C# 体里 —— 内置运算符的体是纯 C#,拿不到解释器也就拿不到当前作用域;只接**内置**那一支,
  类里写过 `is := f` 的照旧走自己的实现。
- **接口也算类型,判定与类型检查同一个判据**(`BuiltinClasses.HasTrait`:当前作用域里有生效中的
  实现、目标类收得下这个值)。挂点四处:读写成员的兜底 `TraitSlot`;`is`/`isnot`(在
  `StepBinaryOp` 里 —— 内置运算符的体是纯 C#,拿不到解释器);**注解**与**参数**
  (解释器的 `Accepts`,落在 `StepVarDef` / `CallInto` 两处);**写入口**
  (`ViaTrait` 当 `Variable.CheckAssignable` / `Assign` / `Scope.Assign` 的补充判据)。
  于是 `x : myTrait = u`、`(v: myTrait) => …`、`by h : myTrait = property …` 在实现生效期间
  都通得过,出了作用域(或 `Dispose` 之后)照旧报「无法将 … 赋值给 myTrait」。
  这**不动 `IsAssignableTo`**(纯函数,拿不到解释器也就拿不到当前作用域)。
  **`myClass <: myTrait`(类型那一侧)也认**:同一个 `HasTrait`,判据是"这个类的实例在作用域里
  都算那个接口";`(typeof x) <: myTrait` 就够不着了(类型推断不出是哪个实例),
  那一侧用 `x is myTrait`。
- 接口里 `= default` 的槽,实现没填就是那个"什么都不做"的默认属性(读 `()`、写丢掉)—— 和 `default`
  本来的语义一致。
- **槽里能放运算符**:`by + := property g s`。那一格的**值是 property**,所以用它是**两级**
  —— 先读槽(推 getter)拿到运算符函数,再拿它收右操作数(`ControlKind.TraitOp`,和类运算符的
  `ClassOp` 分开:两者阶段数不一样)。挂点只有一处:`BindOperator` 在**类型表落空**之后问一句
  "这个接收者身上有没有这个符号的 `by` 槽"(自己那层 → 生效中的接口实现),有就交给 `TraitOp` 帧。
  于是 `u + v`、`u.+ v`、`u += v` 三条路都通(复合赋值那条经 `CallAssign` 写回)。
  两边看得见的不同:trait 那边是 `instance`,类体里自己声明的那条是 `this`。
  **只认 `by` 槽**:裸的 `+ := f`(类运算符)走原来的 `ClassOperatorFactory` 那条路,不变。
- **槽上的访问控制照旧**(`private` / `protected` / `readonly` / …):判据就是现成那一套,只是 owner 是
  **接收者**(读 `TryGetByGetter` 的 `CheckObjectReadAccess`、写 `StepMemberAssign` 的
  `CheckMemberAccess`)—— 所以 `private` 的含义是"**这个类自己的代码**能碰"(实现体在类外面,碰不到),
  `protected` 是"子类的代码也能"。措辞沿用原样:读报「变量 'a' 是私有的」、写报「字段 'a' 是私有的」。
  可见性写在**接口那次声明**上;实现体**换槽**(`by a = …`)时再写修饰符解析器直接拒
  (「修饰符后需要 ':='」),**新建**槽(`private by a := …`)可以带 —— 和类体一个规矩。
  接口声明成 `readonly` 的槽实现体换不了它(换槽要过 `CheckWritable`)—— 等于那种槽形同没法实现,
  **现状如此**(测试 222 钉着);要改的话有两条路:声明时就拒掉,或者"实现体填一次、之后只读"。
- 已知代价:① 实现 scope 的词法父是**实现块**的捕获作用域(接口体与实现体通常写在同一处);
  ② 留存下来的闭包各自拎着那次调用的激活作用域 —— 被引用的实例不会提前释放(从前实现身上只留
  最后绑定的那一个,但那个语义是错的);③ `by u.a` 取出的是**槽本身**,它没有"这一次"可言,
  之后再 `.Get ()` / `.Set v` 会报「此刻没有正在被服务的实例」;④ 同理,实现体里别的方法
  (`impl.someMethod ()`)直接读 `instance` 也报这个 —— "这一次调用"指的就是**槽体那一次**
  (槽体里调它没事:帧链那一半认得)。

测试 —— 213(端到端:读写、`is`、`Fields`、类型约束、Dispose)、214(作用域、叠加、子类、`use 5`)、
221(`instance` 是"这一次调用"的事:留存闭包、一次实现服务两个实例、辅助函数、没有激活时报错)、
222(槽上的访问控制:private / protected / 换槽不许带修饰符 / readonly 槽的现状)、
125(类型树上多一个 `Interface`)。

## 多参数 lambda

```ravel
add := (x: int y: int) => { x + y; }
add 3 4    # 柯里化
```

## _ 占位符 / 运算符节

```ravel
add1 := _ + 1    # (x: object) => x + 1
add2 := +.1      # 同上,运算符节写法:符号在前表示左操作数留空
```

`+.1` 与 `_ + 1` 脱糖成同一个 lambda。右操作数**只吃一个 primary**（含成员访问），
复杂式要自己加括号：`+.(2 * 3)`。

**括号界定 section 的范围**：`(+.1) 41` 先把 `(+.1)` 变成 lambda、再让 `41` 应用上去 = 42。
（占位符消糖本来只作用于整条语句，那会把 `(+.1) 41` 整个包成 `(_0) => (_0 + 1) 41`，
所以 `ParseParen` 在括号内就地收口。）

## `$` / `@`（括号的语法糖）

并列调用是左嵌套（`f a b` ≡ `(f a) b`）；这两个各封一头，**都在 `ParseCall` 的后缀循环里**：

- `$`：把**右边**封成一个实参（`f $ a b` ≡ `f (a b)`，右结合 —— 右边交给 `ParseExpression`）；
- `@`：把**左边**封口，后面的 `.成员` 挂到左边那一串的**结果**上（`x.f () @ .g ()` ≡ `(x.f ()).g ()`）。
  后面不是 `.成员` 就是显式的"到这儿为止"（`a @ b c` 和 `a b c` 一样）。尾巴上多出来的 `@` 报错。

`@` 的动机：`.成员` 比并列的调用绑得紧，`x.f ().g ()` 会被读成 `x.f ((().g ()))`（零参调用后面接链
全废）。测试 231。

**并列的调用比运算符松**:实参吃到运算符为止 —— `print 1 + 2` ≡ `print (1 + 2)`,
`inc 1 * 5` ≡ `inc (1 * 5)`,`f a b` 照旧是两个实参(柯里化不受影响)。
实现就是 `ParseCall` 里实参那一档用 `ParseExpression(allowCall: false)`(运算符链、不吃并列的实参)。
**没有例外**,括号开头的实参也一样:`f (1) + 2` ≡ `f ((1) + 2)`(从前那条"括号组自己完整"的
特判已经删掉 —— 库里 `x.Count () == 0` 那一类写法全改成了带括号的 `(x.Count ()) == 0`)。
代价:运算符要作用在调用**结果**上时自己加括号;`string x + " 个"` 里 `string` 会把 `+ " 个"`
一起吃掉(类型错误,响亮),转换器式调用(`string x` / `bigint n` / `typeof x`)后面还要接东西
也得把实参括起来 —— 教程 3.7 与常见陷阱、测试 233。
## ::= 命名

```ravel
add ::= (x: int) => { x + 1; }
add.name   # "add"
```

## 集合/字典

```ravel
{1 2 3}     # Set  (无换行)
{a:1 b:2}   # Dict (无换行 + IDENT:)
{}          # 空字典(单行;空块本来就禁止,所以没有歧义)
{a; b;}     # Block (有分号/换行)
```

**字典键只能是标识符**:`{"a": 1}` 是语法错误(`ParseDict` 收的是 `TokenType.Identifier`,
`ParseBrace` 的前瞻也只认 IDENT:`)。`{a: 1}` 的键就是字符串 `"a"`,
要非标识符的键只能 `d.Set "带 空格" 1`。

**集合元素的相等性**:标量(`int`/`string`/…)比**值**(所以 `{1 2 2}` 是 2 个元素);
`list`/`set`/`dict` 比**身份**(record 的自动相等对 `List<T>`/`HashSet<T>`/`Dictionary`
字段退化成引用比较),所以 `{[1] [1]}` 是 2 个元素、`{ [1] }.Contains [1]` 是 false。
集合类型**没有 `==`/`!=`**:可变集合上的值相等说不清(放进去再改,哈希就失效了)。
这是有意的取舍,不是漏实现——要改就得先解决循环引用下的深度相等(`ShowDepth` 只管显示)。

**「有 `;`/换行才是 Block」这条规则不看位置,lambda 体一样适用**:
`() => { x + 1 }` 是语法错误,得写 `() => { x + 1; }`。`;` 和换行都是 Newline token,
它是「这是块」的标记——没换行时 `{ x }` 是 Set、`{ a: 1 }` 是 Dict,
没有别的信息能区分。`ParseMandatoryBlock`(`=>` 后面那个块)也要过这条检查。

(这里曾经去掉过检查,理由是「`=>` 后面块是强制的、没有歧义」——那是按解析器好不好写
在想问题。语言规则该由语言定;当时改完连 `tests/59`/`72` 都从「被语法错误抢先报错」
变成了「靠语法错误通过」,两个用例始终没测到自己要测的 readonly/unreadable。)

## 序列方法(三种容器共用的一层)

`list` / `set` / `dict` 上那些**只跟元素顺序有关**的方法(`Count` / `IsEmpty` / `Any` /
`Contains` / `First` / `Last` / `ToList` / `ToSet` / `Distinct` / `Reverse` / `Take` / `Skip` /
`Concat` / `Join` / `Sum` / `Min` / `Max`,以及收函数的 `Each` / `Map` / `Where` / `Fold` /
`All` / `Any p` / `Find` / `SortBy`)**只有一份实现**,注册给三种容器
(`Runtime/BuiltinClasses.Sequences.cs`),每种容器只回答一件事:**怎么按枚举顺序把元素取出来**
(`items` —— 字典给的是值)。

这就是将来 `IEnumerable` 那一层:接口出来了就把注册点从三处并到一处、`items` 改成问接口要,
**方法体一个字不用改**。`Concat` / `Union` 这些收"别的容器"的地方已经先按这个形状写了 ——
`ElementsOf` 就是今天版的"接受任何可枚举"。

两批的分界是**要不要调用户函数**:

- 不调的:普通内置方法,纯 C# 当场算;
- 调的(`Each`/`Map`/`Where`/`Fold`/`All`/`Any`/`Find`/`SortBy`):成员值是 `SeqMethod`
  (`ISelfBinding`),读出来绑好接收者就交出 `ControlFunction` → 求值器推 `ControlKind.SeqOp` 帧。
  **非这样不可**:原生闭包是同步的 C# 调用,而调 Ravel 函数 = 往帧栈上推帧
  (见 `Interpreter.Control.cs` 的 `StepSeqOp`,一个元素一步)。

规矩(都照 C# / Linq 对齐):

- 变换与查询**交回新的 list**(今天"一串值"就是 list),要 set 自己 `ToSet ()`;
- 顺序类的(`First`/`Last`/`Take`/`Skip`/`Reverse`)按**枚举顺序** —— set / dict 的顺序是
  它们枚举器给的,不是插入顺序;
- 比大小一律走 `<`(`Less`),比不了**报错并说出两边**(`[1 "a"].Max ()` → 「比不了 Integer 与 String」)。
  (自己的类型要比大小 / 排序走的是**另一条协议** `CompareTo` —— 见「比较与排序」。)
  排序经 `SortByKey`:先拿第一个量一遍再排 —— .NET 会把比较器抛的异常包成
  `InvalidOperationException`,那不是 RuntimeException,Ravel 的 `try` 接不住;
- 字典的"元素"是**值**(按键找用 `Has` / `Keys ()`);
- 空容器:`First` / `Last` / `Min` / `Max` / `Find`(没找到)**报错**,`Sum ()` 给 0。

## 比较与排序（`lib/sorting.rav`；predefined 加载，所以 `IComparable` 是全局名）

**协议**：`CompareTo other` 交回 -1 / 0 / 1（和 C# 的 IComparable 同约定；负=我小）。
它**每个值都有** —— 引擎在 `Object` 上注册了一条（`Runtime/BuiltinClasses.Methods.cs`），
体就是引擎那把尺子 `Less`（也就是 `<` 的口径），所以 `3.CompareTo 5` / `"a".CompareTo "b"`
拿起来就能用，不必谁登记。比不了照样说人话（「比不了 Integer 与 String」）。

**换比法**：在自己类里写一条 `CompareTo` —— 它盖掉 `Object` 那条（和 `ToString` 一个规矩）：

```ravel
Rec ::= class {
    K: int = 0
    CompareTo := (o: Rec) => { if { K < o.K; } { -1; } { if { K > o.K; } { 1; } { 0; } } }
}
impl (IComparable Rec { () })
```

`impl` 是"**这个类型自己讲了怎么比**"这个类型层面的事实：登记之后 `x is IComparable`、
注解 `(x: IComparable)`、`GetImplementors ()` 全认（和 `INumber` 一个规矩；六种内建标量
也空登记了一遍）。**现装那条路（`by CompareTo = property …`）在这儿用不了**：这个名字
`Object` 上已经有了，而成员查找是**类链先说话**、槽还没轮到 —— 和 `IFile` 那种"类里本来
没有的名字"不同。

**`Sorting` 模块**：`Compare a b`（就是 `a.CompareTo b` 的名字）· `Sort xs` · `SortBy xs key` ·
`Max xs` / `Min xs` / `MaxBy xs key` / `MinBy xs key`。

- 排序是库里自己写的**稳定归并**（`SortOn` / `Merge`），比的是 `CompareTo` —— 于是
  **实现了接口的类型也排得了**。引擎那个 `xs.Sort ()` / `xs.Min ()` / `xs.Max ()` 走 `Less`
  （只认内建标量），碰见对象只会说「比不了 X 与 Y」。库里这套与它**并存**，不是替掉它。
- 稳定性：键相等的保持原来的先后（`Merge` 里 `<= 0` 取左边）。
- `Max` / `Min` 空表**报错**（和 `First` / `Last` 一个规矩），相等时留先出现的那个。

用例 `tests/234_icomparable.rav`。

## core 字段与 unsafe ()

`core` 修饰的字段读写都要先 `unsafe ()`。**`unsafe ()` 按作用域生效**：
它把**调用它的那个作用域**记进 `Interpreter.UnsafeScopes`，检查时沿作用域链往上找标记。
标记随调用帧走，函数返回后自然失效。

从前是个只增不减的计数器（`UnsafeDepth++`，没有对应的 `--`），于是**任何一次**
`unsafe ()` 之后整个程序余下的 core 检查全关掉：在某个无关函数里调一次、哪怕它早就返回了，
外部就能直接读写核心字段——core 修饰符等于不存在。

```ravel
f := () => { unsafe (); 0; }
f ()                 # 跟 secret 毫无关系
print c.secret       # 现在照样报「字段 'secret' 是核心字段，需要 unsafe」
```

读的门禁统一在 `BoxedValue.GateRead`（裸标识符和成员访问共用一份）——
从前 `StepIdent` 自己抄了一份、漏抄 core 那条，于是 `c.secret` 被拦住、
类体里直接写 `secret` 却读得到。

## 关键API

```ravel
# 类型反射
int.name          # "Integer"
int.Parent ()     # ValueType
int <: ValueType  # true(类型之间:`<:` 子类型 / `:>` 父类型,两边都得是类型)
1 is ValueType    # true(值的说法;`isnot` 取反,`1.is` / `is.int` 也行)
T.GetImplements () # 这个类型**现在**实现了哪些接口(见「接口与实现」一节)
I.GetImplementors () # 反过来:**现在**哪些类型实现了这个接口
int.Subtypes ()   # [Every]  (Integer 没有自己的子类;子类型看 ValueType.Subtypes ())

# 对象
obj.Fields ()     # **这个值有哪些成员**:自己那层照单全收(字段和方法一视同仁),
                  # 再并上类型链上的方法名。只排掉 `this`(它是值自己,不是成员)
obj.Copy ()       # 浅拷贝
f.Body ()         # 函数/类的体(Block);没有体的给**空块** —— 类型恒定,不用 `()` 顶替
f.Scope ()        # 捕获作用域(Scope);类对象没有,同样给空 Scope
obj.field := v    # 定义/覆盖字段(不存在就新建);obj.field = v 只改已存在的
# 类型注解是个表达式(求值在定义处/参数创建处):
#   `x: int = v`            一个名字(可带 . 成员访问)
#   `x: (pick ()) = v`      括号里的任意表达式 —— 括号必需,否则 `f ()` 会和下一个参数撞
obj.field += v    # 成员复合赋值(+= -= *= /= %=),左操作数只求一次
obj.+             # 取绑定好 self 的运算符函数(符号就是成员名)

# 打印函数:打出它的**源码**(AstPrinter 把 AST 还原回去)+ 柯里化**applied的实参**。
# 签名里是还等着的那几个,所以两者合起来就是完整形状:
#   <function add (x: int) => { x + 1; }>
#   <function (y: int) => { x + y; } applied x=3>


# 模块
ravel "M"
using "file.rav"
```

## 错误报告

运行时错误带**位置**和 **Ravel 层调用栈**，跨文件也分得清：

```
Error: 未定义的变量 'missing'
  --> tests/152_error_report.rav:4:29
  4 | helper := (n: int) => { n + missing; }
    |                             ^
  调用栈 (2 层):
    在 tests/152_error_report.rav:4:20
    在 tests/152_error_report.rav:1:1
```

（取自 `tests/152_error_report.rav` 的实际输出——它是精确比对用例，所以这段不会漂。）

- **抛出点只管给消息**：90 多处 `throw new RuntimeException("...")` 不用操心位置。
  位置和栈由求值器在冒泡时补（`Interpreter.Stack.cs` 的 `StepOnce` → `Locate`）——
  `catch (...) when (!ex.Located)` 保证只有**最内层**补，外层不覆盖成更外侧的位置。
- **帧链就是调用栈**，沿 `Parent` 收集即可，这是显式帧栈架构白捡的好处。
  只取 `BlockExecFrame`（每次块执行 = 一次调用），节点帧只是栈帧内部的步骤；
  最多列 12 层（深递归时帧链可能有几十万层）。
- **文件名挂在 `BlockExpr.Source` 上**（主文件 / `using` 的模块 / `eval` 片段），
  节点本身只有行列。调用栈里每层用**块**的位置（≈ 函数定义处），
  出错位置则精确到当前求值的节点。
- 渲染在 `Runtime/ErrorReport.cs`：路径取相对 cwd、分隔符统一 `/`——报告短，
  且让 `tests/152_error_report.rav` 能精确比对（不是 `# expect-error` 那样只看前缀）。
- **语法错误也走这份渲染**（`SyntaxException`，见 `RuntimeValue.cs`）：位置来自 token、
  没有调用栈，但同样画 `--> file:line:col` 和插入符（`tests/174_syntax_error_report.rav`）。
  它以前是个裸的 `System.Exception`，于是 CLI / 测试运行器分不清「用户代码写错了」
  和「解释器有 bug」——两者都落在同一个 `catch (Exception)` 里。现在三个类型各归各位：
  `RuntimeException`（求值期）/ `SyntaxException`（词法语法期）/ `ExitException`（exit 解栈），
  落到兜底 `catch (Exception)` 的一律打 `!! 解释器内部错误`。
- **`eval` 里的语法错误对 Ravel 层是可接的**：`StepOnce` 另有一个 `catch (SyntaxException)`，
  把它转成 `RuntimeException` 再走同一套 handler 分发。不转的话
  `Ex.Try { eval "1 +" } {...}` 不生效——handler 只认 `RuntimeException`——
  eval 一段用户输入就能撂倒整个程序。没人接时仍抛原异常，CLI 按语法错误渲染。
  `eval` 的块带合成名 `<eval>`（`ErrorReport.ShortPath` 认这种虚拟名，不当路径解析）。

## 两个「类型说有、值却没有」的坑

这两处都栽过，加新类型/新内建时留意：

- **`Bool <: Function` 要求 `BoolVal : FunctionVal`**。true/false 可调用
  （`true {a} {b}`），类型表里挂在 Function 下；值这边不跟上，从 Function 继承来的方法
  拿到 self 是 BoolVal，`(FunctionVal)s` 直接抛 C# 的 InvalidCastException——
  它不是 RuntimeException，Ravel 的 try 接不住，程序被打掉。`BlockVal` 一直是
  `: FunctionVal(...)` 这么接的，`BoolVal` 是漏掉的那个。
- 反过来，**不能拿 `is FunctionVal` 当「这是方法/闭包」的判据**，因为落在 Function
  类型下的有两种**数据值**：`BoolVal`（`Bool <: Function`）和 `ClassVal`（类对象）。
  用 `RuntimeValue.IsClosure`：`Fields ()`、`print obj`、
  `StepClassInit`/`StepCtorApply` 的半成品构造器判断都走它。用错会让
  `flag: bool = true` 从 `Fields ()`/`print obj` 里消失，会让
  `init := () => { true; }` 的对象被当成半成品构造器交出去，
  还会让 `class {…}` 拿到一个 `PartialCtor` 而不是类。

## 测试

`tests/` 下的 golden test。`# expect-error` 预期异常，`# --- expected ---` 预期输出，`# todo` 等待实现
（**当前没有 todo 了**：最后 4 个是元类，随「类就是 ObjectVal」那一轮落地）。计数不写在这里——跑 `dotnet out/ravel.dll test` 看，或者按目录数。早期把基础特性合并过几个大文件（`01_core`(基础/运算符/列表/位运算/_)·`11_control_flow`·`13_functions`·`40_callcc`·`75_modules`·`98_types`·`99_collections`），后面按特性一个用例一个文件。expect-error 与 todo 因语义必须独立。

- `expect-error` 只看 `output.StartsWith("Error:")`，所以**解释器自己漏出来的 C# 异常不算数**：
  `CaptureOutput` 给非 `RuntimeException`/`SyntaxException`/`ExitException` 的异常加了
  `!! C# 异常 …` 前缀，它不以 `Error:` 开头，会直接把用例判 FAIL。加这个前缀当场就抓到过 6 个
  漏出的异常（其中 4 个是语法错误没有类型、2 个是 exit 没被单独接住）——现在两者都有正式类型了。
