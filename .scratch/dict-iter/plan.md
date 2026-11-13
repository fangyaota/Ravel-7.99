# dict 当序列：元素改成 `[键 值]`，`Has` 改叫 `ContainsKey`

## 需求（用户）

1. dict 枚举得到**键值对**（`[键 值]` 两格），不是值。
2. dict 不**主动**提供 `Contains`（继承来的那条通用 `IEnumerable.Contains` 可以留着），
   改提供 `ContainsKey` / `ContainsValue`。

## 落点

| 文件 | 改动 |
|------|------|
| `Runtime/Members/SequenceMembers.cs` | `Items(DictVal)` 改成逐项 `new ListVal([k, v])`（新 `Pairs` 帮手）；删掉 `[ClassMethod("Contains")]` —— 它原来给 list / set / dict 共用，dict 不该有 |
| `Runtime/Members/ListMembers.cs` / `SetMembers.cs` | 各自补一条 `[ClassMethod("Contains")]`（原来那份是从父类继承来的） |
| `Runtime/Members/DictMembers.cs` | `Has` → `ContainsKey`、`HasValue` → `ContainsValue`；`Entries ()` 与 `Pairs` 共用一份摊法 |
| `Runtime/BuiltinClasses.Sequences.cs` | `ElementsOf(DictVal)` 也改成两格 —— 容器运算（`+` / `-` / `Union` / `Except` / `SeqOp` / `PluginApi.Elements`）与 `ToList` 同一个口径 |
| `Syntax/Lowering.cs:608` | 字典模式拆解内部调的是 `ContainsKey`（原来写死 `"Has"`） |
| `lib/keys.rav` | `IDict` 槽 `Has` → `ContainsKey`；`Scope` 的实现同步；`Keyed.Has` → `ContainsKey` |
| `lib/args.rav` 等 12 个库文件 | 调用点 `.Has` → `.ContainsKey`（`Args.Has` 这个工具函数也改名） |
| `docs/tutorial/07-collections.md`、`06-patterns.md`、`09-modules.md` | 口径与名字 |
| `tests/lang/10_collections.rav`、`class/217_ienumerable.rav`、`class/64_operator_class.rav` | 跟着新语义改断言 |

## 语义

```ravel
d := {"a" -> 3 "b" -> 1}

d.ToList ()                      # [[a 3] [b 1]]
[x for x in d]                   # [[a 3] [b 1]]
d.Foreach ((p) => { p.At 1; })   # 3 / 1

d.ContainsKey "a"                # true
d.ContainsValue 1                # true
d.Contains 1                     # false —— 没有 dict 自己的 Contains，只剩通用那条，比整个 [键 值]
```

- `d.Min ()` / `Max ()` / `Join` / `Sum` 现在看到的是两格项，比不了就报「比不了 List 与 List」；
  要按值算就 `(d.Values ()).Min ()`。
- `d.ToDict ()` 与原字典等价（`ToList` / `ToDict` 对称）。
- 集合代数里 dict 当**另一头**时也给两格：`{1 2}.Union {"a" -> 1}` → `{1 2 [a 1]}`。

## 没做

- 没给 dict 加 `ContainsKey`/`ContainsValue` 之外的别名（`Has`/`HasValue` 已删干净，
  `Ravel.Structures` 里插件类自己的 `Has` 不动）。
- 没动 `IEnumerable.Contains` 那条通用实现 —— 用户明确说"不**主动**提供"，不是"不许有"。
