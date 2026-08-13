# Ravel — 上下文文档

## 项目概述

Ravel 是一个 CPS (Continuation-Passing Style) trampoline 解释型编程语言。C# 实现，130 测试全过。

## 编译运行

```bash
cd /workspace/ravel
DOTNET_GCHeapHardLimit=0x10000000 dotnet publish -c Debug -o out
dotnet out/ravel.dll                    # 全量测试
dotnet out/ravel.dll path/file.rav      # 单文件
```

## 文件结构

```
Runtime/
  Interpreter.cs          256 行  CPS核心 + 控制流 + Block/Lambda
  Interpreter.System.cs   320 行  RegisterBuiltins + System模块
  Interpreter.Classes.cs  182 行  class/metaclass/callcc
  Interpreter.Expr.cs     331 行  表达式/调用/类型转换
  RuntimeValue.cs                值类型
  RuntimeType.cs                 类型系统
  Scope.cs / Variable.cs         作用域
  Step.cs                        CPS步骤
  BoxedValue.cs                  成员访问
Parser.cs / Lexer.cs / Ast.cs / Token.cs / TokenType.cs
Program.cs                       golden test runner

lib/
  predefined.rav          别名 + using
  std.rav                 Property + interface
  try.rav                 异常处理

tests/                    130 个 golden test (01-132)
```

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
        While If CallCC Exit With RavelMod Using

**值**: True False Default

## 全局变量

仅 1 个：`System`（模块自身）。

其他所有变量通过 `predefined.rav` 别名定义（`int := System.Integer` 等）。

## 类型创建（class）

设计见 [ADR-0001](adr/0001-class-and-type-system.md)。

- `class Parent { fields + init }` 是**唯一**用户类型构造器，产物是 `ObjectVal`。
- `type` 禁止创建类型，退化为元类型（`typeof` 结果、类型注解、类型值）。
- 内置类型是 C# 硬编码（元类 `type`），实例是 C# record，不走字段 shape。
- **元类 = 创建者**：`Type` 自指；`Class` 的元类是 `Type`；`class` 建的类元类是 `Class`；用户元类 M 建的类元类是 M。
- 字段存 `RuntimeType.FieldShape`（字段名 → 类型约束 + 默认值），实例化时复制到 `ObjectVal.Scope`。

## by 属性

```ravel
by age := Property (() => { _age; }) ((v: int) => { _age = v; })
```

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
int.Metaclass ()  # Type

# 对象
obj.Fields ()     # 字段名列表（排除 init/by/private）
obj.Copy ()       # 浅拷贝

# 模块
ravel "M"
using "file.rav"
```

## 测试

130 个 golden test。`# expect-error` 预期异常，`# --- expected ---` 预期输出。
