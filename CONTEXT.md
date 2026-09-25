# Ravel — 上下文文档

## 项目概述

Ravel 是一个**显式持久帧栈**解释型编程语言（原 CPS trampoline 已移植替换，见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)）。C# 实现，测试全绿——具体计数看 `dotnet out/ravel.dll test` 的输出，别写死在这里（文档里的数字总是追不上）。

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
  Interpreter.cs          入口/类型表/ResolveType/ThrowRavel/CheckFieldAccess
  Interpreter.Stack.cs    帧栈推进循环(StepOnce/Return/PushChild + 块执行)
  Interpreter.Nodes.cs    节点状态机(每 AST 节点一个 NodeFrame,按 Results.Count 分阶段)
  Interpreter.Call.cs     CallInto 调用分派 + 合成控制帧的推帧助手
  Interpreter.Control.cs  控制帧状态机(with/callcc/using/eval/类初始化/交替/合成…)
  Interpreter.Modules.cs  模块路径解析与加载(references + 搜索目录、循环引用检测)
  Interpreter.System.cs   RegisterBuiltins + System 模块
  ModuleSearchPath.cs     模块搜索目录(单一定义,predefined.rav 与 using 共用)
  Frame.cs / RList.cs     帧链(不可变持久) / 持久化单链表
  RuntimeValue.cs         值基类 + RuntimeException/TypeMismatch/Exit
  RuntimeType.cs                 类型系统
  RuntimeType.Initializers.cs    内建类型转换器/class 创建/CollectBodies
  RuntimeType.Methods.cs / Operators.cs   方法表(存 FunctionVal)/运算符
  Scope.cs / Variable.cs         作用域
  BoxedValue.cs                  成员访问
  Values/                        FunctionVal(基类,Body 即「参数→结果」)/LambdaVal/BlockVal/
                                 TypeVal/ControlFunction/BuiltinMethodVal/BoundClassOp/
                                 ComposeVal/PartialCtor/ContinuationVal/ObjectVal/.../各基础值
Lexer.cs / Ast.cs / Token.cs / TokenType.cs
Parser.cs                       入口 + token 辅助(Peek/Consume/ParseError)
  Parser.Statements.cs          语句:定义/赋值/运算符定义
  Parser.Expressions.cs         优先级链(管道→逻辑→比较→加减→乘除)
  Parser.Atoms.cs               基本单元 + 括号/块/集合/字典
  Parser.Holes.cs               `_` 占位符消糖那趟 AST 改写
Testing/GoldenTestRunner.cs     golden test 运行器(解析/执行/比对/汇报)
Repl/                           REPL 前端
  NeoInteractor.cs        外壳:多页缓冲 + 光标 + 主菜单(编辑/运行/读写/普通 REPL)
  ReplView.cs             单行渲染(按 token 高亮 + 光标块),纯函数
  ReplSession.cs          编辑缓冲持久化(repl_session.json),读写失败静默
Program.cs                      CLI 入口(REPL / test / 单文件)

