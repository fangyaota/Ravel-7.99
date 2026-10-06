namespace Ravel.Runtime;

using System.Text;

/// <summary>把 AST 渲染回 Ravel 源码。同一条渲染路,两个入口:
///
/// * <see cref="Signature"/> —— **显示**用(`print` 一个函数、报错文案里那句"是哪个函数")。
///   **一条语句一行都铺不开**:它要塞进一句提示里,所以 `Fit` 按 <see cref="MaxLength"/>
///   截断、块全挤在一行(见 <see cref="Block"/>)。
/// * <see cref="Program"/> —— **整份源码**(`ravel ast --source`)。**一个字都不截**,
///   块**按层展开缩进**,出来的是能再喂回解析器的 `.rav`。
///
/// 这两档靠一个 `indent` 参数分:
/// * `indent < 0` —— 单行档(显示走这条):块写成一行的 `{ a := 1; f a; }`。
/// * `indent >= 0` —— 展开档(整份源码走这条):块写成
///   `{\n` + 每条语句缩进一层 + `\n` + `}`,**收尾那个 `}` 缩进到 `indent` 那一格**
///   (所以它跟开头那一行对齐)。传下去的是 `indent + 1`(语句在块里就深一层)。
///
/// 括号按**保守**规则加:实参 / 列表元素那一格只吃"主表达式 + 取成员",别的形状套一层;
/// 二元运算按优先级判(见 <see cref="BinPrec"/>)。**多了只是难看,少了就是另一个意思** ——
/// 所以拿不准的一律套。
///
/// 两样东西回不来,别指望:
/// * **注释** —— AST 里根本没有(要看原文只能读源文件);
/// * **用户写下的那个形状** —— 印的是 `Lowering` **之后**的树。`a ?? b` 印出来是
///   `NilOr (() => { a; }) (() => { b; })`,模式参数印出来是 `(__p0: object) => { … }`
///   加一串判 `__p0 : IEnumerable` 的语句。要"原样"得先有没过 Lowering 的树(没有这条路)。</summary>
internal static class AstPrinter
{
    private const int MaxLength = 72;

    /// <summary>缩进一格几个空格。</summary>
    private const int Step = 4;

    /// <summary>单行块写到多长就不留一行、改成展开。**这是个手感数,不是规矩** ——
    /// 小了(比如 40)`if { …; } { …; }` 这种到处都在的写法会被撑开;大了(比如 120)
    /// 长行又回来了。挑 60 是因为 ravel 自己的库正文基本都在这条线以内。</summary>
    private const int InlineMax = 60;

    /// <summary>渲染一个函数的**签名链**:`(a: int) => { … }`。**单行档** —— 显示用。</summary>
    public static string Signature(LambdaVal lam)
    {
        var parts = new List<string> { Param(lam.ParamName, lam.ParamTypeExpr, lam.ParamPattern, -1, lam.ParamAttrs) };
        var body = lam.Block;
        while (Next(body) is { } inner)
        {
            parts.Add(Param(inner.Param.Name, inner.Param.Type, inner.Param.Pattern, -1, inner.Param.Attrs));
            body = inner.Body;
        }

        // **只有一格、而那一格是"要 `()`"** —— 整个签名就印成 `()`。
        // 外面那层括号是**参数表自己的**,里面那对才是模式 —— 一层就够,别印成 `(())`
        // (那正是"空参数表"那个读法,也是 `() => …` 写出来时的样子)。
        if (parts is ["()"]) return "() => " + Block(body);

        return "(" + string.Join(" ", parts) + ") => " + Block(body);
    }

    /// <summary>整份程序 —— **不截断、块按层展开**(每条顶层语句一行,块里的语句各占一行
    /// 并缩进一层)。出来的是能再解析回去的源码,`ravel ast --source` 用它。</summary>
    public static string Program(Program p)
        => Join("\n", p.Statements.Select(s => Statement(s, 0)));

