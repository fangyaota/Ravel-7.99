# `->`：造一对的普通运算符

## 需求

> 给我添加 `->` 运算符，右结合，返回 `pair`，仅在不在 `{}` 里面时有用，如 `{(1 -> 1)}`、`{1 -> 1;}`、`[1 -> 1]`

后续两次纠正定了口径：

1. "参考 `===` 和 `:`" —— 不要解释器特判，要走运算符那套基础设施。
2. "是普通运算符！！！！！！！！和 `===` 一样！！！！！！！！" —— 住在 `Object` 的成员表里，因此**可被类覆盖**（`-> := (o) => …`）；之前说的"不可覆盖"作废。

## 落点（对照 `===` 的六处）

| `===` | `->` 的对应 |
|-------|-------------|
| `Syntax/TokenType.cs` 的 `EqualEqualEqual` | 复用已有的 `TokenType.DictArrow`（词法器早就发它，字典条目也用它）|
| `Syntax/Lexer.cs:263` 的 `("===", …)` | 无需改：`MultiCharOps` 里已有 `("->", TokenType.DictArrow)` |
| `Syntax/OperatorTable.cs:35` `Add("===", 10, TokenType.EqualEqualEqual)` | `Add("->", 11, TokenType.DictArrow, right: true)`（右结合；档位在比较/判定 10 与移位 12 之间，见下面「档位」一节）|
| `Syntax/Ast.cs:8` `OperatorSymbols.All` | 加 `"->"`（成员表扫描校验 + 用户可写 `-> := …` 的名单）|
| `Syntax/Parser.Statements.cs:20` `IsOperatorToken` | 加 `TokenType.DictArrow`（类体能定义它、`a.->  b` 能当成员取）|
| `Runtime/Members/ObjectOperatorMembers.cs:30` `[ClassOp("===")]` | 加 `[ClassOp("->")]`，语义走 `BuiltinClasses.MakePair` |
| `Operators.IsCompare` 收 `===` | **不收** `->`：它不是比较，串起来也不成链，右结合 |

解释器里**没有** `->` 的任何特判：`StepBinaryOp` → `BindOperator` → 成员表，与 `===` 同一条路。

## 和字典的碰撞（`->` 独有的问题）

字典字面量 `{k -> v}` 顶层那个 `->` 是条目分隔符。三处处理：

- `Syntax/Postfix.cs`：`Postfix` 多一格 `Arrow`（与 `Comp` / `Ternary` 同一套"按入口说清、不靠继承"的做法），新增 `DictKey = new(false,false,false,false,true,false)`。
- `Syntax/Parser.Expressions.cs` 的 `TryInfixBinding` 多收一个 `Postfix`：`t.Type == TokenType.DictArrow && !policy.Arrow` 时不当中缀。
- `Syntax/Parser.Atoms.cs` 的 `ParseDict`：键按 `Postfix.DictKey` 解析，之后 `Consume(DictArrow)`。
- `Syntax/Parser.Lookahead.cs` 的 `ScanBraceBody`：词法器把 `;` 也发成 `Newline`（lexeme 不同），顶层见到它就说明这是**块**不是字典 —— `{1 -> 1;}` 于是走块那条路，`(…) ()` 才拿到那对。

## 档位（为什么是 11）

一开始直接抄了 `===` 的 10，实测暴露了问题：`->` 右结合 ⇒ `Rbp = Lbp - 1 = 9`，而 `:` / `==` 的 `Lbp = 10 > 9`，
于是它们被并进**右边那一格**：

```ravel
print (1 -> 2 : pair)          # 1 -> false   ← 本来想要 true
print (1 -> 2 == (pair 1 2))   # 1 -> false   ← 本来想要 true
```

`===` 放 10 是对的（它属于比较那一整档，还要参与链式比较），`->` 不一样：它造一个值，不是谓词。
放到 11（`Rbp = 10`）后：

| 写法 | 结果 |
|------|------|
| `1 -> 2 : pair` | `(1 -> 2) : pair` → `true` |
| `1 -> 2 == p` | `(1 -> 2) == p` → `true` |
| `1 -> 2 -> 3` | 仍是右结合（右操作数在 `minBp = 10` 上还吃得到 `->`）|
| `1 + 2 -> 3` | `3 -> 3`（`+` 是 14，更紧）|
| `x : int -> 1` | `(x : int) -> 1`（`:` 的 `Rbp = 11`，见到 `Lbp = 11` 就停）|

## 语义

- `a -> b` ≡ `pair a b`：值类型的一对（见 `.scratch/pair/plan.md`），可当字典键、按内容比。
- 右结合：`1 -> 2 -> 3` = `1 -> (2 -> 3)`；左边那格是一对时必须括号：`(1 -> 2) -> 3`。
- 打印：`PairVal.ToString` 对"键本身又是一对"的加括号，两种写法不再同形。
- 覆盖：类里写 `-> := (o) => …` 就接管（与 `===` 一样，只换这一个运算符的语义）。

## 测试

- `tests/class/66_pair.rav`：运算符写法（`1 -> 2`、右结合、左嵌套带括号、`[1 -> 1]`、`{(1 -> 1)}`、`{1 -> 1;}` 块调用、`(1 -> "a") == (1 -> "a")`）、`d.Contains (pair …)` 三条（从 `12_containers.rav` 搬来，那边空间不够）、`Arrowish` 类覆盖 `->`。
- `tests/lang/12_containers.rav`：删掉那三条 `Contains`（腾出 118B，回到 5,106B）。
- `tests/class/64_operator_class.rav`：`Math.Fields ().Count ()` 57 → 58（`Object` 的运算符表多一格，`->` 也算成员）。

## 没做

- 不给 `->` 单独造 token（复用 `DictArrow` 就够，词法层不用动）。
- 不做"`{}` 里靠空格区分"之类的启发式：`{k -> v}` 永远是字典，要一对就套括号或写成分号。
- 不加 `->` 的字面量打印特例（`PairVal.ToString` 已经够用）。
