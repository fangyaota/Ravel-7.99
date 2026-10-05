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
    /// <summary>每次解析从 0 数的序号。给**作用域只在造出来那一小段里**的名字用:
    /// lambda 参数(`_or{n}` / `_nil{n}` / `__y{n}`)—— 它们天然不会撞,
    /// 而且 `_or{n}` / `_nil{n}` 会**出现在打印出来的函数体里**,从 0 数输出才可复现。</summary>
    private int _born;

    /// <summary>给**落在用户作用域里的中间量**取名字用的序号(`__g{n}` / `__d{n}`)。
    ///
    /// **全进程单调递增,不归零。** 这些是拿 `:=` 定义在**调用方那个作用域**里的,
    /// 而两次解析完全可能落在同一个作用域 —— `using` 进来的模块和主文件共享顶层作用域,
    /// `eval` 也一样。归零的话第二个文件里那句就撞上「`__g0` 这个名字已经存在」,
    /// 而那名字用户根本没写过,看了只会懵。
    ///
    /// 用全局序号**不影响输出**:它们从不进打印结果(打印一个 `Generator` 打的是
    /// `Generator { GetEnumerator = Property {...} }` 这种快照,里面的 lambda 不展开)。
    /// 会进输出的那几个参数名走 <see cref="_born"/>。</summary>
    private static int _tempSeq;

    private static string FreshTemp(string prefix)
        => prefix + Interlocked.Increment(ref _tempSeq);

    private static StringLiteral Lit(string s, AstNode at) => new(s) { Line = at.Line, Column = at.Column };

    /// <summary>把几段拼成一串 `+`(字面量和求出来的东西混着接)。</summary>
    private static Expression Concat(AstNode at, params Expression[] parts)
    {
        var e = parts[0];
        for (var i = 1; i < parts.Length; i++)
            e = new BinaryExpr(e, "+", parts[i]) { Line = at.Line, Column = at.Column };
        return e;
    }

    /// <summary>一个**类型表达式**的名字 —— 报错里说"要的是 List"用。
    /// 直接 `string` 它(注解求出来就是类型对象;`typeof` 一道反而得到"Type")。</summary>
    private static Expression TypeName(Expression type, AstNode at)
        => Call(Ident("string", at), type);

    /// <summary>一个**值**的类型名(`string (typeof v)`)—— 报错里说"得到 Integer"用。</summary>
    private static Expression ValueType(Expression v, AstNode at)
        => Call(Ident("string", at), Call(Ident("typeof", at), v));

    /// <summary>报错时要写进 `SourceSpot` 的文件名。取不到(插值片段)就 null
    /// —— `ErrorReport` 那边认这个。</summary>
    private readonly string? _source;

    private Lowering(string? source) => _source = source;

    /// <summary>跑一趟。**每个解析出口都要过一手**(`Parser.Parse` / `Parser.ParseExpression`)。</summary>
    public static Program Apply(Program p) => new Lowering(p.Source).Run(p);

    /// <summary>单个表达式那一路 —— 插值字符串的片段(`"${…}"`)走这条。</summary>
    public static Expression Apply(Expression e, string? source = null) => new Lowering(source).Lower(e);

    private Program Run(Program p) => p with { Statements = Stmts(p.Statements) };

    /// <summary>降一串语句。**一进一出的那些直接过 `Statement`;只有解构那条会一条变好几条**
    /// (所以这儿是 `SelectMany`,不是 `Select`)。</summary>
    private List<Statement> Stmts(List<Statement> xs) => xs.SelectMany(Expand).ToList();

    private IEnumerable<Statement> Expand(Statement s)
        => s is Destructure d ? Unpack(d) : [Statement(s)];

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
        LambdaExpr l => Lambda(l),
        BlockExpr bl => bl with { Statements = Stmts(bl.Statements) },

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

    /// <summary>`(…) => …` 那一枚 lambda。
    ///
    /// **模式参数**(`([x y]) => …`)在这一趟展开成体开头的几句解构 —— 参数本身拿的是
    /// 合成名(`__p{n}`),真正给用户用的名字由模式绑出来。于是"形状不对"就是一次普通求值
    /// 错误(`Bind` 报的 `TypeError`),而它发生在**这一支自己那次调用里**(第一格就判),
    /// 所以 `|` 的交替接得住 —— 多参子句上的守卫做不到这一点(那会儿交替帧早返回了)。
    ///
    /// 多参 lambda 是**柯里化**折出来的(`(a b) => …` 是一层层 `LambdaExpr`),所以
    /// 每一层各自带自己的参数、各自展开自己的模式 —— 这儿不用管层数。</summary>
    private Expression Lambda(LambdaExpr l)
    {
        // 参数的类型是**表达式**(`(x: 某个类型表达式) => …`),所以也得走一趟
        var body = (BlockExpr)Lower(l.Body);
        var param = l.Param with { Type = Lower(l.Param.Type), Pattern = null };

        if (l.Param.Pattern is { } p)
        {
            var binds = new List<Statement>();
            Bind(p, Ident(l.Param.Name, l), null, binds);
            body = body with { Statements = [.. binds, .. body.Statements] };
        }

        return l with { Param = param, Body = body };
    }

    /// <summary>解构:按模式把右边那个值拆成一串普通的 `:=`。
    ///
    /// <code>
    /// [x _ z ..rest] : T = e
    /// ≡  __d0 : T = e                     ← 有注解、或对象模式,才落地(见下)
    ///    __g0 := __d0.GetEnumerator ()
    ///    __g0.MoveNext ()   x := __g0.Current
    ///    __g0.MoveNext ()                 ← `_` 也要走一步:跳过 ≠ 不取
    ///    __g0.MoveNext ()   z := __g0.Current
    ///    rest := Generator (y: function) => { while { __g0.MoveNext (); } { y (__g0.Current); } }
    ///
    /// {x y z} : U = e
    /// ≡  __d0 : U = e ; x := __d0.x ; y := __d0.y ; z := __d0.z
    /// </code>
    ///
    /// **游标顺序照 `lib/iterator.rav` 那条**:`GetEnumerator` → **先 `MoveNext`** → 再 `Current`
    /// (`foreach` 就是这个白描,`Take` 自己控 `MoveNext` 时也是这个次序)。`..rest` 交回的
    /// 是"从这儿往后的那一串"—— 拿同一枚游标包一个 `Generator`,所以它和游标一样是**一次性的**。
    ///
    /// **中间量什么时候落地**(`__d{n}`):写了 `: T` 就一定落 —— 那条注解管的是**右边那个值**,
    /// 得有地方挂它;对象模式一个值要取好几个成员,不落地的话 `{x y} := f ()` 会把 `f` 跑两遍
    /// (`??=` 的成员那条同理,见 `CoalesceAssign`)。列表模式取一次就够(`.GetEnumerator ()`),
    /// 右边本来就是现成的名字时也不落。
    ///
    /// `Attrs`(修饰符)跟着每一个拆出来的定义走 —— 包括中间量:`private [x y] : T = e` 在类体里
    /// 拆出来的是两个私有成员,那两个中间量也不该露出去。</summary>
    private List<Statement> Unpack(Destructure d)
    {
        var outs = new List<Statement>();
        List<string>? attrs = d.HasAttrs ? d.Attrs : null;
        var value = Lower(d.Value);
        var pattern = d.Pattern;

        // 落地(造一个中间量 `__d{n}`)两个理由:
        //   * 这一格自己带了类型(`[x y] : list`)—— 那条注解得有地方挂,挂上去就当场验;
        //   * 右边不是现成的名字 —— 拆的时候它要被**读好几遍**(对象模式每个成员一次;
        //     列表模式判"是不是序列"一次、`GetEnumerator` 再一次),不落地会把右边跑几遍。
        var needTemp = pattern.Type is not null || value is not IdentifierExpr;
        var source = value;
        if (needTemp)
        {
            // **注解不挂在这儿** —— 类型一律由 `Bind` 入口那道检查管(`:` + 拒收)。
            // 挂成"带注解的定义"的话,顶层和里面那几格会用两套判据、两句报错。
            var t = FreshTemp("__d");
            outs.Add(Define(t, null, value, attrs, d));
            source = Ident(t, d);
        }

        Bind(pattern, source, attrs, outs);
        return outs;
    }

    /// <summary>按模式往 <paramref name="outs"/> 里补语句。嵌套的那几格递归进来。
    ///
    /// **拆不成分两种,都报 `TypeError`**(不是引擎内部那种硬错):
    ///
    /// * 这一份压根不是"能按顺序取的东西" → 当场说清,`得到 Integer` 那种;
    /// * 取到一半到底了(`MoveNext` 回 false)→ 说清"要第几格"。
    ///
    /// 为什么非得是 `TypeError`:**带模式的参数**就是靠它接住的 —— `([x y]) => … | (_) => …`
    /// 里第一支形状不对时,拒收要发生在**它自己那次调用里**(第一格就判),`|` 的交替才接得住。
    /// (多参子句上的守卫做不到这一点:那会儿交替帧早返回了。)
    ///
    /// 顺带把原来那两条**看不懂的错**换掉了:
    /// `[q w] := 5` 从前报「类型 'Integer' 没有方法 'GetEnumerator'」,
    /// `[q w] := [1]` 报「list.At 的索引 1 越界」—— 都是**脱糖内部**的词。</summary>
    private void Bind(Pattern p, Expression source, List<string>? attrs, List<Statement> outs)
    {
        var at = p;

        // **这一格自己带了类型**(`[x: int]` / `{n: int}` / `[[a b]: list]`):当场验,不成**拒收**。
        //
        // 为什么不是"给它挂个带注解的定义(`x : int = v`)让引擎去验":那条路的错误是
        // **体里异步**抛出来的,而 `|` 的交替只认**调用那一刻同步**抛的类型错
        // (`StepAlternate` 的 C# `catch`)+ 异步的 `RejectedException`(`ResumeAlternate`)。
        // 用拒收就和别的形状检查一条路,`|` 接得住 —— 参数模式当"分派头一格"就靠这条。
        //
        // 判据用的是 `:`(不是注解那套"能转就转"):模式说的是**形状**,`[a: float] := [1]`
        // 该说"不是 float",而不是悄悄转成 1.0。
        if (p.Type is { } want)
        {
            var isType = new BinaryExpr(source, ":", Lower(want)) { Line = at.Line, Column = at.Column };
            var msg = Concat(at, Lit("这一格要的是 ", at), TypeName(Lower(want), at),
                                 Lit("，得到 ", at), ValueType(source, at));
            outs.Add(ExprStmt(If(Not(isType, at), Reject(msg, at), at), at));
            p = p with { Type = null };                  // 验过了,下面按形状拆
        }

        // **这一格整体**的条件(`|> f`):把这一格的值当实参喂出去,不成拒收。
        // 用 `source` 而不是绑出来的名字 —— 名字和它本来就是同一个值,而复合的那几格
        // (列表/对象)压根没绑过名字,`source` 是**唯一**那条路,也是两层共用的那条。
        if (p.When is { } when)
        {
            // 名字那一格喂出去的是**元素**,复合的那几格喂的是**整块** —— 报错分开说。
            var msg = p is NamePattern { Name: var nm }
                ? Lit($"'{nm}' 没过 `|>` 后面那个条件", at)
                : Lit("这一整块没过 `|>` 后面那个条件", at);
            outs.Add(ExprStmt(If(Not(Call(when, source), at), Reject(msg, at), at), at));
        }

        switch (p)
        {
            // `_`:不绑东西。**列表**那边轮不到这儿 —— "跳过"在那一串里是**走一步**
            // (游标得往前挪),那件事只有在列表自己的循环里做得成;到这儿来的 `_`
            // 都是**按名字取**的位置(字典那一项、对象的成员),没有游标可挪,就是"不绑"。
            case SkipPattern:
                return;

            // **字面量那一格**:先过那一族、再比相等。两道不是一道 —— 见 `LiteralPattern`。
            case LiteralPattern lit:
                var what = Concat(at, Lit("这一格要的是 ", at), Lit(lit.Text, at));
                var got = Call(Ident("string", at), source);
                var sameKind = new BinaryExpr(source, ":", Lower(lit.Domain))
                    { Line = at.Line, Column = at.Column };
                outs.Add(ExprStmt(If(Not(sameKind, at),
                    Reject(Concat(at, what, Lit("，得到 ", at), got,
                                 Lit("（", at), ValueType(source, at), Lit("）", at)), at), at), at));
                var eq = new BinaryExpr(source, "==", Lower(lit.Value))
                    { Line = at.Line, Column = at.Column };
                outs.Add(ExprStmt(If(Not(eq, at),
                    Reject(Concat(at, what, Lit("，得到 ", at), got), at), at), at));
                return;

            case NamePattern n:
                outs.Add(Define(n.Name, null, source, attrs, at));
                CheckGuard(n, n.Name, Ident(n.Name, at), outs);
                return;

            case RestPattern r:
                // 剩下的 = 同一枚游标继续走到底。包成 `Generator` 才是个 `IEnumerable`
                // (`y` 是"往外送一个"的那个函数,和 `lib/generator.rav` 里各处一个写法)。
                var y = "_y" + _born++;
                var yid = Ident(y, at);
                var drain = Call(Ident("while", at),
                    Block([ExprStmt(Call0(Member(source, "MoveNext")), at)], at));
                drain = Call(drain, Block([ExprStmt(Call(yid, Member(source, "Current")), at)], at));
                var gen = new LambdaExpr(new Parameter(y, Ident("function", at)), AsBlock(drain, at))
                    { Line = at.Line, Column = at.Column, Sugar = true };
                outs.Add(Define(r.Name, null, Call(Ident("Generator", at), gen), attrs, at));
                CheckGuard(r, r.Name, Ident(r.Name, at), outs);
                return;

            case MemberPattern m:
                foreach (var item in m.Names)
                {
                    // 成员不在这一份上 —— 也是"形状对不上",不是引擎的硬错
                    outs.Add(ExprStmt(If(
                        Not(Call(Member(Call0(Member(source, "Fields")), "Contains"), Str(item.Name)), at),
                        Reject(Str($"要求成员 '{item.Name}'，可这一份上没有"), at), at), at));
                    Bind(item, Member(source, item.Name), attrs, outs);   // 同上:走那道口子
                }
                return;

            // **字典模式**:`{"a" -> v  k -> 1}` —— **按键**取。次序和列表那条一样:
            // 先说清"这一份得是 `dict`"(不然下面那两句报的是「类型 'Integer' 没有方法
            // 'Has'」,用户看不出是自己的模式用错了地方),再逐项查键在不在 —— 不在是**拒收**。
            //
            // `Has` 查在**前**:`Get` 缺键是会**抛**的,而"抛"会穿掉 `|` 的交替。
            // 老写法 `{x y}`(成员名)走的是另一条(`MemberPattern`):`Fields ()` + 成员访问。
            case DictPattern dd:
                outs.Add(ExprStmt(If(
                    Not(new BinaryExpr(source, ":", Ident("dict", at)) { Line = at.Line, Column = at.Column }, at),
                    Reject(Concat(at, Lit("要的是字典（按 `键 -> 模式` 拆的那种），得到 ", at),
                                 Call(Ident("string", at), Call(Ident("typeof", at), source))), at), at), at));
                foreach (var e in dd.Entries)
                {
                    var key = Lower(e.Key);

                    // 键**当得了键**吗 —— 当不了是**拒收**。这一步非有不可:下一步 `Has` 那头
                    // 会**抛**(「Key: List 没有默认的键…」),而抛会穿掉 `|` 的交替(那台机器
                    // 只接拒收)。判据就是字典自己那条 —— 按值比的那些:`ValueType` 那一支,
                    // 外加 `Bool` / `Void`(见 `BuiltinClasses.CanBeKey`,两处要一起改)。
                    var keyable =
                        new BinaryExpr(
                            new BinaryExpr(
                                new BinaryExpr(key, ":", Ident("ValueType", at)) { Line = at.Line, Column = at.Column },
                                "||",
                                new BinaryExpr(key, ":", Ident("bool", at)) { Line = at.Line, Column = at.Column })
                            { Line = at.Line, Column = at.Column },
                            "||",
                            new BinaryExpr(key, ":", Ident("void", at)) { Line = at.Line, Column = at.Column })
                        { Line = at.Line, Column = at.Column };
                    outs.Add(ExprStmt(If(Not(keyable, at),
                        Reject(Concat(at, Lit("这一项的键当不了键（要按值比的：数 / 串 / 字符 / 布尔 / `()`），得到 ", at),
                                     Call(Ident("string", at), Call(Ident("typeof", at), key))), at), at), at));

                    var missing = Concat(at, Lit("要键 ", at), Call(Ident("string", at), key),
                                             Lit("，可这一份上没有", at));
                    outs.Add(ExprStmt(If(Not(Call(Member(source, "Has"), key), at),
                        Reject(missing, at), at), at));
                    Bind(e.Sub, Call(Member(source, "Get"), key), attrs, outs);
                }
                return;

            case ListPattern l:
            {
                // 先确认它**能按顺序取** —— 不然下面那句 `GetEnumerator` 报的是
                // 「类型 'Integer' 没有方法 'GetEnumerator'」,用户看不出来是自己的模式用错了地方
                var notSeq = new BinaryExpr(
                    new StringLiteral("要的是能按顺序取的东西（list / set / dict / 字符串 / Generator…），得到 ")
                        { Line = at.Line, Column = at.Column },
                    "+",
                    Call(Ident("string", at), Call(Ident("typeof", at), source)))
                    { Line = at.Line, Column = at.Column };
                outs.Add(ExprStmt(If(
                    Not(new BinaryExpr(source, ":", Ident("IEnumerable", at)) { Line = at.Line, Column = at.Column }, at),
                    Reject(notSeq, at), at), at));

                var g = FreshTemp("__g");
                var gid = Ident(g, at);
                outs.Add(Define(g, null, Call0(Member(source, "GetEnumerator")), attrs, at));

                // **零格**(`[]`):要求它是**空的** —— 走一步还有东西就拒收。
                // (`Some` / `None` 分派靠这条收尾:`[v]` 认有值那半,`[]` 认空那半。)
                if (l.Parts.Count == 0)
                {
                    outs.Add(ExprStmt(If(Call0(Member(gid, "MoveNext")),
                        Reject(Str("要的是空的，可这一串还有东西"), at), at), at));
                    return;
                }

                var k = 0;
                foreach (var part in l.Parts)
                {
                    if (part is RestPattern) { Bind(part, gid, attrs, outs); continue; }

                    // **先走一步再取** —— 游标站在"还没读的那个"前面(见 `lib/iterator.rav`)。
                    // 走不动 = 这一串比模式短,同样是"形状对不上"。
                    k += 1;
                    outs.Add(ExprStmt(If(
                        Not(Call0(Member(gid, "MoveNext")), at),
                        Reject(Str($"要第 {k} 格，可这一串已经到底了"), at), at), at));

                    switch (part)
                    {
                        case SkipPattern:
                            break;                       // 走了这一步就是"跳过",没有别的
                        case NamePattern n:
                            // **走 `Bind` 那道口子**(不是就地 Define)—— 类型检查在它的入口
                            Bind(n, Member(gid, "Current"), attrs, outs);
                            break;
                        default:
                        {
                            // 嵌套:先落到一个中间量上,再拿它当下一次解构的源
                            var t = FreshTemp("__d");
                            outs.Add(Define(t, null, Member(gid, "Current"), attrs, at));
                            Bind(part, Ident(t, at), attrs, outs);
                            break;
                        }
                    }
                }

                return;
            }
        }

        throw Error(at, "Lowering 没认这个模式");
    }

    /// <summary>取反。**只在布尔上**用(`MoveNext` 的返回值、类型判定)——它就是一元的 `!`。</summary>
    private static UnaryExpr Not(Expression operand, AstNode at)
        => new("!", operand) { Line = at.Line, Column = at.Column };

    /// <summary>这一格带了**条件**(`[u == 1]` / `a == 1 = e`):绑完之后验一次,不成**拒收**。
    ///
    /// 名字用 <paramref name="bound"/> 那个**已经绑好的**标识符,不重读源 ——
    /// 所以条件里写的 `u` 就是绑出来的那个 `u`(和参数表里守卫的读法一致)。
    /// 判据一样走拒收(不是 `throw`):`|` 的交替接得住,参数模式和它一条路。</summary>
    private void CheckGuard(Pattern p, string name, Expression bound, List<Statement> outs)
    {
        if (p.Guard is not { } guard) return;
        var at = p;
        outs.Add(ExprStmt(If(Not(guard, at),
            Reject(Lit($"'{name}' 这一格的条件没过 —— 这一支不收这个值", at), at), at), at));
    }

    /// <summary>`if { 条件; } { 这一段; } { 0; }` —— 条件不成立才跑那一段。</summary>
    private Expression If(Expression cond, Expression then, AstNode at)
        => Call(Call(Call(Ident("if", at), AsBlock(cond, at)), AsBlock(then, at)), AsBlock(Num("0", at), at));

    /// <summary>**拒收**:`reject "…"`。
    ///
    /// 为什么不是 `throw (TypeError "…")`:那条路走的是**异常处理器栈**,`|` 的交替不认它
    /// (只有 `try` 接得住)。而带模式的参数正是靠 `|` 接住"这一支形状不对"才有用 ——
    /// 所以要用**同一枚拒收信号**(参数守卫用的也是它):先给最近的交替接,没人接才落到 `try`。
    /// 消息可以是拼出来的(不是序列那一条要带上实际类型)。</summary>
    private Expression Reject(Expression message, AstNode at)
        => Call(Member(Ident("System", at), "Reject"), Call(Ident("TypeError", at), message));

    private static StringLiteral Str(string v) => new(v);

    private static NumberLiteral Num(string v, AstNode at)
        => new(v) { Line = at.Line, Column = at.Column };

    // ── 造节点的几个小帮手(解构这摊用得多,单拎出来)──

    private MemberAccess Member(Expression obj, string name)
        => new(obj, name) { Line = obj.Line, Column = obj.Column };

    /// <summary>零参调用:`x.MoveNext ()` —— 实参是 `()`(Void)。</summary>
    private static CallExpr Call0(Expression f)
        => Call(f, new VoidLiteral { Line = f.Line, Column = f.Column });

    private static Statement Define(string name, Expression? type, Expression value, List<string>? attrs, AstNode at)
        => new VarDefinition(name, type, value, attrs) { Line = at.Line, Column = at.Column };

    private static Statement ExprStmt(Expression e, AstNode at)
        => new ExpressionStatement(e) { Line = at.Line, Column = at.Column };

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
