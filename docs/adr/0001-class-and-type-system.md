# ADR-0001: 类型系统重设计 —— class 是唯一类型构造器，元类 = 创建者

- **状态**: Accepted（设计已确认，待实现）
- **日期**: 2026-08-13

> ⚠️ **本文件写的是设计意图，其中一部分从未落地或已被取代。** 实际模型以 `CONTEXT.md` 的
> 「类型与对象：类就是 ObjectVal」和「运算符」两节为准。要点：
> - **`RuntimeType` / `TypeVal` 这套表示已经没了**：「一个类」就是一个 `ObjectVal`，
>   类性由它 Scope 里的 `parent` / `block` / `name` / `call` 表达。本文里的 `RuntimeType`
>   一律读成「类对象」，`TypeVal` 读成「类对象那个值」（`RuntimeValue.Type` 返回的是元类）。
> - **`FieldShape` 依然不存在**。类体是运行时按祖先链逐层执行的,没有「先把字段收进 shape、
>   实例化时再复制到 ObjectVal.Scope」这一步。「继承」和「class 的 Initializer」两节末尾
>   各有一条更详细的更正。
> - **`base.init` 换成了 `parentInit := init` 惯用法**——`base` 没有回来，元类建类靠
>   「覆盖之前先把继承来的 `init` 存下来」这一招。子类 init 里的 `base = 父类()` 也已移除
>   ——继承改成了平铺。
> - **元类已经实现**（元类 = 父类是 `type` 的类）：117/118/121/122 不再是 `# todo`。
>   但**不是**靠本文设想的 `Metaclass` 字段 + `Class` 类型，见上面两条。
> - **构造器靠名字识别**:类体里写 `init := () => {...}`,`init` 是变量名不是修饰符。
> - **元类 = 创建者**这条仍然成立,但**没有 `Class` 类型了**:`class` 就是 `type` 的别名
>   (`predefined.rav` 里 `class := System.Type`),两者是同一个值。用户类直接挂在 `Type` 下,
>   `typeof Person` 和 `typeof int` 都是 `Type`;建类挂在 `Type` 那层的 `init` 上
>   (没有 `Type.Initializer` 这个成员)。
>   本文里 `Class <: Type`、`X.Metaclass = Class`、`Class [Type]` 这些说法都按此理解——
>   把 `Class` 读成 `Type` 即可,另见 `CONTEXT.md`「类型与对象：类就是 ObjectVal」。
> - **「`type` 的构造器角色」当初被否掉的理由已不成立**。背景那节说「曾考虑 `type` 创建类型
>   但否定了」,理由是要给 `class` 和 `type` 划清界限。现在界限取消了:`type { ... }` 就是建类,
>   和 `class { ... }` 一回事。内置类型的 `init` 就是各自的转换器(没有单独的转换器概念了)。

## 背景

上一轮重构删除了自定义类/元类创建系统（`Interpreter.Classes.cs`、`ClassMeta`、`MetaType` 等），38 个依赖它的测试转成 TODO。现在需要重新设计「类型创建」机制。

曾考虑「`type` 创建类型、`class` 继承 `type`」，但经过推敲否定了 `type` 的构造器角色：内置类型（`Int`/`List`/`String`…）永远不是 `class`，它们的实例是 C# record（`IntVal.Value`、`ListVal.Elements`），字段编译期硬编码，与 `class` 的「ObjectVal + 动态字段」是两条不同的值路线。强行让 `type` 既服务内置类型又服务结构化类型会制造裂缝。

## 决策

### 三条路线并存，各归其位

| 路线 | 谁创建 | 实例表示 | 字段 | 元类 |
|---|---|---|---|---|
| **内置类型**（Int/List/String…） | C# 硬编码（`RuntimeType` 静态单例 + `CastToXxx`） | C# record（`IntVal.Value`…） | record 硬编码字段 | `Type` |
| **用户类型** | **只有 `class`** | `ObjectVal` | `FieldShape → Scope` | **创建者** |
| **`type`** | ——（不创建） | —— | —— | `Type`（自指） |

### `type` 禁止创建类型

`type` 的 `Initializer` 不设（`type 1` 实测报 **`类型 Type 不能作为构造器调用`**）。`type` 退化为纯「元类型」概念，只承担：`typeof` 结果、类型注解、类型值。

`type object {...}` 直接报错。当时改的是几个 `type object {...}` 用例，全部改成 `class object {...}`
（测试编号后来重排过，别按老编号找；权威清单见 `CONTEXT.md` 的「测试」一节）。

### `class` 是唯一用户类型构造器

`class Parent { fields + init }` 创建类，产物统一是 `ObjectVal`。`Class <: Type` 的继承链不变（class 是 type 的子类这个事实保留），但「创建」只有 `class` 一个入口。

### 元类 = 创建者

一条统一规则：**一个类型的元类，就是创建它的那个构造器对应的类型**。

- `Type.Metaclass = Type`（自指，根元类）
- 内置类型的 `Metaclass = Type`
- `Class.Metaclass = Type`
- `class` 构造器建 X → `X.Metaclass = Class`
- 用户元类 M 建 X → `X.Metaclass = M`

规则自洽、可无限延伸，无需为「元类的元类」特判。

## 详细设计

### 元类层次（可扩展链）

```
Type [Type]      ← type 自指，根元类，链的起点
Class [Type]     ← class 是内置的，元类 type
M [Class]        ← 用户 class class 建的元类 M，元类 = class（创建者）
A [M]            ← 用户 M 建的类 A，元类 = M
实例 [A]         ← 实例的类型 = A
```