    /// <summary>体里只跟着一个 lambda 表达式的话,把那个 lambda 返回出来(多参数 lambda 的消糖形状)。
    /// 多一条语句就不是了 —— 那是"函数体里顺手定义了个函数",不是一条参数链。</summary>
    private static LambdaExpr? Next(BlockExpr body)
        => body.Statements is [ExpressionStatement { Expr: LambdaExpr inner }] ? inner : null;

    /// <summary>参数。**显示**那条路(`indent &lt; 0`)上,带模式的那几个印**模式** ——
    /// 印的是人写的那个样子(`() => …` / `1 => …` / `([x y]) => …`),而不是脱糖之后
    /// 那一串 `__p…` 加体里的拒收检查(那些检查**正是**这个模式的意思,展开只是噪音)。
    ///
    /// **整份源码**那条路(`indent &gt;= 0`)一律印**脱糖之后**的样子:那一路的契约是
    /// "再解析回来得是同一棵树",而树里参数就是 `__p…` 加体开头那串绑定 —— 把模式也印出来
    /// 等于同一件事说两遍,重解析会**又绑一遍**(实测:77 个文件对不上)。</summary>
    private static string Param(string name, Expression type, Pattern? pattern, int indent,
                                List<string>? attrs = null)
        => Attrs(attrs)
         + (pattern is null || indent >= 0 ? name + ": " + Annotated(type, indent)
          : IsUnitPattern(pattern) ? "()"                    // 见下:两种写法一个构造,挑短的印
          : Pattern(pattern, indent));

    /// <summary>是不是"要单位那个值"那一格。**`()` 和 `(())` 解析出来是同一个构造**
    /// (前者是空参数表那条路、后者是参数表里装一个括号分组,现在两条路合流了),
    /// 所以只能挑一种印法 —— 挑 `()`,它短,而且是历史上那个写法。</summary>
    private static bool IsUnitPattern(Pattern p) => p switch
    {
        LiteralPattern { Value: VoidLiteral } => true,
        WholePattern w => IsUnitPattern(w.Inner),
        _ => false,
    };

    private static string Param(Parameter p, int indent)
        => Param(p.Name, p.Type, p.Pattern, indent, p.Attrs);

    /// <summary>把一个模式印成**人写的样子** —— 给报错用:模式参数取的是**合成名**
    /// (`__p3f9c2_0`),直接印给用户看等于什么都没说;印模式本人(`1` / `([x y])` / `()`)
    /// 才说得出"是哪个参数"。和显示那条路一个写法(见 <see cref="Param(string, Expression, Pattern?, int)"/>)。</summary>
    public static string Describe(Pattern p) => IsUnitPattern(p) ? "()" : Pattern(p, -1);

    /// <summary>渲染一个块 —— **单行档**,带截断。给显示用(`print` / 报错文案)。</summary>
    public static string Block(BlockExpr b) => Fit(BlockInner(b, -1));

    /// <summary>块的真身。`indent < 0` 挤成一行,否则**按层展开** —— 见类文档里那两档。
    ///
    /// 展开档里,**装得下的小块照旧写一行**([`InlineMax`] 以内):`if` / `while` 是**库函数**,
    /// 所以 `if { 条件; } { A; } { B; }` 在树上是"一次调用三个块",一格格摊开能把三行的东西
    /// 撑成十几行 —— 比原文还难读。判据就是"把它单行写出来有多长",长度是**递归算出来的**
    /// (里面的块也先按单行试),所以一个短块里套着长块时,外层自然跟着展开。
    ///
    /// 每条语句都带分号:单行块在 Ravel 里本来就要写 `;`(见 `Parser.ParseMandatoryBlock`),
    /// 展开档照写也不吃亏(分号本来就是语句分隔符)。</summary>
    private static string BlockInner(BlockExpr b, int indent)
    {
        if (b.Statements is []) return "{ }";

        var inline = "{ " + Join("; ", b.Statements.Select(s => Statement(s, -1))) + "; }";
        // `indent < 0` 是显示那条路,**一律**单行(它自己带截断)
        if (indent < 0) return inline;

        // 展开那条路要短才留一行 —— 而且单行写出来的东西里不该有换行(有就说明里头展开了)。
        // **量归量、印归印**:上面那句是按**单行档**量的(那是它最短的写法),真印出来得用
        // **当前这一档**再走一遍 —— 两档印出来的东西**不完全一样**(模式参数:显示档是 `()`、
        // 源码档是 `__p…: void`),直接复用那个量的结果会把显示档的样子漏进源码里。
        if (inline.Length <= InlineMax && !inline.Contains('\n'))
            return "{ " + Join("; ", b.Statements.Select(s => Statement(s, indent))) + "; }";

        var lines = b.Statements.Select(s => Pad(indent + 1) + Statement(s, indent + 1) + ";");
        return "{\n" + Join("\n", lines) + "\n" + Pad(indent) + "}";
    }

