# Ravel — 上下文文档

## 项目概述

Ravel 是一个**显式持久帧栈**解释型编程语言（原 CPS trampoline 已移植替换，见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)）。C# 实现，31 passed / 0 failed / 18 todo。

## 编译运行

```bash
cd /workspace/ravel
DOTNET_GCHeapHardLimit=0x10000000 dotnet publish -c Debug -o out
dotnet out/ravel.dll test                # 全量测试
dotnet out/ravel.dll path/file.rav      # 单文件
dotnet out/ravel.dll                    # REPL
```

## 文件结构

```
Runtime/
  Interpreter.cs          入口/类型表/ResolveType/ThrowRavel
  Interpreter.Stack.cs    显式帧栈求值器核心(StepOnce 循环 + 每节点状态机 + 控制帧)
  Interpreter.System.cs   RegisterBuiltins + System模块 + 控制内建注册
  Frame.cs / RList.cs     帧链(不可变持久) / 持久化单链表
  RuntimeValue.cs                值类型
  RuntimeType.cs                 类型系统
  RuntimeType.Initializers.cs    class 构造器/CollectInit/DefineBase
  RuntimeType.Methods.cs / Operators.cs   方法表(存 FunctionVal)/运算符
  Scope.cs / Variable.cs         作用域
  Step.cs                        仅 Done(同步调用结果)
  BoxedValue.cs                  成员访问
  Values/                        FunctionVal/LambdaVal/BlockVal/TypeVal/ControlFunction/
                                 MethodVal(BuiltinMethodVal/BoundClassOp)/ComposeVal/
                                 ContinuationVal/ObjectVal/.../各基础值
Parser.cs / Lexer.cs / Ast.cs / Token.cs / TokenType.cs
Program.cs                       golden test runner + REPL 入口

lib/
  predefined.rav          别名 + using
  std.rav                 Property + interface
  try.rav                 异常处理

tests/                    49 个 golden test(普通 + 14 expect-error + 18 todo + fixture)
```

## 求值器架构(显式帧栈)

见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)。要点：

- 整个程序一个块根帧,`StepOnce()` 扁平循环逐帧推进,C# 栈恒平。
- 每节点状态机按 `Results.Count` 推进;`Return` 把结果交给父帧。
- 调用分派:`BuiltinMethodVal`/`Sync`(算值)/`LambdaVal`/`BlockVal`/`TypeVal`(类→ClassInit 帧)/`ControlFunction`(控制帧)/`ContinuationVal`(还原帧链)/`BoundClassOp`(类运算符)/`ComposeVal`(prepend/append)。
- 控制内建(`if`/`while`/`with`/`foreach`/`callcc`/`using`/`eval`)= `ControlFunction(Kind, Arity, Args)` 纯数据,收满参数推控制帧。求值器内部还会合成 `Alternate`/`ClassInit`/`Compose`/`ClassOp`/`CallAssign`/`CallReturn` 控制帧。
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
- `type` 禁止创建类型，退化为元类型（`typeof` 结果、类型注解、类型值）。
- 内置类型是 C# 硬编码（元类 `type`），实例是 C# record。
- **元类 = 创建者**：`Type` 自指；`Class` 的元类是 `Type`；`class` 建的类元类是 `Class`；用户元类 M 建的类元类是 M。
- 类实例化（`StepClassInit` 控制帧）：新 instance scope 跑类体 block 收集字段 → 打包 `ObjectVal` → 绑 `this` → `DefineBase` → `CollectInit` 找带 `init` 属性的构造器（`|` 组合）→ 调 init。

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
obj.Fields ()     # 字段名列表(模块=作用域变量;对象=类型方法)
obj.Copy ()       # 浅拷贝

# 模块
ravel "M"
using "file.rav"
```

## 测试

49 个 golden test。`# expect-error` 预期异常，`# --- expected ---` 预期输出，`# todo` 等待实现。当前 31 passed / 0 failed / 18 todo。普通测试已按特性合并为 7 个文件：`01_core`(基础/运算符/列表/位运算/_)·`11_control_flow`·`13_functions`·`40_callcc`·`75_modules`(模块/eval/类/with/throw)·`98_types`(类型/反射/大数/作用域)·`99_collections`。expect-error 与 todo 因语义必须独立。