`class` 在链上位置固定（元类恒为 `type`），用户日后叠多少层 `class class class ...` 都只是往链下游加节点，上游不动。

### RuntimeType 新增两个字段

```csharp
/// 字段 shape：字段名 → (类型约束, 默认值)，实例化时复制到 ObjectVal.Scope
internal readonly Dictionary<string, (RuntimeType Type, RuntimeValue Default)> FieldShape = [];

/// 元类：创建本类型的那个构造器对应的类型
internal RuntimeType Metaclass { get; set; }
```

### TypeVal.Type 改为动态返回

```csharp
public override RuntimeType Type => Value.Metaclass;
```

取代现在的 `=> RuntimeType.Type`。这样 `typeof int` = `type`、`typeof A` = `class`/`M`、`typeof (A 实例)` = `A`，三者可区分。

### class 的 Initializer

> ⚠️ **本节描述的 `FieldShape` 方案从未落地。** 实际实现里 `RuntimeType` 上只有 `Body`/`Initializer`/`Metaclass`，
> 类体是运行时按祖先链逐层执行的，不是先把字段收进 shape 再复制到实例。见下面「继承（平铺，无 base）」一节。

`class Parent { init := (x) => {...} name: string = "" }` 执行时：

1. 参数 `Parent`（TypeVal）+ `body`（BlockVal）。
2. 压临时 Scope 跑 `body`——字段声明 `name: string = ""` 是普通 `VarDefinition`，落到临时 Scope。
3. 遍历临时 Scope，收进新 `RuntimeType.FieldShape`；`init ... := ...` 的 `VarDefinition`（带 `IsInit`）单独抽出，不进 FieldShape。
4. `new RuntimeType(parent: Parent.Value)`，塞 FieldShape，`Metaclass = Class`。
5. 默认实例化器 = 「按 FieldShape 建 `ObjectVal` → 把 `this` 绑到实例 Scope → 跑 init」。
6. 返回 `new TypeVal(新类型)`。

`this` 无新机制——就是「`CurrentScope` 临时设成实例 Scope 再跑 init」，与 `with` 同款。

### 实例化统一路径

`T args` / `A args` 都走 `CallInto` → `TypeVal` 分支。内建类型的产物用 `RuntimeType.Initializer`（转换器），用户 class 的产物用 `ClassInit` 控制帧「建对象 + 跑类体 + 跑 init」。

### 继承（平铺，无 base）

> **本节已按当前实现重写。** 原文写的是 `FieldShape` 子类叠加 + `base.init()` 沿 Parent 调初始器——`FieldShape` 这个结构在代码里从未落地（`RuntimeType` 上只有 `Body`/`Initializer`/`Metaclass`），`base.init()` 也一度退化成 `base = 父类()` 的显式构造再被移除。下面是实际模型。

继承是**平铺**的：实例化 `T` 时沿 `RuntimeType.Parent` 链从顶祖先到自身依次跑**每一层的类体**，所有层的字段落在**同一个 instance scope** 里。因此字段查找只需一层——`Scope.LookupField` 既不跳实例链也不走词法链。

- 每个实例只有**一个** scope，没有「父类实例」这层间接（曾经的实例层 `parent` 字段链、`DefineBase`、`withDeep` 深拷贝都已删除）。
- `this` 与 `ObjectVal` 在**第一个类体执行之前**就绑好，`ClassType` 是最终子类。
- **父类的 init 不自动调用**：每层只跑类体，然后在实例作用域里按名字 `init` 找构造器。因为各层平铺在同一个 scope、子类的 `init` 覆盖父类的，所以取到的天然是「最具体层声明的那个」；子类没写就落回父类的，整条链都没有才报错。初始值必须写在字段声明上。
- **构造器靠名字识别**：类体里写 `init := () => {...}`，`init` 就是变量名（不再是修饰符，也不再自动生成 `init` 别名）。一个类最多一个构造器，没有按参数类型重载。
- 子类重声明同名字段 = 覆盖（`DefineOrReplace`），祖先同类字段被替换。
- `RuntimeType.Parent` 只剩两个用途：**收集类体**（`CollectBodies`）与类型层的 `IsAssignableTo` / 方法表查找。

**没有 `base`。** 元类创建类的 `base.init parent block` 因此暂时没有实现手段，相关用例（117/118/121/122）保持 `# todo`。重启元类特性需要另立机制。

### class class 的两个角色

`class class {...}` 里两个 `class` 语义不同：

1. 第一个 `class`：**构造器**（「创建类型」这个动作）。
2. 第二个 `class`：**父类**（M 继承 class → M 的实例是类，即 M 是元类）。

M 的**元类** = 第一个 `class` → `Class`；M 的**父类** = 第二个 `class`。两个维度互不干扰。

## 后果

### 砍掉

- **元类机制**（`type type` / `Metaclass()` 方法那套）：不再需要「类型的类型去生成类型」的二阶结构。
- **`Proto` / `Instantiate`**：把字段 shape 的临时 Scope 暴露出去再塞回，是泄漏抽象。建议砍掉。（已砍：`Proto` 现在不存在，`tests/142_property_attrs.rav` 的注释里留了记录。）
- **`setInitializer`**：保留与否待定——它干净地表达「覆盖默认实例化器」，与 `FieldShape` 正交，倾向保留。

### 测试影响

- `type object {...}` → `class object {...}` 那几个用例：已改（编号已重排，不指旧号）。
- `type type { init new }` 那类「元类实现」用例：作废，重写或删。
- 类型树标注 `Class [Class]` 改为 `Class [Type]`（class 元类改成 type 的体现）——
  落地在 `tests/125_type_tree.rav`，它的期望输出就是 `Class [Type]`。
