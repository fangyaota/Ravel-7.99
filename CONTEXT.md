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

## metaclass

### 架构

所有类创建统一走 init——普通类和 metaclass 同一条线：

```
class { body }     → Class.init(parent, block)     → EvalClassCore
MyMeta { body }    → MyMeta.init(parent, block)    → base.init → Class.init → EvalClassCore
Interface { body } → Interface.init(parent, block)  → base.init → Class.init → EvalClassCore
```

入口：`EvalMetaclass`（在 `Interpreter.Classes.cs`）。

### 类创建流程

1. `EvalClassCore` 扫描 `cs.Variables`，`HasAttr("init")` 收集构造器
2. 多个 init 用 `CombineInits` 以 `|` 组合（参数不匹配自动试下一个）
3. `MakeClassConstructor` 创建实例化闭包
4. **包装代码**（`metaCtor != null` 时）：`Step.Run(classCtor)` → 创建类构造器 → 设 `this` → 调 `Meta.Init(parent, block)` → 返回 `this`
5. 无 metaCtor（普通 class）：直接返回 `classCtor`

### 无 init

`inits.Count == 0` → `ctor = FromTrampolined(_ => Error("No constructor"))`。普通类和 metaclass 同一条报错。

### MetaType

`ClassMeta.MetaType` 存 metaclass 类型。`FunctionVal.Type` 返回 `Meta?.MetaType`。`typeof MyClass` = metaclass 类型。`RuntimeType.MetaClass` 字段供 `Metaclass()` 方法查询。

不沿父类继承链传播——谁创建就是谁的 metaclass。

### base.init

`BoxedValue` 中 `br.ParentMeta.Init` 直接拿 FunctionVal，不走名字匹配。`_currentMetaType` 在包装代码中设置，`Class.DefineMethod("init")` 读取，传给 `EvalClassCore`。

### init 继承

不继承。子类无 init = 报 `No constructor`。必须显式 `init ctor := () => { base.init () }`。

### 关键文件

`Interpreter.Classes.cs` — EvalMetaclass, EvalClassCore, CombineInits, MakeClassConstructor

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
