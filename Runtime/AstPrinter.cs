namespace Ravel.Runtime;

using System.Text;

internal static class AstPrinter
{
    private const int MaxLength = 72;

    private const int Step = 4;

    private const int InlineMax = 60;

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

    // `verbatim`:印**真跑的那棵树** —— 合成语句(注解检查、回绑那条)也印出来。
    // 转储(`ravel ast --out`)要的是这个:只印"看着像源码"的那份会丢名字绑定,
    // 印出来再解析就对不上了。给人看的签名/块仍走默认那条路(合成语句滤掉)。
    public static string Program(Program p, bool verbatim = false)
    {
        var prev = Verbatim;
        Verbatim = verbatim;
        try { return Join("\n", p.Statements.Select(s => Statement(s, 0))); }
        finally { Verbatim = prev; }
    }

    private static bool Verbatim;

    private static LambdaExpr? Next(BlockExpr body)
        => Visible(body) is [.., ExpressionStatement { Expr: LambdaExpr inner }] ? inner : null;

    // 合成语句(注解检查)不印 —— 求值时照跑,只是别让它漏进签名/转储里。
    private static List<Statement> Visible(BlockExpr b)
        => Verbatim
            ? b.Statements
            : b.Statements.Where(s => s is not (ExpressionStatement { Synthetic: true } or VarDefinition { Synthetic: true })).ToList();

    private static string Param(string name, Expression type, Pattern? pattern, int indent,
                                List<string>? attrs = null)
        => Attrs(attrs)
         + (pattern is null || indent >= 0 ? name + ": " + Annotated(type, indent)
          : IsUnitPattern(pattern) ? "()"                    // 见下:两种写法一个构造,挑短的印
          : Pattern(pattern, indent));

    internal static bool IsUnitPattern(Pattern p) => p switch
    {
        LiteralPattern { Value: VoidLiteral } => true,
        WholePattern w => IsUnitPattern(w.Inner),
        _ => false,
    };

    private static string Param(Parameter p, int indent)
        => Param(p.Name, p.Type, p.Pattern, indent, p.Attrs);

    public static string Describe(Pattern p) => IsUnitPattern(p) ? "()" : Pattern(p, -1);

    public static string Block(BlockExpr b) => Fit(BlockInner(b, -1));

    private static string BlockInner(BlockExpr b, int indent)
    {
        var stmts = Visible(b);
        if (stmts is []) return "{ }";

        var inline = "{ " + Join("; ", stmts.Select(s => Statement(s, -1))) + "; }";
        // `indent < 0` 是显示那条路,**一律**单行(它自己带截断)
        if (indent < 0) return inline;

        // 展开那条路要短才留一行 —— 而且单行写出来的东西里不该有换行(有就说明里头展开了)。
        // **量归量、印归印**:上面那句是按**单行档**量的(那是它最短的写法),真印出来得用
        // **当前这一档**再走一遍 —— 两档印出来的东西**不完全一样**(模式参数:显示档是 `()`、
        // 源码档是 `__p…: void`),直接复用那个量的结果会把显示档的样子漏进源码里。
        if (inline.Length <= InlineMax && !inline.Contains('\n'))
            return "{ " + Join("; ", stmts.Select(s => Statement(s, indent))) + "; }";

        var lines = stmts.Select(s => Pad(indent + 1) + Statement(s, indent + 1) + ";");
        return "{\n" + Join("\n", lines) + "\n" + Pad(indent) + "}";
    }

    private static Exception Unknown(object node)
        => new InvalidOperationException($"AstPrinter 不认识 {node.GetType().Name} 这个节点");

