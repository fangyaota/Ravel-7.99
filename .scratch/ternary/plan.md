# 三元 `a if c else b`

## 需求

`cond { a; } { b; }` 在"只挑一个值"的时候太吵，要一个表达式形态：`a if c else b`。
词形照 Python（`?:` 不行 —— `:` 早就是类型判定）。用户 2026-11-13 点头。

## 语法

- 优先级夹在 `:=` 与 `??` 之间：比赋值紧、比 `||`/`??` 松（松到 1，和推导式后缀同档）。
- 右结合：`a if c1 else b if c2 else d` = `a if c1 else (b if c2 else d)`。
- 条件必须是 `bool`（求值走 `BoolVal`/`PartialBool` 那条分派，非 bool 报「值 X 不是函数，不能调用」）。
- 缺 `else` 报「`if` 后面要跟 `else` —— 三元写成 `a if 条件 else b`;只想挑一支跑,就写 `cond { } { }`」。

## 落点

| 文件 | 改动 |
|---|---|
| `Syntax/Ast.cs` | 新节点 `ConditionalExpr(Condition, WhenTrue, WhenFalse)` |
| `Syntax/Parser.Expressions.cs` | 新的一层 `ParseTernary`，插在 `ParseAssignment` 与 `ParseNullCoalesce` 之间 |
| `Syntax/Postfix.cs` | 加 `Ternary` 一格（`Full`/`Bare`/`PipeOnly`/`CompHead` 都开） |
| `Syntax/Parser.Comprehensions.cs` | 子句的"部分"（源 / 滤波条件）用 `CompHead with { Ternary = false }` |
| `Syntax/Parser.Postfix.cs` | 并列实参的闸门加 `IsWord("else")` |
| `Syntax/Lowering.cs` | `Conditional` → `Call(Call(cond, block(a)), block(b))`，与手写 `cond { } { }` 同形 |
| `Runtime/AstPrinter.cs` | `ConditionalExpr` 印回糖，`Prec = 1`；真支/条件要 2 起、假支 1 就够（右结合） |

## 为什么 `Ternary` 要按入口说清

`if` 有两个身份：三元的开头、推导式的滤波子句。子句那格必须关掉三元，否则

```ravel
[x for x in [1 2 3] if (x > 1) if (x < 3)]   # 两个滤波器
```

会被读成 `if (x > 1) if …`（三元缺 `else`）。关法是 `Postfix.Ternary`，和 `Comp` 一样，
**写在每个入口的参数上**，不靠解析器字段继承（`_compSuffixOff` 那次就是这么坏的）。

同一处还有个连带发现：`ParseCompPart`（子句的源与条件）之前是 `ParseCompHead()`，
Comp 关了但 Ternary 开着 —— 于是 `for x in [1 2 3] if …` 里那个 `if` 被当成源的三元。
现在两处都关。

## 语义（都实跑过）

```ravel
x := 5
print (x if (x > 0) else 0)                  # 5
print (1 + 2 if false else 3)                # 3   优先级：整条 `1 + 2` 是真支
print (1 if false else 2 + 3)                # 5   假支吃到 `2 + 3`
print (1 if false else 2 if false else 3)    # 3   右结合
print ((true { 1; } { 2; }) if true else 0)  # 1   分支可以是块值
print ([x if (x > 1) else 0 for x in [1 2 3]])   # [0 2 3]
```

分支是**裸块**：`y := 0; z := (y = 9) if false else (y = 4)` → `z == 4`、`y == 4`。

## 测试

`tests/lang/02_control_flow.rav` 末尾 `ravel "ternary"`（5 条）。为腾地方删了同文件里
三行"别处已覆盖"的断言（`s := {42}` / `typeof s` / `s.Contains 42`，typeof 在 06_types、
集合在 10_collections / 12_containers 都有）。文件 5,076B。

## 没做

- 分支省略（`a if c` 独用当过滤器）—— 会把 `if` 子句和三元的边界彻底搅在一起。
- 行首 `else` 续行 —— 那是留给 `|>` / `.` 的口子，`else` 掺进去就成了半个关键字。
- `ravel ast` 的去糖视图不单独认它（印出来是 `cond ({ a }) ({ b })`，与手写块一致）。