    /// <summary>印不出来的一格 —— **当场响,不印占位符**。
    ///
    /// 这三种节点(`Statement` / `Expression` / `Pattern`)都是**封闭层级**:新增一个就得
    /// 回来给这三张表各加一支。从前那支是 `_ => "?"` —— 新节点印成 `?`,`--source` 出来的
    /// 源码读回去当然对不上(`ast` 自检会红),可**报错消息里的 `describe` 也会印成 `?`**,
    /// 那就成了一句看不懂的话。占位符把"打印机不认识这个节点"这条信息**吞掉了**,
    /// 而这正是这门语言最恨的那种静默。
    ///
    /// 抛的是 C# 异常(不是 `RuntimeException`):这是**打印机自己的 bug**,不是脚本的错 ——
    /// CLI 那边兜到它,报的是「解释器内部错误」带类型名,一眼看得出该补哪一支。</summary>
    private static Exception Unknown(object node)
        => new InvalidOperationException($"AstPrinter 不认识 {node.GetType().Name} 这个节点");

    private static string Pad(int indent) => new(' ', indent * Step);

    /// <summary>定义前面那圈修饰符(`readonly` / `private` …)。**从前整个丢掉了** ——
    /// `readonly` 是有语义的(赋值会报错),印丢了就不叫渲染回源码了。</summary>
    private static string Attrs(List<string>? attrs)
        => attrs is { Count: > 0 } ? Join(" ", attrs) + " " : "";

    private static string Statement(Statement s, int indent) => s switch
    {
        // 定义有三种收尾,别混:**注解那条配 `=`**(`x: int = v` —— 解析器
        // `Consume(Equal, "类型注解后需要 '='")` 只认这个,写成 `:=` 是语法错)、
        // `::=` 是自动命名、其余才是 `:=`。
        VarDefinition v =>
            Attrs(v.Attrs)
            + v.Name
            + (v.TypeAnnotation != null ? ": " + Annotated(v.TypeAnnotation, indent) + " = "
                                        : v.Named ? " ::= " : " := ")
            + Expr(v.Value, indent),
        Assignment a => a.Name + " = " + Expr(a.Value, indent),
        SlotAssign sa => "by " + Expr(sa.Path, indent) + (sa.Define ? " := " : " = ") + Expr(sa.Value, indent),
        ExpressionStatement es => Expr(es.Expr, indent),

        // 下面这三种**活不过 `Lowering`**(见 `Syntax/Lowering.cs` 抬头),正常到不了这儿。
        // 留着只是"万一" —— 真到了得**响**,不能印个占位符糊过去(见 `Unknown`)。
        BindStatement bd => bd.Name + " :< " + Expr(bd.Monad, indent),
        Destructure d => Attrs(d.Attrs) + Pattern(d.Pattern, indent) + " = " + Expr(d.Value, indent),
        _ => throw Unknown(s),
    };

