# 字典键 / 集合元素：改成按 `===` 比

## 需求（用户原话）

> 现在dict的键接收自定义类吗？如果不是，改为接受
> 使用===作为判断
> 可以保留Keyed

## 改动前的样子

- 引擎 `BuiltinClasses.KeyArg` 只放行 `ClassVal.IsValueLike` 的类型，其余（类实例、list、dict…）直接报
  「…得是值类型（数 / 串 / 字符 / 布尔 / `()`），得到 List」。
- 库那一层（`lib/keys.rav` 的 `IDict dict`）先用 `Keys.Of k` 调 `k.Key ()`，把对象**规范**成值类型的键，
  再交给引擎的 `SysGet` / `SysSet`。所以类实例能不能当键，取决于它有没有登记 `IKey` 并写 `Key`。

## 改动后

- 键**不再挑类型**：任何值都能当键。
- 「是不是同一个键」按 `===`（`BuiltinClasses.StrictSame`）：
  - 运行期类型不同 → 不等（`1`、`1.0`、`"1"` 是三个格子）；
  - 值语义（数 / 串 / 字符 / 布尔 / `()`）→ 按内容；
  - 容器 / 类实例 / 函数 / 模块 / 类型 → 按**引用**。
- 集合的元素去重也是这套口径（`{1 1.0}` 是两格，`{[1 2] [1 2]}` 也是两格）。

## 落点

| 文件 | 改动 |
|------|------|
| `Runtime/Values/SameComparer.cs`（新） | `IEqualityComparer<RuntimeValue>`：`Equals` 直接问 `StrictSame`；`GetHashCode` 对"非值类型对象"用 `RuntimeHelpers.GetHashCode`（引用哈希），其余用 record 的内容哈希 |
| `Runtime/Values/DictVal.cs` | 构造时 `new Dictionary<…>(entries, SameComparer.Instance)` |
| `Runtime/Values/SetVal.cs` | 构造时 `new HashSet<…>(elements, SameComparer.Instance)` |
| `Runtime/BuiltinClasses.Methods.cs` | `KeyArg` 改成直通（引擎的字典键）；`StrictKeyArg` 先留着给扩展，随后按用户指示**删掉**——不再有严格判定 |
| `Runtime/Builtins/PluginApi.cs` | `PluginApi.Key` 直通（早先挂 `StrictKeyArg`，该判定删掉后也直通） |
| `lib/keys.rav` | `IDict dict` 那五条槽改成直接传 `k`（`Get` 调 `instance.At k`，不再 `Of k`）；`IKey` / `Of` / `Keyed` 原样保留 |
| `Runtime/Members/DictMembers.cs` | `SysGet` 改名成 `At`（`d[k]` 和 `d.Get k` 都落到它）；`SysSet` / `SysHas` / `SysRemove` / `SysGetOr` 名字不动 |

## 保留 `Keyed` 与 `IKey` 的理由

"内容相等就算同一个键"仍然是个真需求（`Group` / `Frequency` 就靠它）。`Keyed` 是显式的那一层：
先 `Key ()` 拿值类型的规范键，再落到引擎的字典上；`IKey` 是它的协议。所以它们留着，但**普通 `dict` 不再经过它们**。

## 边界

- `{1 -> "a"}[1.0]` 报「键不存在: 1」（`===` 认为不是同一个键）；
- `d[l] = v` 用同一个 list 实例取得回来，`d[别的同内容 list]` 取不回来；
- 扩展侧也不再挑类型（`PluginApi.Key` 直通）：图顶点可以是 list，按实例算 —— `295_graph.rav` 原来那条"起点得是值类型"换成"同一个 list 实例加得进去、`Neighbors` 换个同内容实例查不到"；
- `Keys.Of` 对"`Key` 交回容器"的类仍旧报错（`IKey` 那套的校验没动）。

## 测试

- `tests/class/235_ikey.rav` 重写了键的那两段：`1` / `"1"` / `1.0` 三格、list 按引用、类实例按引用、
  `d2.Keys ()` 只剩真正存进去的那枚；`IKey` / `Keys.Of` / `Keyed` 那几段原样留着（它们还管用）。
- 跑过：`build.sh --warn --pick=class` 16、`test lang` 20、`diag` 9、`module` 15、`net` 7、`task` 3、`tests/lib/*.rav` 39 条逐文件全绿。

## 文档

- `07-collections.md` 7.3 讲清键的判据；`Group` / `Frequency` 那句改成"先调 `.Key ()` 取规范键"。
- `03-operators.md` 3.2 加一句"字典的键、集合的去重也按 `===` 比"。
- `05-functions.md` 5.13 从"键只能是值类型"改成"键可以是任何值"，`IKey` 那段改成讲 `Keyed`；
  顺手修了 `impl (IKey Rec default)` 这个**早就失效**的写法（`default` 现在是函数，空体要写 `{;}`）。
- `06-patterns.md` 键那段同步；`08-classes.md` 的"空体写 `default`"改成"空体写 `{;}`"。
