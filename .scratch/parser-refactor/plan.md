# 解析器重构：把散在调用链里的"知识"收回来

状态：**方案，未实施**
基线：`2a585e5`（链式比较，全量 `169 passed, 0 failed`；`--warn` 也绿）

## 一、要解决什么

现在解析器没有"乱"到读不懂，但它有三类知识散在**调用链**上，每加一条语法都要摸一遍：

1. **优先级有四份真源**：`BinOpBp`（`Syntax/Parser.Expressions.cs:174`，带符号的运算符，`**` 不在表里）、
   `WordOpBp`（同文件 `:191`，`or`/`and`）、`ParsePower`（同文件 `:277`，为 `**` 开的"右结合 + 比一元紧"后门）、
   `BinPrec`（`Runtime/AstPrinter.cs:185`，按字符串再写一遍，而且是另一套编号）。
   加一个运算符要同步四处；漏一处，错误会从"解析错"变成"打印出来的源码重解析后是另一个意思"。
   `**` 之所以开不了表里的口子，是它的"比一元运算符还紧"没法用 `(Lbp, Rbp)` 表达——
   这件事本身说明表的设计维度少了一维。

2. **后缀许可靠布尔参数传递**：`ParsePostfixRest`（`Parser.Expressions.cs:325`，200 行）在 6 个参数里决定
   "后面还能挂什么"；`allowCall` 在 `Parser.Expressions.cs` 出现 47 处、`Atoms` 9 处、`Statements` 6 处。
   调用点写 `ParsePostfixRest(e, false, true, false, false)`，读的人只能去数参数位置。更糟的是
   `allowCall: false` 的**实际**语义是"别吃并列实参，但 `|>` 照收"——一个布尔承载了两件事。

3. **两个巨函数在靠 lookahead 猜**：`ParseParen`（`Parser.Atoms.cs:320`，205 行）同时管
   lambda 参数表 / `()` 字面量 / 普通括号 / 块实参；`ParseStatement`（`Parser.Statements.cs:137`，240 行）
   要先猜"这是定义、解构、模式还是表达式"。猜的依据散在
   `LooksLikeLambdaParams`、`IsLambdaAfterParen`、`BarePatternLambdaAhead`、`ScanBraceBody`、
   `LooksLikeDictEntry`、`LooksLikePatternDestructure` 里，每多一种语法就多一个向前扫描。

另一笔隐性债：**语法糖放哪没定规矩**。`~x` 在 parser 造块、参数守卫在 `PrependGuard` 包 lambda、
行首 `.` 在 **Lexer** 改 token 流、`?.`/`??` 在 **Lowering** 换节点、链式比较在 parser 改结合。
这次加链式比较时我才被逼出一条判据（见 P3），它现在只活在注释里。

目标不是"教科书式的解析器"，是：**加一条语法只改一处；读一个调用点不用数参数。**

## 二、四条硬约束

1. **语义不变**：169 条 golden 用例全绿。若某条用例锁的是实现细节（例如链式比较那次
   `310_precedence.rav` 里 `1 < 2 == true` 的旧期望），先拿出来讨论，不静默改。
2. **注释是资产**：每处"为什么必须这么写"（`**` 的后门、`allowCall:false` 的两层含义、
   `ScanBraceBody` 为什么要两路扫）在搬动时**跟着代码走**。禁止"整理时顺手删注释"。
   上一轮删 `///` 5986 行是因为它们只是复述签名，不是理由；理由型注释一条都不许少。
3. **不加层数、不加依赖**：最多引入"一张表 + 一个记录"，不引入 visitor / parser generator / DI。
4. **一步一提交**：P0、P1 各自独立可回滚；每步结束跑 `bash build.sh` 与 `bash build.sh --warn`。

## 三、P0：运算符表（`Syntax/OperatorTable.cs`）

一张表描述一个运算符的全部已知属性，parser 与 printer 都读它：

```csharp
public sealed record OpInfo(
    string Symbol,
    TokenType? Token,   // null = 词形运算符（or / and）
    int Lbp, int Rbp,
    int PrintPrec,      // 打印时决定加不加括号的那一档
    bool RightAssoc);

public static class Operators
{
    public static bool TryToken(TokenType t, out OpInfo op);
    public static bool TryWord(string lexeme, out OpInfo op);
    public static OpInfo Of(string symbol);   // 打印器用
}
```

关键设计点：

- **`**` 进表**，用 `RightAssoc = true, PrintPrec = 18` 表达"比一元紧"。但"比一元紧"这件事
  `(Lbp, Rbp)` 表达不了，所以 `ParsePower` 这一层**保留**，改成通用规则：
  查表 → 若 `op.RightAssoc && op.PrintPrec > UnaryPrec` 就按右递归解析。
  后门变成规则，将来加 `//`（整除）之类不必再开第二个后门。
