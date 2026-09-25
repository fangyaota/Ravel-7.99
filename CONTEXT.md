# Ravel — 上下文文档

## 项目概述

Ravel 是一个**显式持久帧栈**解释型编程语言（原 CPS trampoline 已移植替换，见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)）。C# 实现，55 passed / 0 failed / 7 todo。

## 编译运行

```bash
rm -rf out && DOTNET_GCHeapHardLimit=0x10000000 dotnet publish -c Debug -o out
                                         # 先删 out/：增量 publish 有时不更新它,
                                         # 会跑到陈旧产物、得出假的结论
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
Parser.cs / Parser.Holes.cs / Lexer.cs / Ast.cs / Token.cs / TokenType.cs
                                (Parser.Holes.cs = `_` 占位符消糖那趟 AST 改写)
Testing/GoldenTestRunner.cs     golden test 运行器(解析/执行/比对/汇报)
Repl/                           REPL 前端
  NeoInteractor.cs        外壳:多页缓冲 + 光标 + 主菜单(编辑/运行/读写/普通 REPL)
  ReplView.cs             单行渲染(按 token 高亮 + 光标块),纯函数
  ReplSession.cs          编辑缓冲持久化(repl_session.json),读写失败静默
Program.cs                      CLI 入口(REPL / test / 单文件)

lib/
  predefined.rav          别名 + using
  std.rav                 Property + interface
  try.rav                 异常处理

tests/                    62 个 golden test(普通 + expect-error + todo + fixture)

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
- 深度递归 20 万层安全(原 CPS ~4k 层爆栈)。

## 类型层次

```
Object (parent=self)
├── ValueType
│   ├── Integer
│   ├── Float
│   └── String → BigInt → Fraction → BigFraction
├── Function
│   ├── Bool          ← true/false 可调用:收两个块返回选中那个的结果
│   ├── Block
│   └── Type
│       └── Class
│           ├── List / Set / Dict / Ravel
│           └── 用户 metaclass
├── Void / Exception
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
- **构造器就是名字叫 `init` 的变量**（类体里写 `init := () => {...}`），不是修饰符——曾经写过 `init ctor := ...`，已废弃。一个类最多一个构造器，没有按参数类型重载；要分派就在 `init` 里自己判断。
- `type` 禁止创建类型，退化为元类型（`typeof` 结果、类型注解、类型值）。
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
      if { c (); } { body (); again 0; } { 0; }  # again 0 跳回 callcc 之后
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
{a; b;}     # Block (有分号/换行)
```

## 关键API

```ravel
# 类型反射
int.name          # "Integer"
int.Parent ()     # ValueType
int.Is ValueType  # true
int.Subtypes ()   # [String BigInt ...]
int.Initializer () # Property 代理(getter=构造器,setter=设构造器)

# 对象
obj.Fields ()     # 字段名列表(模块=作用域变量 + 类型方法;其他=类型方法)
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
Error: 未定义的变量 'nope'
  --> lib/_errtest.rav:2:9
  2 |     n + nope
    |         ^
  调用栈 (2 层):
    在 lib/_errtest.rav:1:18
    在 temp.ravel:1:1
```

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

## 测试

62 个 golden test。`# expect-error` 预期异常，`# --- expected ---` 预期输出，`# todo` 等待实现。当前 55 passed / 0 failed / 7 todo。普通测试已按特性合并为 7 个文件：`01_core`(基础/运算符/列表/位运算/_)·`11_control_flow`·`13_functions`·`40_callcc`·`75_modules`(模块/eval/类/with/throw)·`98_types`(类型/反射/大数/作用域)·`99_collections`。expect-error 与 todo 因语义必须独立。
