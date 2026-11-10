# 成员表重构：一个对象一张表

状态：**已实施**（`8db986d` 改造 + `eec44a5` 测试调整，全量 `150 passed, 0 failed`，`--warn` / `--rebuild` 均绿）
基线：`0a89c1f`（`Integer` 的 `VirtualScope` 已落地，全量 `150 passed, 0 failed`）

> 实施记录见文末「九、实施记录」：实际落地与设计稿有出入的地方都在那里，**以代码为准**。

## 一、要解决什么

现在的成员查找是拼出来的：`Scope` 存数据，`ClassVal.InstanceTable` 存"给实例的成员"，
`MemberView` 再把两者按"本层 + 沿类链逐层翻表"合成一个 `Scope` 形状。于是同一个"读成员"
有两条独立通道，谁也说不清一张表里到底有什么；`InstanceTable` 又是一张"预先抄一份给实例用"
的副本表，和"对象自己的表"并存。

目标模型只有一句话：

> 每个对象、每个值都有一张自己的成员表 `VirtualScope`；读成员先查自己这张表，
> 表里没有就问"它是谁"（类或值类型）要定义，造出来存进自己这张表。

`MemberView`、`ClassVal.InstanceTable`、链式查找三样一起消失。

## 二、职责划分

| 类型 | 职责 |
| --- | --- |
| `Scope` | 词法变量表：`_vars` + `Parent`，只管"这个名字在不在可见作用域里" |
| `VirtualScope` | 成员表：一个对象/值一张；成员按需创建、创建后存本地；对象可写、值只读 |
| `ClassVal` | 类对象本身也持一张 `VirtualScope`（类级数据 + 构造器），不再有第二张实例表 |
| `BuiltinClasses` | 注册表：类 → 该类给实例的成员定义（`EngineMember` 写这里）；**不拆类、不加新文件结构** |

查找规则（全部情形共用一条）：

```
LookupField(name)
  → 本表已造好的那一格
  → Create(name)            // 问"我是谁"要定义
  → null                    // 交给调用点报"没有这个方法"
```

没有第二次跳转，没有 parent 链遍历。机制名（`name` / `parent` / `block` / `init` / `this` / `requires`）
**不进 `Create`**，所以 `(5).parent` 仍然报"没有方法"，这是既有行为，必须保持。

## 三、改动清单

### 1. `Runtime/Values/VirtualScope.cs`

- 构造改为 `VirtualScope(RuntimeValue owner, bool writable = false)`。
- 保留 `protected abstract Variable? Create(string name)` 与 `protected abstract IEnumerable<string> AllNames`.
- `LookupField`：本层 → `Create` → `null`；造出来的值走"不抛"的存档路径。
- `Define` / `Assign` / `Remove`：`writable` 为真按常规走，为假抛
  「值类型的成员表是只读的」。
- `MemberNames` / `Variables`：对象列举"已存 + 类注册表名单"，值类型列举静态清单；
  去重、顺序稳定（清单在前，注册表在后）。

### 2. `Runtime/Values/ObjectVal.cs`

- `Scope` 直接返回这台对象的 `VirtualScope`（对象可写）。
- `MemberScope => Scope`。
- `InstanceMembers => Type is ClassVal c ? c.Scope : Scope`。
- 删除 `_memberScope` / `_instanceMembers` 两个缓存字段。
- 类性判断、赋值判定、显示等其余职责不动。

### 3. `Runtime/Values/ClassVal.cs`

- 删除 `InstanceTable`。
- 类对象自身用 `VirtualScope` 承载类级数据（`name` / `parent` / `block` / 构造器）。
- `MethodNames` 改读自身表。

### 4. `Runtime/BuiltinClasses*.cs`（不拆分文件结构）

- 新增 `InstanceMembers: Dictionary<ObjectVal, Dictionary<string, Variable>>`。
- `EngineMember(type, name, impl, …)` 由"写 `InstanceTable`"改为写这张注册表。
- 建类时把父类那张表整份浅拷进子类（`Variable` 引用共享），**只发生一次**，
  之后没有链。
- 值类型的注册（`DefineOp` / `DefineMethod`）维持现状：写进各自 `VirtualScope` 的静态清单来源。

### 5. 值类型逐个迁移

`Int` 已完成。其余按同一模板：

```
Real / BigInt / Fraction / BigFraction / String / Char / Bool / Void / Range / Default
```

每个类型一个 `XxxScope : VirtualScope`，静态清单列出全部成员名，`Create` 从
`BuiltinClasses` 取实现。每迁一个，`BuiltinClasses.Operators` / `Methods` 里对应的注册删除。

### 6. 错误路径（**第一步必须做**）

现状：运行时错误交给 Ravel 侧处理，处理代码又要读异常对象的成员，读成员又依赖被改的查找通道，
于是成环、进程不返回。先修这条：

- `Interpreter.Stack` 的错误分发加 `_reporting` 重入标记。
- 正在报错时再抛出，直接输出 `!! {类型}: {消息}`，不再交给 Ravel 侧。
- 错误渲染只读现成字段（消息、源码位置、调用栈），不经过成员查找。

## 四、执行顺序（一次性改完再跑测试）