lib/
  predefined.rav          别名 + 控制流 + using(启动时自动加载)
  try.rav                 异常处理:handlerStack + callcc 实现 Ex.try/Ex.throw
  math.rav                Math 模块(pi/e/square/cube),`using "math.rav"` 引入
  app.rav                 示例脚本(math + try 的冒烟),手动跑:
                          dotnet out/ravel.dll lib/app.rav
  std.rav                 ⚠️ 死文件:没被加载,且唯一的 Interface 靠已移除的 base
                          (元类特性还没实现,todo 用例全是它:117/118/121/122)

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
- 调用分派(`Interpreter.Call.cs` 的 `CallInto`):`ControlFunction`(控制帧)/`LambdaVal`(推 body 帧)/`BlockVal`/`TypeVal`(类→ClassInit 帧)/`ComposeVal`(prepend/append)/`BoundClassOp`(类运算符)/`PartialCtor`(半成品构造器→CtorApply 帧)/`ContinuationVal`(还原帧链);其余 `FunctionVal` 走默认分支,同步调 `Body(arg)` 把值塞回 sink。同步函数一律是「参数→结果」,没有 Step 包装(旧 CPS 的 `Done` 壳已删除)。
- 控制内建(`with`/`callcc`/`using`/`eval`)= `ControlFunction(Kind, Arity, Args)` 纯数据,收满参数推控制帧。求值器内部还会合成 `Alternate`/`ClassInit`/`Compose`/`ClassOp`/`CallAssign`/`CallReturn`/`CtorApply` 控制帧。`ControlKind` 因此只有 11 个值。
- **构造器调用与普通函数同一条柯里化路径**:`Point 3 4` ≡ `((Point 3) 4)`。`ClassInit` 建好对象、跑完类体后把参数喂给 `init`;`init` 还返回函数(参数没收齐)就交出 `PartialCtor` 半成品,由 `CtorApply` 帧继续喂,直到 `init` 应用完才把对象交出来。
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
Object (parent=self)
├── ValueType
│   └── Integer / Float / String / BigInt / Fraction / BigFraction   (并列,不是链)
├── Function
│   ├── Bool          ← true/false 可调用:收两个块返回选中那个的结果
│   ├── Block         ← 没有 Ravel 别名(block 在 ReservedWords 里)
│   └── Type
│       └── Class
├── List / Set / Dict   ← 直接挂在 Object 下,不经过 Class
├── Void / Exception / Ravel(模块) / Scope / Property
├── Any (顶类型, parent=null)
└── Every (底类型, parent=null)
```

`Bool <: Function` 是为了 lisp 式的条件:`true {a} {b}` 执行 a、`false {a} {b}` 执行 b，
于是 `if` 退化成 `(c ()) t e`（见下面「控制流」）。

## System 模块

内置模块，解释器启动时创建。包含所有类型和核心函数：

**类型**: Integer String Bool Float BigInteger Fraction BigFraction
        List Set Dict Object Void Function Type Class
        AnyType EveryType ExceptionType ValueTypeVal

**函数**: WriteLine Write ReadLine Assert TypeOf Eval RandInt
        CallCC Exit With RavelMod Using unsafe
        property currentScope

（`if`/`while`/`foreach` 不在 System 模块里——它们在 `lib/predefined.rav` 用 Ravel 写。）

**值**: True False Default

## 全局变量

仅 1 个：`System`（模块自身）。

其他所有变量通过 `predefined.rav` 别名定义（`int := System.Integer` 等）。

## 类型创建（class）

设计见 [ADR-0001](adr/0001-class-and-type-system.md)。

- `class Parent { fields + init }` 是**唯一**用户类型构造器，产物是 `ObjectVal`。
- **构造器就是名字叫 `init` 的变量**（类体里写 `init := () => {...}`），不是修饰符——曾经写过 `init ctor := ...`，已废弃，解析器会专门拦下来报「构造器不用 init 修饰符，直接写 `init := () => { ... }`」。一个类最多一个构造器，没有按参数类型重载；要分派就在 `init` 里自己判断。
- `type` 禁止创建类型，退化为元类型（`typeof` 结果、类型注解、类型值）。
- 用户类会被 `RuntimeType.Define` 登记进 `AllTypes`,`Subtypes ()` 才反射得到它们
  (只登记用户类:模块类型 System/Ex 每个 Interpreter 都重建一份,登记只会累积)。
  `AllTypes` 是静态表,而一个进程里会跑多个 Interpreter,所以**每个 Interpreter 构造时
  调 `ResetUserTypes ()` 清掉上一个留下的**——否则上一个建过的类会出现在下一个的
  `Subtypes ()` 里(测试之间就会互相看见,类型树快照这类用例直接失效)。
- **`C := class {...}` 建的类型没有名字**（只有 `::=` 会命名）。显示和报错时走 `RuntimeType.DisplayName`，空名字退化成 `class`——否则错误信息会变成「类型 '' 不支持运算符」这种没法读的东西。`Type.name` / `typeof c` 的显示 / `print obj` 都取它。
- 内置类型是 C# 硬编码（元类 `type`），实例是 C# record。
- **元类 = 创建者**：`Type` 自指；`Class` 的元类是 `Type`；`class` 建的类元类是 `Class`；用户元类 M 建的类元类是 M。
- **继承是平铺的**：子类实例化时沿 `RuntimeType.Parent` 链从顶祖先到自身依次跑**每层类体**，所有层的字段落在**同一个 instance scope**里，所以字段查找只需一层（`Scope.LookupField` 不走链、不走词法链）。
- 类实例化（`StepClassInit` 控制帧，阶段由 `Count` 推进）：建 instance scope → 打包 `ObjectVal` 并绑 `this`（`ClassType` = 最终子类，在第一个类体执行**之前**）→ 逐层跑类体 → **在实例作用域里按名字 `init` 找构造器**。各层平铺在同一个 scope，子类的 `init` 覆盖父类的，所以取到的天然是「最具体层声明的那个」；子类没写就落回父类的，整条链都没有才报错。
- **父类的 init 不会自动调用**，初始值要写在字段声明上（`a: int = 1`）；父类 init 里写的赋值不生效。子类重声明同名字段是覆盖。
- **没有 `base`**：曾用 `base = 父类()` 手工构造父类实例挂到实例 scope 的 `parent` 字段上，现已移除（`parent` 字段、`DefineBase`、`withDeep` 深拷贝一并删除）。元类路径的 `base.init parent block` 因此暂时无实现手段（117/118/121/122 保持 todo）。

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
- 可用符号见 `Ast.cs` 的 `OperatorSymbols.All`（`+ - * / % == != < > <= >= & | ^`），与 `RuntimeType.Operators` 注册的内置一致。一元 `!`、短路 `&&`/`||` 是求值器特判的，不能自定义。
- 分派是**两跳**：`a + b` 先 `a.Type.TryLookupMethod("+")`；类运算符注册的是 `BoundClassOp`，于是推 `ClassOp` 帧到**实例作用域**里按符号名找实现（各层类体平铺在同一 scope、子类覆盖父类，所以只有一个）。
- 成员访问同构：`a.+` 取到绑好 self 的函数，`1.+` 取内置的。

## by 属性

```ravel
by age := property (() => { _age; }) ((v: int) => { _age = v; })
```

实例读 `obj.age` / 写 `obj.age = v` 走 getter/setter（`CallInto` 派发，lambda getter/setter 也有效）。
`obj.age += v` 同样走——先过 getter 读、算完再过 setter 写（`StepByCompoundAssign`）。

## 多参数 lambda

```ravel
add := (x: int y: int) => { x + y }
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

