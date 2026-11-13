# 推导式（comprehension）—— 设计与落地

`myMetaClass.rav` 里那五行需求的实现记录：语法、脱糖、踩过的坑、为什么是链式而不是循环。

## 1. 需求

```ravel
a  := [x for i in ... if ...]      # 列表
b  := (x for i in ...)             # 枚举器
b2 := x for i in ... if ...        # 裸形式，同一个东西
c  := {x for i in ...}             # 集合
d  := {k -> v for i in ... if ...} # 字典
```

## 2. 语法

推导式是**表达式后缀**，不是运算符：绑定力当成最低那档（1，比 `||` 的 2 还松），所以

```ravel
i * 2 + 1 for i in xs      # 值是 i * 2 + 1，不是 i * (2 + 1) 之类
```

容器由外面那对括号决定 —— `[… ]` 列表、`{… }` 集合、`{k -> v …}` 字典、裸写（或 `(…)`）是惰性的 `Generator`。判据只有一条：**第一个值表达式之后紧跟 `for`**，才算推导式；否则 `[x]` 还是老的单元素列表。

`for` / `if` 不是关键字（词法器只发标识符），解析器按上下文词匹配。为此有三处配合：

- `TryPostfixArg` 里把 `for` / `if` 当成"不能吃当实参"的边界 —— 否则 `x for i in xs` 会被读成 `x` 拿 `for` 当实参。
- 包裹语法的第一个值用 `ParseCompHead()` 解（带 `_compSuffixOff` 计数，该期间关掉后缀推导式）—— 否则 `[i for i in xs]` 里的 `i` 会先把后面整串吞成 Generator 推导式，外层再找子句时只剩 `]`。
- `ForAhead()` 从当前位置扫到本条收尾，depth 0 见到 `for` 才算。

## 3. 脱糖

递归的 `Seq(clauses, value)`，一层 `for` 一层 lambda：

| 输入 | 变成 |
|------|------|
| `for n in src` 后面还有子句 | `src \|> .Bind ((n) => { Seq(剩下的) })` |
| `for n in src` 后面紧跟若干 `if` | `src \|> .Where ((n) => { 条件们 && })` |
| `for n in src` 是最后一层 | `src \|> .Map ((n) => { value })`（字典的叶子是 `[k v]`）|
| 收尾 | 列表 `.ToList ()`、集合 `.ToSet ()`、字典 `.ToDict ()`、裸形式原样 |

**`if` 必须贴着它自己的 `for`。** 这是选这个形状的唯一理由：`for i in A if p for j in B` 脱成 `A.Where(i=>p).Bind(i=>…)`，`p` 里的 `i` 还在作用域里；要是写成 `A.Bind(…).Where(j=>p)`，`i` 就出作用域了。

为什么不生成 `while` + 游标：链式版本一行降完、不用造临时名和游标、`ravel ast -s` 里读起来就是用户能写的 Ravel，而且 `Map`/`Where`/`Bind`/`ToList` 已经全部由引擎和 `lib/iterator.rav` 提供（`lib/predefined.rav:77-79` 无条件 `using` 它们）。

## 4. 语义

- 循环变量在 lambda 作用域里，**不外泄**：推导式之后 `print i` 是「未定义的变量 'i'」。
- 第一层的源只求一次；第二层的源按外层每个元素各求一次。
- 列表源是急的（`[…].Map` 直接返回列表）；`Range`/`string` 等走 `IEnumerable` 的源返回 `Generator`，裸形式与 `(…)` 形式不到枚举时不跑。
- 字典的键重复后到盖前者（`ToDict` 里 `out[k] = v`）。
- 不可枚举的源报 `类型 'X' 没有方法 'Map'`。

## 5. 落点

| 文件 | 改了什么 |
|------|----------|
| `Syntax/Ast.cs` | `CompKind`、`CompClause`/`CompFor`/`CompIf`、`Comprehension` |
| `Syntax/Parser.Comprehensions.cs`（新） | `ForAhead`、`ParseCompHead`、`ParseComprehension`、`_compSuffixOff` |
| `Syntax/Parser.Expressions.cs` | `ParseExpression` 末尾的裸形式后缀 |
| `Syntax/Parser.Postfix.cs` | `TryPostfixArg` 认 `for`/`if` 为边界 |
| `Syntax/Parser.Atoms.cs` | `[` / `ParseSet` / `ParseDict` 三处 `ForAhead()` 分支 |
| `Syntax/Lowering.cs` | `Comp` / `CompSeq` / `Lam` |
| `Runtime/AstPrinter.cs` | `Comp` 打印、`Prec = 1` |
| `tests/lang/18_lambdas.rav` | `ravel "m53"` 一节，12 条断言 |
| `docs/tutorial/07-collections.md` | 7.4.2 节 |
| `docs/tutorial/11-cheatsheet.md` | 一行 |

## 6. 踩过的坑

- `dotnet build` 不更新 `out/ravel.dll` —— 必须 `bash build.sh --no-test`，否则跑的是旧二进制，报错完全误导。
- 包裹分支解析完不 `Consume` 收尾符，`]` 会被当成下一轮输入。
- 用 heredoc 拼 C# 转义会把 `\"` 弄坏，编译报 `CS1026`；改源码走 edit/write。
- 测试组容量：`tests/lang` 正好 20 个文件（组上限 20），不能新建文件，只能并进 `18_lambdas.rav`（删了 11 行低价值断言腾位置，终值 4,952B ≤ 5,120）。
- 第一版把"值"那一段的裸后缀关掉之后，**括号里也一起关了**，于是 `[i for i in (j for j in xs)]` 报「表达式后需要 ')'」。括号是显式边界，`ParseGroupedParen` 里把 `_compSuffixOff` 清零再恢复，这条就通；用例 `print ([i for i in (j * 2 for j in [1 2])])` 钉住它。

## 7. 没做

- 没有 `for … in … do` 语句形式（那是另一条糖，`Foreach` 已经够用）。
- 没做"推导式里 `break`"（语义上说不清）。
- 不检查源是否可枚举，直接交给 `Map` 报错 —— 报错文案够清楚，且少一次运行期探针。
