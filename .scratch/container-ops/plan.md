# 容器的 `+` / `-`

## 需求

`添加 + - 运算符，与Union / Except 一致` —— set 上的 `+` 要和 `Union` 一样、`-` 要和 `Except` 一样;
list / dict 顺着同一套意思走。

## 口径

| 左 | `+` | `-` |
|----|-----|-----|
| list | 拼接(`Concat`) | 去掉右边出现过的元素 |
| set | 并集(`Union`) | 差集(`Except`) |
| dict | 合并,右边的键盖左边的 | 按右边的键删(右边是 dict 取它的键) |

- 都交回**新的容器**,原容器不动;`xs += [3]` 就是 `xs = xs + [3]`(改绑)。
- 右边一律走 `BuiltinClasses.ElementsOf`(list / set / dict / 插件容器);set 那头取 dict 的**值**(与 `Union` 同口径)。
- 例外:dict 的 `+` 右边必须还是 dict —— 两张键值面才谈得上合并,别的容器给元素没有对应的值。
- `-` 是"删掉右边出现过的元素",不是多重集减法:`[1 1 2] - [1]` 是 `[2]`。

## 落点

| 文件 | 改动 |
|------|------|
| `Runtime/Members/SetMembers.cs` | `[ClassOp("+")] UnionOp` / `[ClassOp("-")] ExceptOp`,直接转发给已有的 `Union` / `Except` |
| `Runtime/Members/ListMembers.cs` | `[ClassOp("+")] ConcatOp`(拼)、`[ClassOp("-")] ExceptOp`(按 `SameComparer` 去元素) |
| `Runtime/Members/DictMembers.cs` | `[ClassOp("+")] MergeOp`(右盖左)、`[ClassOp("-")] DropKeysOp`(dict 取键、别的容器把元素当键) |
| `tests/class/64_operator_class.rav` | 新增 `ravel "64_container_ops"` 段(三种容器 + 两边别的类型 + 原容器不动 + 两条报错文案) |

`+` / `-` 本来就在 `OperatorSymbols.All` 里,不用动解析器;运算符解析走左操作数的成员表,所以只要各 `XxxMembers` 挂上 `[ClassOp]` 就生效。

## 没做

- `*` / `/`(容器没有自然的乘法/除法);
- 多重集减法、按索引的减法(容易和 `RemoveAt` 混);
- `Sorted` / `Chunk` / `Zip` 这几个缺的方法(不在本次范围)。