1. 错误路径重入保护。
2. `VirtualScope` 泛化（可写开关）。
3. 注册表 `InstanceMembers` + `EngineMember` 改道 + 建类时浅拷。
4. `ObjectVal` / `ClassVal` 切到新模型，删 `MemberView.cs`。
5. 值类型逐个迁移。
6. 编译、跑全量、原地修到绿。

中间不做小步提交；全绿后一次提交（值类型迁移可以按类型再拆成后续提交）。

## 五、验证

| 用例 | 期望 |
| --- | --- |
| `print (1 + 1)` | `2` |
| `print ([1 2].At 0)` | `1` |
| `print ([1].Count ())` | `1` |
| `o := (class { x := 1 }) (); o.x := 2` | 可写、读回 `2` |
| `try { (5).parent; }` | 报「类型 'Integer' 没有方法 'parent'」 |
| `print (1 / 0)` | 报错并退出，不挂死 |
| `class/195_member_lookup.rav` | 通过（`Fields ()` 清单顺序不变） |
| 全量 | `150 passed, 0 failed` |

## 六、风险

- **每个对象一张表**让构造变重（`List` 一类的几十个方法）。先实测；超标则退化为
  "类级模板 + 首次写时私有化"，但读路径仍保持"本表 → 模板一次"。
- 其他 `Scope` 子类（类体作用域、模块作用域）不受影响，`VirtualScope` 必须是独立分支。
- `Fields ()` 列举顺序是测试钉住的，清单顺序不能随手调整。
- 值表按值缓存（`IntVal` 的 `_virtual`）会让 record 自动相等把缓存算进去，
  需要手写 `Equals` / `GetHashCode`（`IntVal` 已这么做）。

## 七、为什么这样定

- 删 `InstanceTable`：它是"给实例预抄一份"的副本表，与"对象自己的表"天然重复；
  成员按需从类取得即可，不必预先铺。
- 删 `MemberView`：它是"把数据表与类型链拼成 Scope 形状"的适配器，
  一旦对象自己就是成员表，适配器没有存在理由。
- 值类型例外地共享表：整数数量级太大，不能每个值都造一张字典；共享静态清单 +
  每值一个视图，是这一前提下的最省形状。

## 九、实施记录

提交：`f758df6`（错误路径重入保护）→ `8db986d`（成员表统一）→ `eec44a5`（删 `Fields ()` 整串断言）。

设计稿里没写到、实施时才暴露出来的四处（都改了代码，不是改设计）：

1. **注册表键必须按引用比较**。`InstanceMembers` 是 `Dictionary<ObjectVal, …>`，而 `ClassVal`
   是 record —— 合成哈希会算上 `IsValueLike`／`ClassType` 这些"注册之后还会变"的字段，
   `SealBuiltins()` 一改哈希就漂，整类成员凭空消失（现象：`"hello".Length` 报没有方法）。
   现在用 `ReferenceEqualityComparer.Instance`。

2. **`Scope.MemberOf`**。模块值、插件值（`StackVal` 等）、`DotNetVal`、`ScopeVal` 都拿一个
   现成普通 `Scope` 当自己的表，不走 `VirtualScope`；于是 `MemberScope => Scope` 之后就没有
   注册表那半兜底（现象：`模块 'A' 没有方法 'Fields'`、`类型 'Stack' 没有方法 'Push'`）。
   现在 `Scope` 记着"这一层是哪个值自己的表"，`LookupField` 兜底问注册表。

3. **`InheritTable` 不继承 `init` / `this`**，且注册表那条 `init` 只给值类型
   （`BuiltinCtor(..., exposeInit: true)`）。构造器是每类自己一份、`this` 是每实例一格，
   照抄会让模块/结构的 `Fields` 里凭空多一个 `init`，还挡住顶层 `init := …`。

4. **"只认本层"的两处**（`Scope.OwnField`）：`StepSlotAssign` 换槽不能改到类共用那格
   （`352_slot_replace` 整类 5→42），`BodyScope.LookupHere` 要回答"用户/注入真的落下来了吗"
   （`dataclass` 靠它决定注不注入，实现在 `lib/dataclass.rav` 的 `mine`）。

另外：`VirtualScope` 造出来的格子存在 `_made` 里、**不进** `_vars`。进 `_vars` 的话，
"读一次 `MemberScope`"就让这个名字变成成员，`lib/enum.rav` 那条
`(cls.MemberScope ()).Define …` 会当场撞「已经定义过」。

值类型的迁移做法与设计稿第 5 条不同：**没有**给 `Real`/`String`/… 各写一个 `XxxScope`。
它们的表由 `RuntimeValue.MemberScope` 用 `ConditionalWeakTable` 按值懒建
（`Values.ValueTable`），`Create` 返回 null，定义统一从注册表兜底取 —— 这些类型的
`DefineOp`/`DefineMethod` 本来就都经 `EngineMember` 落进注册表，再拆九个类是重复。
只有 `Integer`（`IntegerScope`）和 `Bool`（`BoolTable`，因为 `BoolVal` 是 `FunctionVal`，
`Scope` 得覆写）保留自己的表类。