- `or`/`and` 进同一张表（`Token = null`），`IsInfixWordOperator` 改成查表。
- `AstPrinter.BinPrec` 删除，`Binary`/`Chain`/`Paren` 统一查 `PrintPrec`。
  `ChainCompare` 的打印档位就是 `Of("<").PrintPrec`，不再有字面量 10。
- `&&`(8,9)、`and`(8,9)、`||`(2,3) 这类"同档不同层"的行为全部由表里的 lbp/rbp 承担，
  逐条用现有用例兜。

必须实测（表改完先跑这三条，再跑全量）：
- `-2 ** 2` = -4、`2 ** 3 ** 2` = 512、`-2 ** 2 ** 2`；
- `1 < 2 == true` 仍是链式（`false`），`(1 < 2) == true` 仍是老读法（`true`）；
- `print or`（词形运算符被当值）仍报"是中缀运算符，后面得跟个操作数"。
- `ravel ast -s` 对 10 个样本前后 diff 逐字相同（P0 不该改变任何树形）。

预期 diff：约 +180 / −120，一个提交。

## 四、P1：后缀许可从一个记录读（`PostfixPolicy`）

```csharp
// "这一次，表达式后面还能挂什么"
public readonly record struct PostfixPolicy(bool Juxtaposition, bool Pipe, bool Block, bool Member)
{
    public static PostfixPolicy Full  => new(true, true, true, true);
    public static PostfixPolicy Atom  => new(false, false, false, false);
}
```

- `allowCall` 拆成 `Juxtaposition`（吃并列实参）与 `Pipe`（吃 `|>`）两位——
  现在 `allowCall:false` 同时表达这两件事，是 `|>` 节和"运算符右操作数"两处特例逼出来的。
- `allowBlock` → `Block`；`Member`（`.xxx`）单独一位，因为 `ParseParen` 里有"括号里不许再取成员"的场合。
- **`stopAtPipe` / `guarded` 不并进记录**：它们描述的是"层级边界/正在解析守卫"，
  不是"许可"。改成 parser 内部一个小的层栈（`PipeFrame`/`GuardFrame` 计数），
  或作为 `Parse` 的出口条件，别在签名的第 6 个位置传递。
- 验收标准写死：`ParsePostfixRest` 签名参数 ≤ 2；调用点形如
  `ParsePostfixRest(e, PostfixPolicy.Atom)`，读得出一句话。

预期 diff：约 +90 / −90，一个提交。

## 五、P2：巨函数拆分（可缓，独立提交）

- `ParseParen` 205 行 → `ParseParenLambdaParams` / `ParseParenExpr` / `ParseParenBlockArgs`，
  外层留 20 行分派；分派条件与今天的顺序**逐条一致**（先 lambda 参数、再 `()`、再表达式）。
- `ParseStatement` 240 行 → 判定收进 `Lookahead` 类（`LooksLikeDefinition` /
  `LooksLikePatternDestructure` / `LooksLikeBarePatternLambda` / `ScanBraceBody` …），
  `ParseStatement` 变成"查一次判定 → 跳对应 `ParseXxx`"。
- 只搬不改。验收：`ast -s` 前后树形相同（临时名允许变），169 条用例绿。

收益是"读代码的心理负担"，不是正确性，所以排最后。

## 六、P3：把"糖放哪"写成规矩

仓库加一份短文档（`docs/parser.md` 或 `CONTRIBUTING` 一节）：

> 改变树形或结合方式 → parser；只换节点种类 → lowering；只影响分词 → lexer。

附三个正例：链式比较（改结合，parser）、`?.`/`??`（换节点，lowering）、行首 `.`（改 token 流，lexer）；
再附一条反例：`~x` 现在在 parser 里造块，属于"改树形"，可以留，但要在文档里点名它是例外还是规律。

## 七、顺序与验收

| 步 | 内容 | 验收 | 预计 |
| --- | --- | --- | --- |
| P0 | 运算符表 | 169/0、`--warn` 无新警告、ast diff 相同 | 半天 |
| P1 | `PostfixPolicy` | 同上 | 半天 |
| P2 | 拆两个巨函数 | 同上 + ast diff 相同 | 一天，可缓 |
| P3 | 写文档 | — | 顺手 |

每步结束：`bash build.sh`（169/0）→ `bash build.sh --warn` → 抽 10 个 `.rav` 做 `ast -s` 差分 →
提交（提交信息里写清"行为不变，纯结构"）。

## 八、不做的事

- 不换解析算法（Pratt 爬升保留），不引入 parser generator。
- 不合并 `Parser.cs` / `Atoms` / `Expressions` / `Statements` / `Holes` 的文件划分。
- 不为了"每节点一个方法"而拆到 60 个 `ParseXxx`。
- 不动 `Lexer`（行首 `.` 那条留在原地，P3 只是登记它）与 `Lowering`（除 P0 里 printer 查表）。
- 不做 P2 之外的"顺便重构"：这个方案里没写的一律不动。

## 九、实施记录

（实施后补：实际落地与设计稿的出入，**以代码为准**）