    private static string Expr(Expression e, int indent) => e switch
    {
        NumberLiteral n => n.Lexeme,
        StringLiteral s => StringLiteralText(s.Value),
        CharLiteral c => "'" + EscapeChar(c.Value) + "'",
        IdentifierExpr i => i.Name,
        VoidLiteral => "()",
        HoleExpr h => "_" + h.Index,
        ListLiteral l => "[" + Join(" ", l.Elements.Select(x => Arg(x, indent))) + "]",
        SetLiteral s => "{" + Join(" ", s.Elements.Select(x => Arg(x, indent))) + "}",
        // 字典的分隔符是 `->`,不是 `:` —— `:` 早就是"类型判定"了(见 CONTEXT 的语法那一节)。
        // 键和值都走 `Arg`:它们后面紧跟着 `->` 或 `}`,而**调用会往右边吃** ——
        // `{"level" -> (System.EnvOr "RAVEL_LOG" "info")}` 不套括号的话,那个 `"RAVEL_LOG"`
        // 会被当成**下一个键**,回读报「字典键后需要 '->'」(实测踩过)。
        DictLiteral d => "{" + Join(" ", d.Entries.Select(x =>
            Arg(x.Key, indent) + " -> " + Arg(x.Value, indent))) + "}",
        // 省掉的那头打印成空的(`[1..]` / `[..1]`)—— AST 里存的就是 `null`,原样交回去
        RangeExpr r => (r.StartClosed ? "[" : "(")
                       + (r.Lo is null ? "" : Expr(r.Lo, indent)) + ".."
                       + (r.Hi is null ? "" : Expr(r.Hi, indent))
                       + (r.EndClosed ? "]" : ")"),
        // 取成员的**接收者**若本身是一次调用,必须套括号:`f a.b` 读起来是 `f (a.b)`
        // (实参位置只吃"主表达式 + 取成员"),而这里要说的是 `(f a).b` —— do 块脱糖出来的
        // `m.Bind (…)` 全是这个形状,不套括号打印出来是另一个意思。
        MemberAccess m => (m.Object is CallExpr ? "(" + Expr(m.Object, indent) + ")" : Atom(m.Object, indent))
                          + "." + m.Member,
        // 多参调用是"一层层收一个"(`f a b` ≡ `(f a) b`),所以函数那一侧认 CallExpr、
        // 实参那一侧**不认**(见 `Arg`)。
        CallExpr c => Atom(c.Function, indent) + " " + Arg(c.Argument, indent),
        BinaryExpr b => Binary(b, indent),
        // 一元运算符的操作数**至少要跟它一样紧**(18 = `**` 那一档)才不用套括号。
        // 这里从前写的是 `Atom`,而 `Atom` **认调用** —— 于是 `!(x.IsEmpty ())` 印成
        // `!x.IsEmpty ()`,读回去 `!` 把那串全吃了(实测:`!a.IsEmpty () || !b.IsEmpty ()`
        // 和 `!(a.IsEmpty ()) || !(b.IsEmpty ())` 是**两棵树**)。前缀运算符后面那串
        // 和实参一样会往右边吃,所以调用在这儿也得套括号。
        UnaryExpr u => u.Op + Paren(u.Operand, 18, indent),
        PipeExpr p => Paren(p.Left, 1, indent) + " <| " + Paren(p.Right, 2, indent),
        LambdaExpr lam => "(" + Param(lam.Param, indent) + ") => " + BlockInner(lam.Body, indent),
        BlockExpr b => BlockInner(b, indent),
        SlotExpr sl => "by " + Expr(sl.Path, indent),
        // 内置类的预设类体里是 C# 造好的值,没有源码可还原
        LiteralExpr => "<builtin>",
        // 同上,`do` 块也活不过 `Lowering`(折成一串 `.Bind`)—— 留着是"万一"。
        DoExpr d => "do " + BlockInner(new BlockExpr(d.Statements), indent),
        _ => throw Unknown(e),
    };

