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

1. **语义不变**：168 条 golden 用例全绿（原 169，删掉不稳定的 299 之后）。若某条用例锁的是实现细节（例如链式比较那次
   `310_precedence.rav` 里 `1 < 2 == true` 的旧期望），先拿出来讨论，不静默改。
2. **注释不超过二十字**（用户 2026-11-12 裁定，取代原"注释是资产"那条）：
   任何注释不得超过 20 字，`///` 一律不要。理由型注释也在内——写不下就用短句点名要害，
   写不下又不重要的直接删。实施时按此条清了一遍，删 209 行注释。
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
- 只搬不改。验收：`ast -s` 前后树形相同（临时名允许变），168 条用例绿。

收益是"读代码的心理负担"，不是正确性，所以排最后。

**落地出入**（详见第九节）：`ParseParen` 切成四段而不是三段（多了 `TryParseRange`，区间试读会造
`RangeExpr`，不算纯粹前瞻）；`ParseStatement` 没有变成"Lookahead 判定 → 跳 `ParseXxx`"，而是
"逐个 `TryParseXxx is { } x` 认领 → 否则 `ParseExpressionStatement`"，因为多数形状本来就是
边走边认，硬造判定等于把同一套 token 检查写两遍。前瞻函数确实单独立了
`Syntax/Parser.Lookahead.cs`，但只收入那些**不造节点**的判定。

## 六、P3：把"糖放哪"写成规矩

**已落地**：`docs/adr/0003-where-sugar-lives.md`（Accepted，2026-11-12），核心判据一句：

> 改变树形或结合方式 → parser；只换节点种类 → lowering；只影响分词 → lexer。

附了一张"糖现在住在哪"的表（`~x` 在 Atoms、参数守卫在 `Lowering.PrependGuard`、行首 `.` 在
Lexer、`?.`/`??`/`??=` 在 Lowering、链式比较在 Expressions、下标糖一半 parser 一半 lowering），
一条"拿不准就问：这个糖需要知道上下文才能决定吗"的辅助判据，以及四个例外/模糊地带
（`~x`、参数守卫、下标两半、`f a b` 柯里化）。

## 七、顺序与验收

| 步 | 内容 | 验收 | 状态 |
| --- | --- | --- | --- |
| P0 | 运算符表 | 168/0、`--warn` 无新警告、ast diff 相同 | 已提交 `f2b6e96` |
| P1 | `PostfixPolicy` | 同上 | 已提交 `97a172a` |
| P2 | 拆两个巨函数 | 同上 + ast diff 相同 | 已提交 `39e3f8e`、`45767fb` |
| P3 | 写文档 | — | 已落 `docs/adr/0003-where-sugar-lives.md` |

每步结束：`bash build.sh`（168/0）→ `bash build.sh --warn` → 抽 10 个 `.rav` 做 `ast -s` 差分 →
提交（提交信息里写清"行为不变，纯结构"）。

## 八、不做的事

- 不换解析算法（Pratt 爬升保留），不引入 parser generator。
- ~~不合并 `Parser.cs` / `Atoms` / `Expressions` / `Statements` / `Holes` 的文件划分。~~
  用户 2026-11-12 改口："允许大幅度重构，清晰是第一目标"——可以切文件、改划分，
  仍然不换算法、不引框架。
- 不为了"每节点一个方法"而拆到 60 个 `ParseXxx`。
- 不动 `Lexer`（行首 `.` 那条留在原地，P3 只是登记它）与 `Lowering`（除 P0 里 printer 查表）。
- 不做方案外的"顺便重构"：没写的先不动，除非切文件本身要求。

## 九、实施记录

（以代码为准，下面是落地与设计稿的出入）

**P0 运算符表**（`f2b6e96`）。落地形状与设计稿基本一致，两处出入：

- `OpInfo` 实际字段是 `(string Symbol, TokenType? Token, int Lbp, int Rbp, int PrintPrec, bool RightAssoc)`；
  `Operators` 提供 `TryToken` / `TryWord` / `PrintPrec` / `IsInfixToken` / `IsPowerLike` / `UnaryPrec = 17`。
  加表用 `Add(symbol, lbp, token, right)`：左结合 `rbp = lbp + 1`，右结合 `rbp = lbp - 1`。