## ::= 命名

```ravel
add ::= (x: int) => { x + 1 }
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

这条「有分号/换行才是 Block」的规则**只管表达式位置**(`ParseBrace` 要在 Set/Dict/Block
之间选)。`=>` 后面的 lambda 体是强制的块,没有歧义,所以不套用——`() => { x + 1 }`
合法,不必写成 `{ x + 1; }`。`ParseMandatoryBlock` 曾经照抄了那条规则,于是教程里
满篇的单行 lambda 全是错的,`tests/59`/`72` 更是被它抢先报语法错误、
根本走不到自己要测的 readonly/unreadable。

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
int.Is ValueType  # true
int.Subtypes ()   # [Every]  (Integer 没有自己的子类;子类型看 ValueType.Subtypes ())
int.Initializer () # Property 代理(getter=构造器,setter=设构造器)

# 对象
obj.Fields ()     # 数据字段名 + 类型方法名(对象=实例字段在前;模块=作用域变量在前)
obj.Copy ()       # 浅拷贝
obj.field := v    # 定义/覆盖字段(不存在就新建);obj.field = v 只改已存在的
obj.field += v    # 成员复合赋值(+= -= *= /= %=),左操作数只求一次
obj.+             # 取绑定好 self 的运算符函数(符号就是成员名)


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
  `Ex.try { eval "1 +" } {...}` 不生效——handler 只认 `RuntimeException`——
  eval 一段用户输入就能撂倒整个程序。没人接时仍抛原异常，CLI 按语法错误渲染。
  `eval` 的块带合成名 `<eval>`（`ErrorReport.ShortPath` 认这种虚拟名，不当路径解析）。

## 两个「类型说有、值却没有」的坑

这两处都栽过，加新类型/新内建时留意：

- **`Bool <: Function` 要求 `BoolVal : FunctionVal`**。true/false 可调用
  （`true {a} {b}`），类型表里挂在 Function 下；值这边不跟上，从 Function 继承来的方法
  拿到 self 是 BoolVal，`(FunctionVal)s` 直接抛 C# 的 InvalidCastException——
  它不是 RuntimeException，Ravel 的 try 接不住，程序被打掉。`BlockVal`/`TypeVal`
  一直是 `: FunctionVal(...)` 这么接的，`BoolVal` 是漏掉的那个。
- 反过来，**不能拿 `is FunctionVal` 当「这是方法/闭包」的判据**，因为 BoolVal 也是
  FunctionVal 了。用 `RuntimeValue.IsClosure`：`Fields ()`、`print obj`、
  `StepClassInit`/`StepCtorApply` 的半成品构造器判断都走它。用错会让
  `flag: bool = true` 从 `Fields ()`/`print obj` 里消失，还会让
  `init := () => { true; }` 的对象被当成半成品构造器交出去。

## 测试

`tests/` 下的 golden test。`# expect-error` 预期异常，`# --- expected ---` 预期输出，`# todo` 等待实现。计数不写在这里——跑 `dotnet out/ravel.dll test` 看，或者按目录数。普通测试已按特性合并为 7 个文件：`01_core`(基础/运算符/列表/位运算/_)·`11_control_flow`·`13_functions`·`40_callcc`·`75_modules`(模块/eval/类/with/throw)·`98_types`(类型/反射/大数/作用域)·`99_collections`。expect-error 与 todo 因语义必须独立。

- `expect-error` 只看 `output.StartsWith("Error:")`，所以**解释器自己漏出来的 C# 异常不算数**：
  `CaptureOutput` 给非 `RuntimeException`/`SyntaxException`/`ExitException` 的异常加了
  `!! C# 异常 …` 前缀，它不以 `Error:` 开头，会直接把用例判 FAIL。加这个前缀当场就抓到过 6 个
  漏出的异常（其中 4 个是语法错误没有类型、2 个是 exit 没被单独接住）——现在两者都有正式类型了。
