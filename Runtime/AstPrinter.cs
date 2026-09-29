namespace Ravel.Runtime;

/// <summary>把 AST 渲染回 Ravel 源码 —— 给「打印一个函数」用:函数值没有可显示的标量,
/// 能显示的只有它的代码。
///
/// **只求读得懂,不保证能原样解析回去**:格式、注释、换行都丢了,该加括号的地方按
/// 保守规则加(嵌套的二元/一元/lambda 一律套一层,所以宁可多不会少)。要精确的原文
/// 得另外记源码区间,这里不做。
///
/// 超出 <see cref="MaxLength"/> 就截断:函数的体可以很长,而调用方(`print`、报错文案)
/// 要的只是一个能认出"这是哪个函数"的线索。</summary>
internal static class AstPrinter
{
    private const int MaxLength = 72;

    /// <summary>渲染一个函数的**签名链**:`(a: int) => { … }`。
    ///
    /// 多参数 lambda 在语法上就是"体里只有一层 lambda"套出来的(见 `Parser.Atoms` 的消糖),
    /// 所以往后走一层就多一个参数、最后一层的体才是真正的函数体 —— 打印时把它还原成
    /// 一条完整的参数表,而不是露馅成嵌套的 `(a) => { (b) => { … } }`。</summary>
    public static string Signature(LambdaVal lam)
    {
        var parts = new List<string> { Param(lam.ParamName, lam.ParamTypeExpr) };
        var body = lam.Block;
        while (Next(body) is { } inner)
        {
            parts.Add(Param(inner.Param.Name, inner.Param.Type));
            body = inner.Body;
        }

        return "(" + string.Join(" ", parts) + ") => " + Block(body);
    }

    /// <summary>体里只跟着一个 lambda 表达式的话,把那个 lambda 返回出来(多参数 lambda 的消糖形状)。
    /// 多一条语句就不是了 —— 那是"函数体里顺手定义了个函数",不是一条参数链。</summary>
    private static LambdaExpr? Next(BlockExpr body)
        => body.Statements is [ExpressionStatement { Expr: LambdaExpr inner }] ? inner : null;

    /// <summary>参数:`名字: 注解`。注解本身是表达式,照源码渲染(常见就是一个名字)。</summary>
    private static string Param(string name, Expression type) => name + ": " + Expr(type);

    /// <summary>渲染一个块:`{ a := 1; f a; }`。
    /// 每条语句带分号 —— 单行块在 Ravel 里本来就要写 `;`(见 `Parser.ParseMandatoryBlock`),
    /// 打印出来的东西因此也解析得回去。</summary>
    public static string Block(BlockExpr b)
    {
        var stmts = b.Statements.Select(Statement).ToList();
        return Fit(stmts.Count == 0 ? "{ }" : "{ " + Join("; ", stmts) + "; }");
    }

    private static string Statement(Statement s) => s switch
    {
        VarDefinition v =>
            v.Name
            + (v.TypeAnnotation != null ? ": " + Expr(v.TypeAnnotation) : "")
            + (v.Named ? " ::= " : " := ")
            + Expr(v.Value),
        Assignment a => a.Name + " = " + Expr(a.Value),
        SlotAssign sa => "by " + Expr(sa.Path) + (sa.Define ? " := " : " = ") + Expr(sa.Value),
        ExpressionStatement es => Expr(es.Expr),
        _ => "...",        // 将来加了新语句种类,打印退化成一个省略号,别把 ToString 搞炸
    };

    private static string Expr(Expression e) => e switch
    {
        NumberLiteral n => n.Lexeme,
        StringLiteral s => "\"" + s.Value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        CharLiteral c => "'" + c.Value switch
        {
            '\\' => "\\\\",
            '\'' => "\\'",
            '\n' => "\\n",
            '\t' => "\\t",
            '\r' => "\\r",
            _ => c.Value.ToString(),
        } + "'",
        IdentifierExpr i => i.Name,
        VoidLiteral => "()",
        ListLiteral l => "[" + Join(" ", l.Elements.Select(Expr)) + "]",
        SetLiteral s => "{" + Join(" ", s.Elements.Select(Expr)) + "}",
        DictLiteral d => "{" + Join(" ", d.Entries.Select(x => x.Key + ": " + Expr(x.Value))) + "}",
        // 取成员的**接收者**若本身是一次调用,必须套括号:`f a.b` 读起来是 `f (a.b)`
        // (实参位置只吃"主表达式 + 取成员"),而这里要说的是 `(f a).b` —— do 块脱糖出来的
        // `m.Bind (…)` 全是这个形状,不套括号打印出来是另一个意思。
        SlotExpr sl => "by " + Expr(sl.Path),
        MemberAccess m => (m.Object is CallExpr ? "(" + Expr(m.Object) + ")" : Atom(m.Object))
                          + "." + m.Member,
        CallExpr c => Atom(c.Function) + " " + Atom(c.Argument),
        BinaryExpr b => Expr(b.Left) + " " + b.Op + " " + Expr(b.Right),
        UnaryExpr u => u.Op + Atom(u.Operand),
        PipeExpr p => Expr(p.Left) + " <| " + Expr(p.Right),
        LambdaExpr lam => "(" + Param(lam.Param.Name, lam.Param.Type) + ") => " + Block(lam.Body),
        BlockExpr b => Block(b),
        HoleExpr h => "_" + h.Index,
        // 内置类的预设类体里是 C# 造好的值,没有源码可还原
        LiteralExpr => "<builtin>",
        _ => "...",
    };

    /// <summary>要拼进更大表达式时,自身定界的那些直接写,其余的套一层括号。</summary>
    private static string Atom(Expression e) => e switch
    {
        NumberLiteral or StringLiteral or CharLiteral or IdentifierExpr or VoidLiteral or HoleExpr
            or ListLiteral or SetLiteral or DictLiteral or MemberAccess or CallExpr or LiteralExpr => Expr(e),
        _ => "(" + Expr(e) + ")",
    };

    private static string Join(string sep, IEnumerable<string> parts) => string.Join(sep, parts);

    /// <summary>太长就截断(尽量断在空格上,别把标识符切成两半)。</summary>
    private static string Fit(string s)
    {
        if (s.Length <= MaxLength) return s;
        var cut = s[..(MaxLength - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > MaxLength / 2) cut = cut[..space];
        return cut + "...";
    }
}
