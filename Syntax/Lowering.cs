namespace Ravel;

/// <summary>解析**之后**的那一趟脱糖(降阶):把语法树里还留着的糖换成直白的形式。
///
/// 现在只有一条 —— **谓词级的 `or` / `and`**:
///
///     f or g    →   (x) => { (f x) || (g x); }
///     f and g   →   (x) => { (f x) && (g x); }
///
/// 为什么这一条**不在解析器里就地折**(别的糖都是在解析器里就地折的):它是**运算符**,
/// 解析器只该管"这儿有个二元运算";`(f x) || (g x)` 是**语义**,不是语法。
/// 分成两步另有两个实在的好处:
///
///   * 解析器里不再有"为了认一条糖而试读、不成再退"那种回退 —— 那正是**指数级**的温床
///     (见 `tests/diag/152_error_report.rav` 里那 1000 层括号);
///   * 将来**模式匹配 / 解构**那种"把一大坨语法折成现有构造"的活,有地方放了。
///
/// 注意它**跑在最后**:`Parser.Parse` 出树之前就折完了,所以 `AstPrinter`(打印函数体、
/// 报错时贴代码)看到的已经是折过的样子 —— 和从前就地折时打出来的一样。
///
/// **次序:跑在占位符消糖(`Parser.Holes`)之后。** 那趟是解析期做的,`_` 到这儿已经换成
/// 真参数了;反过来先折的话,`_` 会被关进刚造出来的那枚 lambda 里,再也收不到外层去。
///
/// 一趟而已,别指望它做优化 —— 它只认形状、换形状,不看类型、不折叠常量。
/// (`Reject` / 方法名之类**同名不同义的**别往这儿塞:那要类型信息,是求值期的事。)</summary>
public sealed class Lowering
{
    /// <summary>每折一个谓词就取一个新参数名 —— 嵌套着写也不会撞。</summary>
    private int _born;

    /// <summary>跑一趟。**每个解析出口都要过一手**(`Parser.Parse` / `Parser.ParseExpression`)。</summary>
    public static Program Apply(Program p) => new Lowering().Run(p);

    /// <summary>单个表达式那一路 —— 插值字符串的片段(`"${…}"`)走这条。</summary>
    public static Expression Apply(Expression e) => new Lowering().Lower(e);

    private Program Run(Program p) => p with { Statements = p.Statements.Select(Statement).ToList() };

    // ========================================
    //  语句
    // ========================================

    private Statement Statement(Statement s) => s switch
    {
        VarDefinition v => v with
        {
            TypeAnnotation = v.TypeAnnotation is null ? null : Lower(v.TypeAnnotation),
            Value = Lower(v.Value),
        },
        Assignment a => a with { Value = Lower(a.Value) },

        // `by a = X` / `by a.x = X` 两条路都带表达式;`Define` 那些开关是 `with` 自动带过去的
        SlotAssign sa => sa with { Path = Lower(sa.Path), Value = Lower(sa.Value) },

        ExpressionStatement es => es with { Expr = Lower(es.Expr) },

        // 只有解析器里手写的 `do` 深度护栏能放行;`ParseDo` 折完就没它了(mk 见 Ast.cs 的注解)
        BindStatement b => b with { Monad = Lower(b.Monad) },

        _ => throw new NotSupportedException($"Lowering 没认这条语句:{s.GetType().Name}"),
    };

    // ========================================
    //  表达式
    // ========================================

    private Expression Lower(Expression e) => e switch
    {
        // ── 这一趟存在的理由 ──
        // 两个操作数**先各自往下走一遍**:`(a or b) or c` 里里外外都是谓词,得一视同仁
        BinaryExpr { Op: "or" or "and" } b => Predicate(b),

        // ── 别的只是"往下走、原样重建" ──
        BinaryExpr b => b with { Left = Lower(b.Left), Right = Lower(b.Right) },
        CallExpr c => c with { Function = Lower(c.Function), Argument = Lower(c.Argument) },
        MemberAccess m => m with { Object = Lower(m.Object) },
        UnaryExpr u => u with { Operand = Lower(u.Operand) },
        PipeExpr p => p with { Left = Lower(p.Left), Right = Lower(p.Right) },
        SlotExpr sl => sl with { Path = Lower(sl.Path) },
        ListLiteral l => l with { Elements = l.Elements.Select(Lower).ToList() },
        SetLiteral s => s with { Elements = s.Elements.Select(Lower).ToList() },
        RangeExpr r => r with { Lo = Lower(r.Lo), Hi = Lower(r.Hi) },
        DictLiteral d => d with
        {
            Entries = d.Entries.Select(x => x with { Key = Lower(x.Key), Value = Lower(x.Value) }).ToList(),
        },
        LambdaExpr l => l with
        {
            // 参数的类型是**表达式**(`(x: 某个类型表达式) => …`),所以也得走一趟
            Param = l.Param with { Type = Lower(l.Param.Type) },
            Body = (BlockExpr)Lower(l.Body),
        },
        BlockExpr bl => bl with { Statements = bl.Statements.Select(Statement).ToList() },

        // ── 叶子:没有能装表达式的格子 ──
        NumberLiteral or StringLiteral or CharLiteral or IdentifierExpr
            or VoidLiteral or LiteralExpr or HoleExpr => e,

        _ => throw new NotSupportedException($"Lowering 没认这个节点:{e.GetType().Name}"),
    };

    /// <summary>`f or g` → `(x) => { (f x) || (g x); }`(与是 `&&`)。
    ///
    /// 交回的是一枚**一元函数**,和 `|` 的交替、`>>` 的组合一个口径:两边都拿同一个实参喂。
    /// 于是短路照旧短路 —— `||` / `&&` 本来就是短路的,`(f x)` 为真时 `(g x)` 根本不求。
    ///
    /// 参数名固定是 `_or0` / `_and0` 这种(带序号)。**用户要是真有个变量叫这个名**,
    /// 它在体的那两个调用点会被这枚参数遮住 —— 和别的消糖(`_0` 那套)一个口径,
    /// 不额外防:防不住,也没人这么起名。</summary>
    private LambdaExpr Predicate(BinaryExpr b)
    {
        var f = Lower(b.Left);
        var g = Lower(b.Right);
        var name = "_" + b.Op + _born++;
        var at = Ident(name, b);

        var both = new BinaryExpr(Call(f, at), b.Op == "or" ? "||" : "&&", Call(g, at))
            { Line = b.Line, Column = b.Column };
        var body = new BlockExpr([new ExpressionStatement(both) { Line = b.Line, Column = b.Column }])
            { Line = b.Line, Column = b.Column };

        return new LambdaExpr(new Parameter(name, Ident("object", b)), body)
            { Line = b.Line, Column = b.Column, Sugar = true };
    }

    private static CallExpr Call(Expression f, Expression arg)
        => new(f, arg) { Line = f.Line, Column = f.Column };

    /// <summary>造一个标识符节点,位置借那个糖节点的(报错/调用栈指回源码那一格)。</summary>
    private static IdentifierExpr Ident(string name, BinaryExpr at)
        => new(name) { Line = at.Line, Column = at.Column };
}
