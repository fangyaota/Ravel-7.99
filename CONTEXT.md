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
  Interpreter.cs          入口/类型表/ResolveType/ThrowRavel/CheckFieldAccess
  Interpreter.Stack.cs    帧栈推进循环(StepOnce/Return/PushChild + 块执行)
  Interpreter.Nodes.cs    节点状态机(每 AST 节点一个 NodeFrame,按 Results.Count 分阶段)
  Interpreter.Call.cs     CallInto 调用分派 + 合成控制帧的推帧助手
  Interpreter.Control.cs  控制帧状态机(with/callcc/using/eval/类初始化/交替/合成…)
  Interpreter.Modules.cs  模块路径解析与加载(references + 搜索目录、循环引用检测)
  Interpreter.System.cs   RegisterBuiltins + System 模块
  ModuleSearchPath.cs     模块搜索目录(单一定义,predefined.rav 与 using 共用)
  Frame.cs / RList.cs     帧链(不可变持久) / 持久化单链表;
                          Frame.cs 还有 ControlFrame.Arg<T> 和 ArgNames(控制帧参数的类型化取值)
  RuntimeValue.cs         值基类(含 IsClosure) + 全部 Ravel 层异常:
                          RuntimeException / TypeMismatchException / ExitException /
                          SyntaxException + SourceSpot(位置)
  Attr.cs                 修饰符名常量(readonly/override/…/core),解析器和门禁共用
  ErrorReport.cs          错误渲染(位置 + 源码行 + 插入符 + 调用栈),运行时/语法错误共用
  BuiltinClasses.cs              内置**类对象**树(建树分两趟)+ 预设类体 + 默认建类逻辑
  BuiltinClasses.{Methods,Operators,Initializers}.cs   内置方法/运算符/转换器的注册
                                 (成员直接进各自类对象的 Scope,没有单独的"方法表")
  Scope.cs / Variable.cs         作用域
  BoxedValue.cs                  成员访问
  Values/                        
    IFunction.cs                 **可调用**的抽象:FunctionVal 和 ObjectVal 都实现它
                                 (对象还要有 `call` 成员才算可调用,见「类型与对象」)
    ObjectVal.cs                 **类与实例共用的表示**;类性靠 Scope 里的 parent/block/name/call
    FunctionVal.cs               Body 即「参数→结果」;LambdaVal/BlockVal/BoolVal/TypeVal(没了)…
    ISelfBinding.cs              自绑定成员(内置方法/类运算符工厂):读出来要绑接收者
    NativeClosure.cs             **体是 C#、但按调用点作用域跑** —— 补 LambdaVal 和
                                 FunctionVal 中间那格(`type` 的 init 靠它看见 `this`)
    BoundCall.cs / MethodVal.cs  `call` 成员的值 / 内置方法与类运算符工厂
    ControlFunction/ComposeVal/PartialCtor/ContinuationVal/...   各基础值
                                 BoolVal 也继承 FunctionVal(类型表里 Bool <: Function,见「两个坑」)
                                 FractionVal/BigFractionVal 构造即约分
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
- 调用分派(`Interpreter.Call.cs` 的 `CallInto`):`ControlFunction`(控制帧)/`LambdaVal`(推 body 帧)/`NativeClosure`(同上,但体是 C#)/`BlockVal`/`ObjectVal`(**有 `call` 成员才可调**,调它 = 实例化)/`ComposeVal`(prepend/append)/`BoundClassOp`(类运算符)/`BoundCall`(`call` 成员的值)/`PartialCtor`(半成品构造器→CtorApply 帧)/`ContinuationVal`(还原帧链);其余 `FunctionVal` 走默认分支,同步调 `Body(arg)` 把值塞回 sink。同步函数一律是「参数→结果」,没有 Step 包装(旧 CPS 的 `Done` 壳已删除)。
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
├── List / Set / Dict   ← 直接挂在 Object 下,不经过 Function
├── Void / Exception / Ravel(模块) / Scope / Property
├── Any (顶类型, parent=自己)
└── Every (底类型, parent=自己)
```

`parent` 是类对象 Scope 里的一个普通成员(不是 C# 字段),所以链到头的方式是**自引用**
(`object`/`Every`/`Any` 的 parent 是自己)—— 遍历这些链的地方都要在 `t.Parent == t` 处停。

`Bool <: Function` 是为了 lisp 式的条件:`true {a} {b}` 执行 a、`false {a} {b}` 执行 b，
于是 `if` 退化成 `(c ()) t e`（见下面「控制流」）。

## System 模块

内置模块，解释器启动时创建。包含所有类型和核心函数：

**类型**: Integer String Bool Float BigInteger Fraction BigFraction
        List Set Dict Object Void Function Type
        AnyType EveryType ExceptionType ValueTypeVal

**函数**: WriteLine Write ReadLine Assert TypeOf Eval RandInt
        CallCC Exit With RavelMod Using unsafe
        property currentScope

（`if`/`while`/`foreach` 不在 System 模块里——它们在 `lib/predefined.rav` 用 Ravel 写。）

**值**: True False Default

## 全局变量

仅 1 个：`System`（模块自身）。

其他所有变量通过 `predefined.rav` 别名定义（`int := System.Integer` 等）。

## 类型与对象：类就是 ObjectVal

**没有 `RuntimeType`、没有 `TypeVal`。** 「一个类」就是一个 `ObjectVal`，
类性由它 Scope 里的成员表达：

| 成员 | 含义 |
|---|---|
| `parent` | 父类对象（原型链上游；自引用 = 链到头） |
| `block` | 类体——实例化时重跑的配方 |
| `name` | 类名（`C := class {...}` 的**成员值是空串**，只有 `::=` 会命名；但 `C.name` 读出来是显示名，空名字退化成 `class` —— 读写不对称，见 BoxedValue 的 `name` 伪成员） |
| `call` | **"可调用"的凭据**（见下） |

`RuntimeValue.Type` 返回这个对象的**元类**（创建它的那个类对象），`typeof X` 就是取它。
`type` 的元类是它自己（自指，链的起点）。

### 四条统一规则

```
① 查成员   X.member   →  沿 X.Type 的 parent 链逐层在各自 Scope 里找
② 判类型   A <: B     →  沿 A 的 parent 链能否走到 B（Every 全局特判）
③ 实例化   X args     →  新建 scope（this = 新 ObjectVal，ClassType = X）
                        沿 X 的 parent 链逐层跑各层 block
                        在 scope 里 LookupField("init") 找构造器
                        调它，并把【它返回什么就是什么】作为结果
④ 建类     X := class { body }        ← 父类默认 object
           X := class Parent { body }  ← 父类 Parent(必须已经存在)
           ≡ 把 class 换成 type 完全等价(两者是同一个值)
```

- **`IsClass`**（我自己是不是一个类）判据是「**元类继承自 `type`**」——走类型关系，
  不是"某个成员名在不在"（后者是鸭子类型该管的事，会跟 interface/shape 的判据混在一起）。
- **能不能调用**的判据是「**自己那层**有没有 `call` 成员」(`Interpreter.Call.cs`，走 `LookupField`
  不走原型链)。引擎因此不需要知道"什么是类"。⚠️ 注意读成员是沿链的，所以 `c.call` 在实例上
  **读得到**（那是它的类的牌子），但 `c ()` 不行 —— 判据只看自己那层，将来 shape 检查也要照这个来。
  每个类对象建出来时都被装上 `call`（值是 `BoundCall`，`CallInto` 见到它就推 ClassInit 帧）。
  **用户自己定义 `call` 也能造出可调用的对象**——这就是将来 interface / shape 的地基。

### 内置类也有类体

内置类的成员是 C# 造好的值，所以它们的**预设类体**用 `LiteralExpr`（AST 节点：直接求值成
一个 C# 值）拼出来，里面只定义 `init`：

| 类对象 | 预设类体 |
|---|---|
| `type` | `init := (parent: type body) => 装 parent/block 并返回 this` \| `(body) => parent 默认 object` |
| `Integer` / `String` / … | `init := <CastToXxx>` |
| `List` / `Set` / `Dict` | `init := MakeDefaultCaster`（只收 `default`） |
| 用户类 | 用户写的 block |

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
- **引擎找构造器用 `LookupField`（一层、不走原型链）**，所以没写 `init` 的类仍报
  「类型 X 没有构造器（init）」，不会掉进 `type` 那层的建类逻辑。
- 用例：`tests/117`（基本）、`118`（挂钩建类过程）、`121`（拿到类再交出去）、
  `122`（typeof 链）、`193`（用户手写的那份）。

### 其余

- **继承是平铺的**：实例化时沿 parent 链从顶祖先到自身依次跑每层类体，所有层的字段落在
  **同一个 instance scope** 里，所以字段查找只需一层（`Scope.LookupField` 不走链、不走词法链）。
  实例 scope 的父是**类定义处的词法作用域**（类体里能引用外部变量），不是父类的实例 scope。
- **父类的 init 不会自动调用**，初始值要写在字段声明上（`a: int = 1`）；父类 init 里写的赋值不生效。
  子类重声明同名字段是覆盖。
- 用户类会被登记进 `AllTypes`，`Subtypes ()` 才反射得到它们。`AllTypes` 是静态表，
  而一个进程里会跑多个 Interpreter，所以每个 Interpreter 构造时调 `ResetUserTypes ()`
  清掉上一个留下的——否则上一个建过的类会出现在下一个的 `Subtypes ()` 里。
- **`parent` / `block` / `call` / `init` 是机制成员**，不出现在 `Fields ()` / `print obj` 里
  （`ObjectVal.MethodNames` 排掉它们）。`init` 这条容易漏：类体就跑在类对象自己的实例作用域里，
  所以类对象的 Scope 里**装着它自己的构造器**。
- **`parent` / `block` / `call` 是自绑定成员**（`ISelfBinding`）的话读出来要绑接收者 ——
  这条区分不能少，漏了的话 `type.Parent ()` 会把未绑定的内置方法当结果返回。

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

`tests/` 下的 golden test。`# expect-error` 预期异常，`# --- expected ---` 预期输出，`# todo` 等待实现
（**当前没有 todo 了**：最后 4 个是元类，随「类就是 ObjectVal」那一轮落地）。计数不写在这里——跑 `dotnet out/ravel.dll test` 看，或者按目录数。普通测试已按特性合并为 7 个文件：`01_core`(基础/运算符/列表/位运算/_)·`11_control_flow`·`13_functions`·`40_callcc`·`75_modules`(模块/eval/类/with/throw)·`98_types`(类型/反射/大数/作用域)·`99_collections`。expect-error 与 todo 因语义必须独立。

- `expect-error` 只看 `output.StartsWith("Error:")`，所以**解释器自己漏出来的 C# 异常不算数**：
  `CaptureOutput` 给非 `RuntimeException`/`SyntaxException`/`ExitException` 的异常加了
  `!! C# 异常 …` 前缀，它不以 `Error:` 开头，会直接把用例判 FAIL。加这个前缀当场就抓到过 6 个
  漏出的异常（其中 4 个是语法错误没有类型、2 个是 exit 没被单独接住）——现在两者都有正式类型了。