    /// <summary>二元运算:两边的子表达式按优先级决定要不要套括号。
    ///
    /// 表跟着 `Syntax/Parser.Expressions.cs` 的 `BinOpBp` 走(Lbp 那一列;那边按 `TokenType`,
    /// 这边按运算符的**字面**)。**认不得的一律当最低**(于是套括号)—— 保守那一头永远安全,
    /// 无非多两个括号;反过来说"`a + b * c` 其实是 `(a + b) * c`",那就印错了。
    ///
    /// 赋值那一类(`=` / `:=` / `+=` …)**不在表里**:它们最低,落在"认不得"那一档,
    /// 于是当操作数时一律带括号 —— 正合意。</summary>
    private static readonly Dictionary<string, int> BinPrec = new()
    {
        ["||"] = 2,
        ["|"] = 4,
        ["and"] = 6, ["^"] = 6,
        ["&&"] = 8,
        [":"] = 10, ["=="] = 10, ["!="] = 10, ["<"] = 10, [">"] = 10,
        ["<="] = 10, [">="] = 10, ["<:"] = 10, [":>"] = 10,
        ["<<"] = 12, [">>"] = 12, ["<<<"] = 12, [">>>"] = 12,
        ["+"] = 14, ["-"] = 14,
        ["*"] = 16, ["/"] = 16, ["%"] = 16,
        // `**` 不在 `BinOpBp` 里 —— 它有两条别人没有的性质:比一元负号还紧、而且**右结合**
        // (`-2 ** 2` 是 `-(2**2)` = -4;`2 ** 3 ** 2` 是 `2**(3**2)` = 512)。所以表里给它最高,
        // 结合性在下面单独判。顺手也把它从"认不得"那一档(会被当成最低)里捞了出来。
        ["**"] = 18,
    };

    private static string Binary(BinaryExpr b, int indent)
    {
        var p = BinPrec.GetValueOrDefault(b.Op, 0);
        // 操作数给多紧才不用套括号 —— 看**结合性**:
        // 左结合的(`a - b - c` = `(a-b)-c`)左边给 `p`(同级不套)、右边给 `p + 1`
        // (`a - (b - c)` 同级,要套);右结合的(`**`)正好倒过来。
        var right = b.Op == "**";
        var left = Paren(b.Left, right ? p + 1 : p, indent);
        var rightOp = Paren(b.Right, right ? p : p + 1, indent);
        return left + " " + b.Op + " " + rightOp;
    }

    /// <summary>这一格至少要有多紧,不够紧就套一层括号。</summary>
    private static string Paren(Expression e, int min, int indent)
        => Prec(e) < min ? "(" + Expr(e, indent) + ")" : Expr(e, indent);

    /// <summary>有多紧 —— 只用来判断"当运算符的操作数时要套括号吗"。
    ///
    /// **100 是"自身定界"**(字面量、列表、取成员……自己就站得住的);
    ///
    /// **-1 那一档是"右边有什么它都会吃进去"的**(比任何运算符都紧不了,一律套括号):
    /// * `CallExpr` —— **实参吃到运算符为止**是这门语言的规矩(`f (1) + 2` 就是 `f ((1) + 2)`,
    ///   见 CONTEXT 的实参结合规则)。所以 `"a" + string srv.Port + "/"` 读回来是
    ///   `"a" + string (srv.Port + "/")` —— 调用搁在运算符的操作数位置,不套括号就是另一个意思。
    ///   它在**左边**更要命:`f a op b` 里那个 `op b` 会被 `f` 的实参吃走。
    /// * lambda / 块 / `by …` / `do` / `<|` —— `1 + (x) => { … }`、`a <| b + c` 这种写法
    ///   本身就靠不住。
    ///
    /// **非得是 -1 不能是 0**:认不得的运算符也按 0 算(见 `Binary`),而"左边的紧度 ≥ 运算符"
    /// 就不套括号 —— 两边都取 0 的话,**恰恰在认不得的运算符上判不出该套**,于是
    /// `(f a) op b` 印成 `f a op b`。低一档就永远判得出来。
    ///
    /// 认不得的二元给 0。"</summary>
    private static int Prec(Expression e) => e switch
    {
        BinaryExpr b => BinPrec.GetValueOrDefault(b.Op, 0),
        // 一元比 `**` 松、比其余运算紧 —— 所以 `(-2) ** 2` 那个左操作数得套括号
        // (不套读回去是 `-(2 ** 2)`,另一个数),而 `a + -b` 不用。
        UnaryExpr => 17,
        IdentifierExpr i when IsWordOperator(i) => -1,
        CallExpr or LambdaExpr or BlockExpr or SlotExpr or DoExpr or PipeExpr => -1,
        _ => 100,
    };

