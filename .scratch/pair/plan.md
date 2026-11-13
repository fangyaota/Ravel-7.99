# pair（值类型的一对）

## 需求

用户要一个**值类型的一对**：`.Key ()` / `.Value ()` / `.At ()`，支持枚举。

背景：`dict` 当序列枚举出 `[键 值]` 两格，但那是 **list** —— list 是可变容器，
`==` / 哈希 / 去重都按引用走，所以同内容的两格既当不了字典的键，也按不上值比。

## 语义

- `Pair k v` 造一对（构造器就是类自己，与 `int x` / `bool x` 同一套口子：一次收一格，第一格交回"还差第二格"的函数）；造完就改不了（没有 `Key = …` 这种赋值口子）。
- **按值比**：`(pair 1 "a") == (pair 1 "a")` 为 `true`（内部走 `StrictSame`，
  所以 `1` 与 `1.0` 仍是不同格）；`===` 同口径。
- **能当键**：`d[pair "k" 1] = 7`，另一枚同内容的 pair 能查回来（`SameComparer` 走 `StrictSame`，
  哈希与相等同一口径）。
- **一串两格**：`Count ()` = 2、`At 0` 是键、`At 1` 是值、`ToList ()` = `[键 值]`，并继承
  `SequenceMembers` 的高阶方法（`Foreach` / `Map` / `Where` / `All` / `Any` / `Find` / `Fold` / `SortBy`）。
- **能枚举/解构**：`lib/iterator.rav` 里 `impl (IEnumerable Pair …)`，所以 `[k v] := p`、
  `for [k v] in …` 都成立；变量位是完整模式（类型/守卫/字面量都收）。
- `At` 只认 0 / 1；越界报「pair.At 的索引 N 越界 (一对只有 0 / 1 两格)」——
  拿 `()` 顶"没有第三格"会让 `[a b c] := p` 变成静默偏差。

## 落点

| 位置 | 改动 |
| --- | --- |
| `Runtime/Values/PairVal.cs` | 新类型：`Key` / `Value`，`ToString` = `键 -> 值`，`Equals` 只比两格（默认 record 会把父记录的成员作用域也算进去），`GetHashCode` 带 64 层递归保护（环只能经可变容器间接造出来） |
| `Runtime/Members/PairMembers.cs` | `: SequenceMembers`，加 `Key ()` / `Value ()` / `At i` |
| `Runtime/BuiltinClasses.cs` | 建 `Pair` 类、`Link(Pair, Object, Type)`、挂成员、进 `IValue` 那一支（`IsValueLike = true`）、进 `AllTypes`、封口 |
| `Runtime/Builtins/SysModule.cs` | `System.Pair` 这个**类对象**（`Pair := System.Pair` 那条别名的来源） |
| `Runtime/BuiltinClasses.Initializers.cs` | `BuiltinCtor(Pair, k => v => new PairVal(k, v))` —— 造一对的唯一口子 |
| `lib/predefined.rav` | `readonly Pair := System.Pair` |
| `lib/iterator.rav` | `impl (IMonad Pair {;})` + `impl (IEnumerable Pair { GetEnumerator = … ToList … })` |
| `Runtime/Members/SequenceMembers.cs` | `Items` 认 `PairVal`（两格） |
| `Runtime/BuiltinClasses.Sequences.cs` | `ElementsOf` 认 `PairVal` |
| `Runtime/BuiltinClasses.Operators.cs` | `SameValue`：值语义的类对象（`IsValueLike`）按内容比，其余对象仍比引用 |

## 测试

- `tests/class/66_pair.rav`：构造、`Key`/`Value`/`At`/`Count`/`ToList`、`p : Pair`、`p : IValue`、
  `==` / `===` / `!=`、当键、集合去重、`Foreach`、`[k v] := p`、`At` 越界文案。
- `tests/lang/03_functions.rav`：原来那段自己定义的 `pair := (a b: int) => { [a b]; }` 改名 `mkpair`
  （`pair` 现在与 `predefined` 里的构造器撞名，`:=` 会报"已经定义过"）。
- `tests/lib/271_system_members.rav`：`System.Fields ().Count ()` 135 → 137（多了 `Pair` 与 `MakePair`）。

## 没做的

- **dict 枚举没有换成 pair**：`d.ToList ()` / `d.Foreach` / `d.Entries ()` 仍是 `[键 值]` 两格的 list。
  换的话 `ToDict`（要求每项 `: IList`）也要认 pair，属另一件事，等用户点头。
- 没有字面量语法（`(1 "a")` 那种），构造只有 `pair k v` 一条路。
