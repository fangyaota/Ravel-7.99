namespace Ravel;

using Ravel.Runtime;

/// <summary>解析**之后**的那一趟脱糖(降阶):把语法树里还留着的糖换成直白的形式。
///
/// <code>
/// f or g    →   (x) => { (f x) || (g x); }        (and 是 &amp;&amp;)
/// a ?? b    →   NilOr (() => { a; }) (() => { b; })
/// x ??= v   →   NilFill (() => { x; }) ((w) => { x = w; }) (() => { v; })
/// x++       →   x += 1                            (-- 是 -=)
/// do { … }  →   Bind 链（DoExpr 折完就没）
/// </code>
///
/// **能放这儿的判据:"未脱糖的形状在 AST 里装得下"。** `BinaryExpr(l, "??", r)`、
/// `UnaryExpr("++", x)` 都装得下,`or` / `and` 更是本来就长那个样;`do` 本来没地方站,
/// 给它新开了一个 `DoExpr` —— 和 `BindStatement` 一样是**活不到求值期**的节点,有先例。
/// 真装不下的就留在解析器里:
///
///   * `?.` —— 折的是"接收者 **+ 后面整条链**",那半截是解析期接着读出来的(要占位符代入),
///     不是局部改写;
///   * `_` 占位符 —— 得**先在**解析期(LambdaExpr.Sugar 那个标记就是为它存在的,
///     同一句里后面的糖要能看穿它);
///   * `break` / `continue` / `return` —— 要解析期的循环栈 / lambda 深度栈;
///   * 多参 lambda `(a b) => …` —— `LambdaExpr` 只装得下一个参数,不折就没法表示;
///   * 运算符节 `+.2`、`|>` —— 解析决策(哪个符号、挂成员还是喂实参)。
///
/// 顺带一个实在的好处:解析器里不再有"为了认一条糖而试读、不成再退"那种回退 ——
/// 那正是**指数级**的温床(见 `tests/diag/152_error_report.rav` 里那 1000 层括号)。
/// 将来**模式匹配 / 解构**那种"把一大坨语法折成现有构造"的活,也放这儿。
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
    /// <summary>每折一条糖就取一个新参数名 —— 嵌套着写也不会撞。</summary>
    private int _born;

    /// <summary>报错时要写进 `SourceSpot` 的文件名。取不到(插值片段)就 null
    /// —— `ErrorReport` 那边认这个。</summary>
    private readonly string? _source;

    private Lowering(string? source) => _source = source;

    /// <summary>跑一趟。**每个解析出口都要过一手**(`Parser.Parse` / `Parser.ParseExpression`)。</summary>
    public static Program Apply(Program p) => new Lowering(p.Source).Run(p);

    /// <summary>单个表达式那一路 —— 插值字符串的片段(`"${…}"`)走这条。</summary>
    public static Expression Apply(Expression e, string? source = null) => new Lowering(source).Lower(e);

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
        // ── 这一趟的主要理由 ──
        // 两个操作数**先各自往下走一遍**:`(a or b) or c` 里里外外都是谓词,得一视同仁
        BinaryExpr { Op: "or" or "and" } b => Predicate(b),

        // ── 空值合并那一对 ──
        // `a ?? b` 是**惰性**的:右边"要不要跑"由库里那个 `NilOr` 说了算 ——
        // 折成 `NilOr (() => { a; }) (() => { b; })`(见 `lib/predefined.rav`)。
        BinaryExpr { Op: "??" } b => Coalesce(b),

        // `x ??= v` / `a.b ??= v` —— **空才写**:不空连 setter 都不调。
        // 折成 `NilFill (() => { x; }) ((w) => { x = w; }) (() => { v; })`:读一点、
        // 写一点、后备一块,三样分开交给库,「空不空」由它判。
        BinaryExpr { Op: "??=" } b => CoalesceAssign(b),

        // ── `do { … }` ──
        DoExpr d => Do(d),

        // ── `x++` / `x--` ──
        // 折出来的就是一条 `BinaryExpr(x, "+=", 1)`,和手写的 `x += 1` 走**同一条路**
        // (变量一条、字段一条,见 `Interpreter.Binary` 的 StepCompoundAssign*)——
        // 所以 `by` 属性那种槽、只读字段那些规矩,一个都不用在这儿重说一遍。
        // **不交回值**(`y := x++` 是语法错误),所以解析器只在**语句位置**认它。
        UnaryExpr { Op: "++" or "--" } u => IncDec(u),

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

    /// <summary>`a ?? b` → `NilOr (() => { a; }) (() => { b; })`。</summary>
    private Expression Coalesce(BinaryExpr b)
        => Call(Call(Ident("NilOr", b), Thunk(Lower(b.Left), b)), Thunk(Lower(b.Right), b));

    /// <summary>`x ??= v` / `a.b ??= v` → `NilFill (() =&gt; { x; }) ((w) =&gt; { x = w; }) (() =&gt; { v; })`。
    ///
    /// 成员那条把**接收者先求一次**再包进闭包(`((r) => { … }) (a)`)—— 否则
    /// `f ().b ??= v` 会把 `f` 跑两遍。
    ///
    /// **形状检查留在解析器里**(`left` 得是变量或成员):那是**语法**规则,
    /// 而且在那儿报错,插入符指着的是 `??=` 那个位置,比指着整条语句准。</summary>
    private Expression CoalesceAssign(BinaryExpr b)
    {
        var fallback = Thunk(Lower(b.Right), b);

        Expression FillWith(Expression target)
            => Call(Call(Call(Ident("NilFill", b), Thunk(target, b)), Setter(target)), fallback);

        // 写回去那一条:`((w) => { target = w; })`。赋值表达式本身交出写进去的那个值,
        // 所以库里 `NilFill` 交回来的也就是它。
        Expression Setter(Expression target)
        {
            var w = "_nil" + _born++;
            var wid = Ident(w, b);
            var assign = new BinaryExpr(target, "=", wid) { Line = b.Line, Column = b.Column };
            return new LambdaExpr(new Parameter(w, Ident("object", b)), AsBlock(assign, b))
                { Line = b.Line, Column = b.Column, Sugar = true };
        }

        switch (Lower(b.Left))
        {
            case IdentifierExpr id:
                return FillWith(id);

            case MemberAccess ma:
            {
                var recv = "_nil" + _born++;
                var target = new MemberAccess(Ident(recv, b), ma.Member) { Line = b.Line, Column = b.Column };
                var lam = new LambdaExpr(new Parameter(recv, Ident("object", b)), AsBlock(FillWith(target), b))
                    { Line = b.Line, Column = b.Column, Sugar = true };
                return Call(lam, Lower(ma.Object));
            }

            default:
                // 解析器挡过一道了,按理到不了这儿;真到了说明它那条件松了
                throw Error(b, "'??=' 的左边得是变量或成员（就像 '=' 那样）");
        }
    }

    /// <summary>`do { … }` → `Bind` 链:每遇到一条 `名字 :&lt; 表达式`,就把它**后面剩下的全部**
    /// 包成 lambda 交给 `Bind`;普通语句搁在同一个 lambda 体里,它前面。最后一条语句
    /// (不许是 `:<`)的值就是整块的值 —— 也就是最内层那个 lambda 体的值。
    ///
    /// <code>
    /// do { x :< m1; y :< m2; Some (x + y); }
    /// ≡  m1.Bind ((x: object) =&gt; { m2.Bind ((y: object) =&gt; { Some (x + y); }); })
    /// </code>
    ///
    /// 绑定出来的变量标 `object`(最宽的那个)—— 这儿对拿到什么一无所知,而 lambda 的参数
    /// 也只知道"有东西来了"。想要具体类型就在块里自己过一手(`n: int = x`)。
    ///
    /// 块里的语句**先各自降一遍**再折(`or` / `??` 那些可能就在里面),于是折出来的
    /// 那棵树里不再有糖。</summary>
    private Expression Do(DoExpr d)
    {
        var stmts = d.Statements.Select(Statement).ToList();

        // 从最后一条往前折:`rest` 始终是"后面那些语句"折出来的那段
        Expression rest = Block([stmts[^1]], d);
        for (var i = stmts.Count - 2; i >= 0; i--)
        {
            if (stmts[i] is not BindStatement bind)
            {
                // 普通语句就搁在折好的那段前面,它于是落在同一个 lambda 体里
                rest = Prepend(stmts[i], rest, d);
                continue;
            }

            var lam = new LambdaExpr(new Parameter(bind.Name, Ident("object", d)), AsBlock(rest, d))
                { Line = d.Line, Column = d.Column };
            rest = new CallExpr(new MemberAccess(bind.Monad, "Bind") { Line = bind.Line, Column = bind.Column },
                lam) { Line = d.Line, Column = d.Column };
        }

        return rest;
    }

    /// <summary>把一条语句搁到已经折好的那段**前面**(`do` 里那些不是 `:<` 的语句)。</summary>
    private Expression Prepend(Statement s, Expression rest, AstNode at)
        => rest is BlockExpr b
            ? b with { Statements = [s, .. b.Statements] }
            : Block([s, new ExpressionStatement(rest) { Line = rest.Line, Column = rest.Column }], at);

    /// <summary>`x++` → `x += 1`(`--` 是 `-=`)。目标的形状同样在解析器里查过。</summary>
    private BinaryExpr IncDec(UnaryExpr u)
        => new(Lower(u.Operand), u.Op == "++" ? "+=" : "-=",
               new NumberLiteral("1") { Line = u.Line, Column = u.Column })
        { Line = u.Line, Column = u.Column };

    /// <summary>把一条表达式包成"要用才跑"的块 `() => { expr; }`。参数名是 `_`(void),
    /// 和 `() => …` 的 AST 写法一致(见 `Parser.Atoms.ParseParen`)。</summary>
    private LambdaExpr Thunk(Expression e, AstNode at)
        => new(new Parameter("_", new IdentifierExpr("void") { Line = at.Line, Column = at.Column }), AsBlock(e, at))
            { Line = at.Line, Column = at.Column, Sugar = true };

    /// <summary>折出来的那段要当 lambda 的体,得是个块:已经是就原样,不是就包一层。</summary>
    private BlockExpr AsBlock(Expression e, AstNode at)
        => e is BlockExpr b ? b : Block([new ExpressionStatement(e) { Line = e.Line, Column = e.Column }], at);

    /// <summary>造一块。`Source` 得跟着走 —— 报错和调用栈沿帧链找的就是它
    /// (从前在解析器里折时,用的也是解析器记的那个 `source`)。</summary>
    private BlockExpr Block(List<Statement> stmts, AstNode at)
        => new(stmts) { Line = at.Line, Column = at.Column, Source = _source };

    private static CallExpr Call(Expression f, Expression arg)
        => new(f, arg) { Line = f.Line, Column = f.Column };

    /// <summary>造一个标识符节点,位置借那个糖节点的(报错/调用栈指回源码那一格)。</summary>
    private static IdentifierExpr Ident(string name, AstNode at)
        => new(name) { Line = at.Line, Column = at.Column };

    /// <summary>降阶阶段的报错。位置取自糖节点,文件取自 `Program.Source`。</summary>
    private SyntaxException Error(AstNode at, string message)
        => new(message, new SourceSpot(_source, at.Line, at.Column));
}