    /// <summary>`or` / `and` 这两个名字**当普通名字用的时候**(`or := 42` 是合法的 ——
    /// 它们只在**中缀位置**才是运算符,见 `Parser` 那一节)。
    ///
    /// 麻烦就在"中缀位置"是**读出来才算**的:操作数 / 实参 / 字典值这些地方后面跟着的
    /// 正好是中缀位置,于是 `print (or)` 印成 `print or` 就读不回来了 ——
    /// 报「'or' 是中缀运算符，后面得跟个操作数」。所以这几处一律套括号。</summary>
    private static bool IsWordOperator(Expression e) => e is IdentifierExpr { Name: "or" or "and" };

    /// <summary>**函数那一侧** / 取成员的接收者:`f` 或 `a.b`。调用也认 ——
    /// `(f a) b` 写成 `f a b` 是一个意思(柯里化:`f a b` ≡ `(f a) b`)。</summary>
    private static string Atom(Expression e, int indent) => e switch
    {
        IdentifierExpr i when IsWordOperator(i) => "(" + Expr(e, indent) + ")",
        NumberLiteral or StringLiteral or CharLiteral or IdentifierExpr or VoidLiteral or HoleExpr
            or RangeExpr or ListLiteral or SetLiteral or DictLiteral or MemberAccess or CallExpr
            or LiteralExpr => Expr(e, indent),
        _ => "(" + Expr(e, indent) + ")",
    };

    /// <summary>**实参那一格**(调用右边 / 列表与集合的元素)—— 只吃"主表达式 + 取成员",
    /// 和 <see cref="Atom"/> 的分别就在 **`CallExpr` 认不认**:
    /// `f (a b)` 和 `f a b` 是两个东西(后者是 `(f a) b`),这里要说的是前者,
    /// 所以调用落进"套括号"那一支。少了这条,`f (a b)` 会印成 `f a b` —— 意思就变了。</summary>
    private static string Arg(Expression e, int indent) => e switch
    {
        IdentifierExpr i when IsWordOperator(i) => "(" + Expr(e, indent) + ")",
        NumberLiteral or StringLiteral or CharLiteral or IdentifierExpr or VoidLiteral or HoleExpr
            or RangeExpr or ListLiteral or SetLiteral or DictLiteral or MemberAccess
            or LiteralExpr => Expr(e, indent),
        _ => "(" + Expr(e, indent) + ")",
    };

    /// <summary>注解那一格(`x: 类型` / `(参数: 类型)`)。
    ///
    /// **只有"一个词"形状的才裸着写**,别的统统套括号 —— 这一格后面紧跟着的是 `=`
    /// (定义)或者 `,` / `)` / 函数体,而**调用和运算符都会往右边吃**:`a: pick true = 5`
    /// 读回去那个 `= 5` 会被 `pick` 的实参吃走,`z: 1 + 1 = v` 也会串(源码里那两处本来
    /// 就写着括号:`a: (pick true) = 5`)。
    ///
    /// 判据用 100(="自身定界"那一档,见 `Prec`):`x: int` / `x: list` / `x: a.b` 照旧
    /// 裸着,`x: (pick true)` / `x: (1 + 1)` 套上。</summary>
    private static string Annotated(Expression type, int indent) => Paren(type, 100, indent);