- `**` 进表（`right: true`，`lbp 18`），"比一元紧"由 `IsPowerLike` 在 `ParsePower` 那一层表达，
  后门变成通用规则；实测 `-2 ** 2`=-4、`2 ** 3 ** 2`=512、`2 ** -1`=0.5。
- 顺带修了两处**已有的**不一致（不属于本次范围，但都是表统一后暴露的）：printer 里 `and` 的档位
  6 → 8（与 parser 真值对齐，原来 `a and b ^ c` 往返打印不一致）；`Cli/AstDump.cs` 的结构自验
  忽略 `Parenthesized`（纯记号，不是语义）。

**P1 后缀收法**（`97a172a`）。落地时把设计稿的四位记录收成三位，并把 `stopAtPipe` 整个删掉：

- 实际类型 `internal readonly record struct Postfix(bool Args, bool Pipe, bool Block)`，
  静态值 `Full` / `Bare` / `PipeOnly`，变换 `NoPipe` / `WithBlocks`（`Syntax/Postfix.cs`）。
- `Member` 那一位最后**没要**：唯一想要它的场合是 `ParseParen` 里的括号组，而那儿本来就走
  `Postfix.Bare`，加一位没有第二个使用者。
- `guarded`（"已经在一层 `?.` 守卫里"）**没有**并进记录，仍作 `ParseMemberChain` /
  `ParsePostfixRest` 的第二个参数：它描述的是"正在解析哪一层"，不是"许不许"。
- `stopAtPipe` 用 `NoPipe` 取代，且语义更直：命中 `|>` 之前先看 `policy.Pipe`，
  不吃就 `break`，不再有"先 `Match` 再 `_pos--` 还回去"那一步。
- 调用点全部变成 `ParsePostfixRest(e, Postfix.Bare)` 这种形状，`ParsePostfixRest` 只剩两个参数。

**注释清场**（同批次提交）。按新的二十字规矩清了一遍：`Syntax/Parser.Expressions.cs` 删 126 行、
`Cli/AstDump.cs` 49、`Parser.Holes.cs` 18、`Parser.cs` 11、`OperatorTable.cs` 3、`Postfix.cs` 2。

**P2 巨函数拆分**（`39e3f8e` / `45767fb`）。两片互不重叠的文件并行做，各一片一提交：

- `Syntax/Parser.Statements.cs`（968 → 964 行）：`ParseStatement` 只剩八步 dispatch
  （`TryParseDirectiveStatement` / `TryParseBindStatement` / `TryParseOperatorDefinitionStatement` /
  `RejectRetiredStatementStart` / `TryParseModifiedStatement` / `TryParseControlFlowStatement` /
  `TryParseDefinitionOrAssign` / `ParseExpressionStatement`），全文 56 个函数，最长
  `EndsThisPart` 44 行。模式拆成 `ParseListPattern`/`ParseBracePattern`/`ParseRestPattern`/
  `ParseGroupPattern`/`ParseNamePattern`/`ParsePlacePattern`/`ParseGuardedNamePattern`/
  `ParseDictEntry`/`ParseMemberEntry`；定义拆成 `ParseDefinedName`/`ParseDefinitionSign`/
  `RequireAfterTypeAnnotation`/`ParseDefinitionValue`，`property ()` 那种空体收进 `EmptyProperty`。
- `Syntax/Parser.Atoms.cs`（881 → 618 行）：`ParseParen` 只剩读 token 加四处分派
  （`ParseUnitParen` / `ParseLambdaParen` / `ParseGroupedParen` / `TryParseRange`），内部再拆
  `ParseParamList`/`ParseParamItem`/`ParsePatternParam`/`ParseTypedParam`/`ParseUntypedParam`/
  `DeclareParam`/`ParseGuardParam`/`BuildLambda`。
- 新建 `Syntax/Parser.Lookahead.cs`（204 行）：`StartsParamGroup`/`StartsParam`/`StartsBindingAt`/
  `CollectModifiers`/`LooksLikeLambdaParams`/`IsLambdaAfterParen`/`SkipTypeAnnotation`/
  `ScanBraceBody`/`BarePatternLambdaAhead`/`FirstIdentName`/`AnyNewlineBeforeClose`/`HasDotDotAhead`。
  它仍借留在原处的 `TypeAt`/`NextType`/`SkipBalanced`/`IsLiteralStart`，**不是**自洽一块；
  `TryParseRange` 因为会造 `RangeExpr` 留在 Atoms，只把前置判定 `HasDotDotAhead()` 前移。
