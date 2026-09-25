# Ravel — 上下文文档

## 项目概述

Ravel 是一个**显式持久帧栈**解释型编程语言（原 CPS trampoline 已移植替换，见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)）。C# 实现，43 passed / 0 failed / 7 todo。

## 编译运行

```bash
DOTNET_GCHeapHardLimit=0x10000000 dotnet publish -c Debug -o out
dotnet out/ravel.dll test                # 全量测试(有 FAIL 时退出码 1)
dotnet out/ravel.dll path/file.rav      # 单文件
dotnet out/ravel.dll                    # REPL
```

## 文件结构

```
Runtime/                         求值器按职责拆成多个 partial class 文件
  Interpreter.cs          入口/类型表/ResolveType/ThrowRavel/CheckFieldAccess
  Interpreter.Stack.cs    帧栈推进循环(StepOnce/Return/PushChild + 块执行)
  Interpreter.Nodes.cs    节点状态机(每 AST 节点一个 NodeFrame,按 Results.Count 分阶段)
  Interpreter.Call.cs     CallInto 调用分派 + 合成控制帧的推帧助手
  Interpreter.Control.cs  控制帧状态机(while/if/with/foreach/callcc/using/eval/类初始化)
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

tests/                    50 个 golden test(普通 + expect-error + todo + fixture)
```

## 求值器架构(显式帧栈)

见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)。要点：

- 整个程序一个块根帧,`StepOnce()` 扁平循环逐帧推进,C# 栈恒平。
- 每节点状态机按 `Results.Count` 推进;`Return` 把结果交给父帧。
- 调用分派(`Interpreter.Call.cs` 的 `CallInto`):`ControlFunction`(控制帧)/`LambdaVal`(推 body 帧)/`BlockVal`/`TypeVal`(类→ClassInit 帧)/`ComposeVal`(prepend/append)/`BoundClassOp`(类运算符)/`PartialCtor`(半成品构造器→CtorApply 帧)/`ContinuationVal`(还原帧链);其余 `FunctionVal` 走默认分支,同步调 `Body(arg)` 把值塞回 sink。同步函数一律是「参数→结果」,没有 Step 包装(旧 CPS 的 `Done` 壳已删除)。
- 控制内建(`if`/`while`/`with`/`foreach`/`callcc`/`using`/`eval`)= `ControlFunction(Kind, Arity, Args)` 纯数据,收满参数推控制帧。求值器内部还会合成 `Alternate`/`ClassInit`/`Compose`/`ClassOp`/`CallAssign`/`CallReturn`/`CtorApply` 控制帧。
- **构造器调用与普通函数同一条柯里化路径**:`Point 3 4` ≡ `((Point 3) 4)`。`ClassInit` 建好对象、跑完类体后把参数喂给 `init`;`init` 还返回函数(参数没收齐)就交出 `PartialCtor` 半成品,由 `CtorApply` 帧继续喂,直到 `init` 应用完才把对象交出来。
- callcc 单发(abort body)/多发(重跑局部 onDone + 返回参数给调用者)。
- 深度递归 20 万层安全(原 CPS ~4k 层爆栈)。

## 类型层次

```
Object (parent=self)
├── ValueType
│   ├── Integer
│   ├── Float
│   ├── Bool
│   └── String → BigInt → Fraction → BigFraction
├── Function
│   └── Type
│       └── Class
│           ├── List / Set / Dict / Ravel
│           └── 用户 metaclass
├── Void / Exception
├── Any (顶类型, parent=null)
└── Every (底类型, parent=null)
```

## System 模块

内置模块，解释器启动时创建。包含所有类型和核心函数：

**类型**: Integer String Bool Float BigInteger Fraction BigFraction
        List Set Dict Object Void Function Type Class
        AnyType EveryType ExceptionType ValueTypeVal

**函数**: WriteLine Write ReadLine Assert TypeOf Eval RandInt
        While If CallCC Exit With RavelMod Using unsafe
        property Foreach currentScope

**值**: True False Default

## 全局变量

仅 1 个：`System`（模块自身）。

其他所有变量通过 `predefined.rav` 别名定义（`int := System.Integer` 等）。

## 类型创建（class）

设计见 [ADR-0001](adr/0001-class-and-type-system.md)。

- `class Parent { fields + init }` 是**唯一**用户类型构造器，产物是 `ObjectVal`。
- **构造器就是名字叫 `init` 的变量**（类体里写 `init := () => {...}`），不是修饰符——曾经写过 `init ctor := ...`，已废弃。一个类最多一个构造器，没有按参数类型重载；要分派就在 `init` 里自己判断。
- `type` 禁止创建类型，退化为元类型（`typeof` 结果、类型注解、类型值）。
- 内置类型是 C# 硬编码（元类 `type`），实例是 C# record。
- **元类 = 创建者**：`Type` 自指；`Class` 的元类是 `Type`；`class` 建的类元类是 `Class`；用户元类 M 建的类元类是 M。
- **继承是平铺的**：子类实例化时沿 `RuntimeType.Parent` 链从顶祖先到自身依次跑**每层类体**，所有层的字段落在**同一个 instance scope**里，所以字段查找只需一层（`Scope.LookupField` 不走链、不走词法链）。
- 类实例化（`StepClassInit` 控制帧，阶段由 `Count` 推进）：建 instance scope → 打包 `ObjectVal` 并绑 `this`（`ClassType` = 最终子类，在第一个类体执行**之前**）→ 逐层跑类体 → **在实例作用域里按名字 `init` 找构造器**。各层平铺在同一个 scope，子类的 `init` 覆盖父类的，所以取到的天然是「最具体层声明的那个」；子类没写就落回父类的，整条链都没有才报错。
- **父类的 init 不会自动调用**，初始值要写在字段声明上（`a: int = 1`）；父类 init 里写的赋值不生效。子类重声明同名字段是覆盖。
- **没有 `base`**：曾用 `base = 父类()` 手工构造父类实例挂到实例 scope 的 `parent` 字段上，现已移除（`parent` 字段、`DefineBase`、`withDeep` 深拷贝一并删除）。元类路径的 `base.init parent block` 因此暂时无实现手段（117/118/121/122 保持 todo）。

## by 属性

```ravel
by age := property (() => { _age; }) ((v: int) => { _age = v; })
```

实例读 `obj.age` / 写 `obj.age = v` 走 getter/setter（`CallInto` 派发，lambda getter/setter 也有效）。

## 多参数 lambda

```ravel
add := (x: int y: int) => { x + y }
add 3 4    # 柯里化
```

## _ 占位符

```ravel
add1 := _ + 1    # (x: object) => x + 1
```

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

# 模块
ravel "M"
using "file.rav"
```

## 测试

50 个 golden test。`# expect-error` 预期异常，`# --- expected ---` 预期输出，`# todo` 等待实现。当前 43 passed / 0 failed / 7 todo。普通测试已按特性合并为 7 个文件：`01_core`(基础/运算符/列表/位运算/_)·`11_control_flow`·`13_functions`·`40_callcc`·`75_modules`(模块/eval/类/with/throw)·`98_types`(类型/反射/大数/作用域)·`99_collections`。expect-error 与 todo 因语义必须独立。