    /// <summary>一格模式。**模式活不过 `Lowering`**(`Destructure` 被拆成一串 `:=`、
    /// 模式参数被拆成 `__p{n}` + 体开头的绑定),所以这一支正常到不了 —— 照源码形状印个大概,
    /// 为的是别掉进看不懂的兜底。</summary>
    private static string Pattern(Pattern p, int indent)
    {
        // **这一格自己那份修饰符**(`[private x]` / `{"k" -> readonly v}`)印在最前 ——
        // 和 `Statement` 里 `VarDefinition` / `Destructure` 一个样子。外层那份(参数级的
        // `Parameter.Attrs`、语句级的 `Destructure.Attrs`)不在 `p` 上、由调用方印,不重不漏。
        var text = Attrs(p.Attrs) + (p switch
        {
            // 带条件的格子:名字取的是条件里最左那个标识符,所以**条件本身就是那个写法**。
            NamePattern { Guard: { } g } => Expr(g, indent),
            NamePattern n => n.Member is { } m ? n.Name + " = " + m : n.Name,
            SkipPattern => "_",
            RestPattern r => ".." + r.Name,
            // 三对括号各有其位:按位置下去、按成员下去、**不下去**(对象自己)。
            WholePattern w => "(" + Pattern(w.Inner, indent) + ")",
            ListPattern l => "[" + Join(" ", l.Parts.Select(x => Pattern(x, indent))) + "]",
            MemberPattern mp => "{" + Join(" ", mp.Names.Select(x => Pattern(x, indent))) + "}",
            LiteralPattern lit => lit.Text,
            DictPattern d => "{" + Join(" ", d.Entries.Select(x => Expr(x.Key, indent) + " -> " + Pattern(x.Sub, indent))) + "}",
            _ => throw Unknown(p),
        });
        // 这一格自己的类型要求(`[a b] : list` 那个 `:` 挂最外那格)。**照写** ——
        // 就算它被提上参数签名去了(见 `Lowering.OuterWant`),这儿还是把它印出来:
        // 那才是人写的样子。字面量那格没有 `Type`(它的要求是 `Domain`),所以 `1` 印出来还是 `1`。
        if (p.Type is { } type) text += ": " + Annotated(type, indent);
        if (p is NamePattern { Sub: { } sub }) text += " = " + Pattern(sub, indent);
        if (p.When is { } when) text += " |> " + Expr(when, indent);
        return text;
    }

    /// <summary>字符串字面量的源码形状(`"…"`,带转义)。
    ///
    /// 除了逐字符那几张表,还有一样**非转不可**的:**`$` 紧挨着 `{`** —— 那是这门语言的
    /// 插值记号。值里带着这两个字符(写法是 `"\${name}"`)而印出来不转,读回去就**变成一段
    /// 字符串拼接**了 —— 一个字符串变成一棵树,不是"难看一点"那么轻。
    /// 只在 `$` 后面真是 `{` 时转,所以 `"价格 $5"` 这种照旧原样。</summary>
    private static string StringLiteralText(string value)
    {
        var sb = new StringBuilder("\"");
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '$' && i + 1 < value.Length && value[i + 1] == '{') sb.Append("\\$");
            else sb.Append(EscapeString(value[i]));
        }
        return sb.Append('"').ToString();
    }

    /// <summary>字符串字面量(`"…"`)里要转的:**`\` 和 `"`**,外加控制字符。
    ///
    /// **单引号一个字都不能动。** `"\'"` 里的 `\'` 在双引号串里**不是转义** —— 读回来是
    /// `\` 和 `'` 两个字符,于是印一遍、解析一遍,字符串就长出一条反斜杠(实测踩过:
    /// `'a' 这一格……` 转一圈变成 `\'a\' 这一格……`)。</summary>
    private static string EscapeString(char c) => c switch
    {
        '\\' => "\\\\",
        '"' => "\\\"",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        _ => c.ToString(),
    };

    /// <summary>字符字面量(`'…'`)里要转的 —— 和上面正好换一个:这儿该转的是 **`'`**。</summary>
    private static string EscapeChar(char c) => c switch
    {
        '\\' => "\\\\",
        '\'' => "\\'",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        _ => c.ToString(),
    };

    private static string Join(string sep, IEnumerable<string> parts) => string.Join(sep, parts);

    /// <summary>太长就截断(尽量断在空格上,别把标识符切成两半)。**只有显示那条路用。**</summary>
    private static string Fit(string s)
    {
        if (s.Length <= MaxLength) return s;
        var cut = s[..(MaxLength - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > MaxLength / 2) cut = cut[..space];
        return cut + "...";
    }
}