- 命名坑：`CollectStatementModifiers` 不能叫 `CollectModifiers`——`Parser.Atoms.cs:189` 已有同名
  不同义的那个（返回 `List<string>?`）。

**`ast -s` 自验修复**（`f042b42` / `8dbb70f`）。两个真打印缺陷：同档比较式当操作数不套括号
（`(1 < 2) == true` 印成 `1 < 2 == true`，重读变成链式比较，意思就变了），已按
`Operators.IsCompare` 在 `Runtime/AstPrinter.cs` 的 `Binary` 收紧，`Chain` 的操作数门槛取
`PrintPrec("<") + 1`；比较档真源同时收进 `Syntax/OperatorTable.cs` 的 `IsCompare`，
parser 的 `IsCompareOp` 改成一行委派。自验本身改成比**未脱糖**的两棵树：`Parser.Parse()`
仍是 `Lowering.Apply(ParseTree())`，新增 `ParseTree()` 与 `ParseSourceUnlowered(...)`，
`Cli/AstDump.cs` 用它打印并回转。两个打印固有损失另行处理：含内置占位符（`LiteralExpr`
印成 `<builtin>`）的文件跳过自验并计数；`CallExpr(MemberAccess 运算符, 实参)` 在
`Canonical` 里按二元式渲染，因为 printer 故意把 `a.+ b` 印成 `a + b`。现状：
`tests/lang/310_precedence.rav`、`tests/lang/367_chain_compare.rav`、`lib/sorting.rav` 三份都 exit 0。

**P2 续：后缀链**（`83a77f8`）。`Syntax/Parser.Expressions.cs` 573 → 238 行，`ParsePostfixRest`
那 118 行拆出去，另立 `Syntax/Parser.Postfix.cs`（278 行）：

- 主循环 `ParsePostfixRest` 只剩"试一支，认领就继续"：`TryPostfixPipeInto` / `TryPostfixArg` /
  `TryPostfixDot`，各自返回 `Expression?`，都不认领就 `break`。
- 管道一支再拆 `PipeIntoOperatorAhead` / `ParsePipeIntoOperator` / `ParsePipeIntoCall` /
  `ParsePipeIntoClause`；下标一支 `ParseIndex` / `ParseIndexExpression` / `ParseIndexPostfixes` /
  `RangeInsideParens` / `AdjacentToPrevious`；成员一支 `ParseMemberChain` / `TryPostfixDot` /
  `ParseGuardedDot` / `OpenGuardedChain` / `GuardedChain`。
- `while` / `foreach` 那种"循环体当块实参"的状态收进 `LoopCtx`（`OpenPendingLoop` /
  `LoopBodyIndex`），不再靠 `ref` 布尔在分支间传递。
- 全量用例由 169 变成 **168**：`tests/task/299_task_io.rav` 时绿时红，用户 2026-11-12 裁"都说不稳定
  了就删掉"（`0058cac`）。剩下偶发红的是联网/时间类用例，单跑稳定。

**`ast -s` 仍红四份**（显示功能遗留，与解析器结构无关，登记不修）：`examples/guess_number.rav`
（原 `BinaryExpr` / 新 `LambdaExpr`）、`examples/intro/matrix.rav`（印出来某行以 `.` 起头，重解析缺
表达式）、`examples/patterns.rav`（位置模式印成 `[l[2] l[1]] = …`，重解析报「'=' 的左边得是变量、
成员或下标」）、`lib/test.rav`（`FStringPart` 插值往返后行号不同）。顺手修掉的两处：`AstPrinter`
原本**不认识 `IndexExpr`**（`ast -s` 直接抛「不认识这个节点」，`0684785`）；`HoleExpr` 印成 `_N`、
重解析成标识符，自验按等价算（`lib/random.rav`、`examples/signal-uses.rav` 因此转绿）。

**并行重构的操作经验**：子代理各自 `dotnet build ... -o /tmp/out_x` 时不要传
`-p:BaseIntermediateOutputPath=仓库内目录`——这个全局属性会传播进插件项目，把
`Ravel.Structures/obj_a/` 也编出重复的 `AssemblyInfo`（CS0579）。现在主项目与五个插件项目的
csproj 都排除了 `obj*` 目录，可以并行构建。