    private static string Pad(int indent) => new(' ', indent * Step);

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
            // `::` 和 `:` 是**两种**写法(`::` 是自动命名:名字由值定),`Named` 丢了回读就变一种。
            + (v.TypeAnnotation != null ? (v.Named ? "::" : ": ") + Annotated(v.TypeAnnotation, indent) + " = "
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
        // 活不过 `Lowering`(它折成 `+` 链),留着是为了"万一"时印得对。
        FStringExpr f => "f\""
            + string.Concat(f.Parts.Select(p => p.Interp is { } x
                ? "{" + Expr(x, indent) + "}"
                : p.Text.Replace("{", "{{").Replace("}", "}}")))
            + "\"",
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
        // 空字典印成 `{->}` —— 单看 `{}` 是空集合(`->` 才是字典的判据,见 ParseBrace)。
        DictLiteral d => d.Entries.Count == 0 ? "{->}" : "{" + Join(" ", d.Entries.Select(x =>
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
        // **例外**:`a.+ b` 这种"运算符成员调用"就是脱糖后的 `a + b`(`Lowering.MemberCall`),
        // 按中缀印回去 —— 否则打印出来的源码会把每个加法都写成成员调用,差分测试没法跟
        // 手写的式子对照。结合性/括号直接借 `Binary` 那条路,保证和手写的二元式印得一样。
        CallExpr { Function: MemberAccess m, Argument: var ra } when OperatorSymbols.IsSymbol(m.Member) => Binary(
            new BinaryExpr(m.Object, m.Member, ra), indent),
        CallExpr c => Atom(c.Function, indent) + " " + Arg(c.Argument, indent),
        BinaryExpr b => Binary(b, indent),
        ChainCompare c => Chain(c, indent),
        // 一元运算符的操作数**至少要跟它一样紧**(18 = `**` 那一档)才不用套括号。
        // 这里从前写的是 `Atom`,而 `Atom` **认调用** —— 于是 `!(x.IsEmpty ())` 印成
        // `!x.IsEmpty ()`,读回去 `!` 把那串全吃了(实测:`!a.IsEmpty () || !b.IsEmpty ()`
        // 和 `!(a.IsEmpty ()) || !(b.IsEmpty ())` 是**两棵树**)。前缀运算符后面那串
        // 和实参一样会往右边吃,所以调用在这儿也得套括号。
        UnaryExpr u => u.Op + Paren(u.Operand, Operators.UnaryPrec + 1, indent),
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

    private static int PrintPrec(string symbol) => Operators.PrintPrec(symbol);

    // `a < b < c` —— 操作数按比较档套括号
    private static string Chain(ChainCompare c, int indent)
    {
        var min = PrintPrec("<") + 1;
        var parts = new List<string> { Paren(c.Operands[0], min, indent) };
        for (var i = 0; i < c.Ops.Count; i++)
        {
            parts.Add(c.Ops[i]);
            parts.Add(Paren(c.Operands[i + 1], min, indent));
        }
        return Join(" ", parts);
    }

    private static string Binary(BinaryExpr b, int indent)
    {
        var p = PrintPrec(b.Op);
        // 操作数给多紧才不用套括号 —— 看**结合性**:
        // 左结合的(`a - b - c` = `(a-b)-c`)左边给 `p`(同级不套)、右边给 `p + 1`
        // (`a - (b - c)` 同级,要套);右结合的(`**`)正好倒过来。
        var right = b.Op == "**";
        // 比较整档同优先级，但串起来是链式比较，所以两边都收紧
        var cmp = Operators.IsCompare(b.Op);
        var left = Paren(b.Left, cmp ? p + 1 : right ? p + 1 : p, indent);
        var rightOp = Paren(b.Right, cmp ? p + 1 : right ? p : p + 1, indent);
        return left + " " + b.Op + " " + rightOp;
    }

    private static string Paren(Expression e, int min, int indent)
        => Prec(e) < min ? "(" + Expr(e, indent) + ")" : Expr(e, indent);

    private static int Prec(Expression e) => e switch
    {
        BinaryExpr b => PrintPrec(b.Op),
        ChainCompare => PrintPrec("<"),
        // 一元比 `**` 松、比其余运算紧 —— 所以 `(-2) ** 2` 那个左操作数得套括号
        // (不套读回去是 `-(2 ** 2)`,另一个数),而 `a + -b` 不用。
        UnaryExpr => Operators.UnaryPrec,
        IdentifierExpr i when IsWordOperator(i) => -1,
        CallExpr or LambdaExpr or BlockExpr or SlotExpr or DoExpr or PipeExpr => -1,
        _ => 100,
    };

    private static bool IsWordOperator(Expression e) => e is IdentifierExpr { Name: "or" or "and" };

    private static string Atom(Expression e, int indent) => e switch
    {
        IdentifierExpr i when IsWordOperator(i) => "(" + Expr(e, indent) + ")",
        NumberLiteral or StringLiteral or CharLiteral or IdentifierExpr or VoidLiteral or HoleExpr
            or RangeExpr or ListLiteral or SetLiteral or DictLiteral or MemberAccess or CallExpr
            or FStringExpr or LiteralExpr => Expr(e, indent),
        _ => "(" + Expr(e, indent) + ")",
    };

    private static string Arg(Expression e, int indent) => e switch
    {
        IdentifierExpr i when IsWordOperator(i) => "(" + Expr(e, indent) + ")",
        NumberLiteral or StringLiteral or CharLiteral or IdentifierExpr or VoidLiteral or HoleExpr
            or RangeExpr or ListLiteral or SetLiteral or DictLiteral or MemberAccess
            or FStringExpr or LiteralExpr => Expr(e, indent),
        _ => "(" + Expr(e, indent) + ")",
    };

    private static string Annotated(Expression type, int indent) => Paren(type, 100, indent);

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
            PlacePattern pl => Expr(pl.Place, indent),
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

    private static string EscapeString(char c) => c switch
    {
        '\\' => "\\\\",
        '"' => "\\\"",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        _ => c.ToString(),
    };

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

    private static string Fit(string s)
    {
        if (s.Length <= MaxLength) return s;
        var cut = s[..(MaxLength - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > MaxLength / 2) cut = cut[..space];
        return cut + "...";
    }
}
