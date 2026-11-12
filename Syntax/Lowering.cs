namespace Ravel;

using Ravel.Runtime;

public sealed class Lowering
{
    private string Fresh(string prefix) => TempNames.Next(prefix, _source);

    private static StringLiteral Lit(string s, AstNode at) => new(s) { Line = at.Line, Column = at.Column };

    private static Expression Concat(AstNode at, params Expression[] parts)
    {
        var e = parts[0];
        for (var i = 1; i < parts.Length; i++)
            e = new BinaryExpr(e, "+", parts[i]) { Line = at.Line, Column = at.Column };
        return e;
    }

    private static Expression TypeName(Expression type, AstNode at)
        => Call(Ident("string", at), type);

    private static Expression ValueType(Expression v, AstNode at)
        => Call(Ident("string", at), Call(Ident("typeof", at), v));

    private readonly string? _source;

    private Lowering(string? source) => _source = source;

    public static Program Apply(Program p) => new Lowering(p.Source).Run(p);

    public static Expression Apply(Expression e, string? source = null) => new Lowering(source).Lower(e);

    private Program Run(Program p) => p with { Statements = Stmts(p.Statements) };

    private List<Statement> Stmts(List<Statement> xs) => xs.SelectMany(Expand).ToList();

    private IEnumerable<Statement> Expand(Statement s) => s switch
    {
        Destructure d => Unpack(d),
        // `x : T = v` —— 注解就是一次普通的 `x : T`(和 `==` / `!=` 一个待遇),
        // 所以拆成"定义 + 断言":定义那条不带注解,后面跟一句只在检查不过时说话的守卫。
        VarDefinition { TypeAnnotation: { } want } v when !(v.Attrs?.Contains(Attr.By) ?? false)
            => Typed(v, want),
        _ => [Statement(s)],
    };

    // 定义 + 一次 `x : T`。**这里就是脱糖的全部**:引擎那边收到的只是普通定义
    // 和一条 `x : T` 表达式(判据、能不能自定义,全看 `:` 那枚成员)。
    // `x : T = _` 那一格的值是 `default T` —— `_` 的意思就是"给我 T 的空值",
    // 而"空值"只有 `default` 知道(`()` 不是空值,拿 `()` 去调 T 也不再是默认值)。
    private IEnumerable<Statement> Typed(VarDefinition v, Expression want)
    {
        // `#ignoreTypes true` 之下：注解只是摆设 —— 发定义，不发那道 `:`。
        // `_` 还按 `default T` 走（它不是检查，是"这个类型空着的时候是什么"）。
        if (v.IgnoreType)
        {
            var plain = v.Value is HoleExpr ? Call(Ident("default", (AstNode)v), Lower(want)) : Lower(v.Value);
            yield return v with { TypeAnnotation = null, Value = plain };
            yield break;
        }

        var value = v.Value is HoleExpr
            ? Call(Ident("default", (AstNode)v), Lower(want))
            : Checked(v, Lower(want));
        yield return v with { TypeAnnotation = null, Value = value };
    }

    // 值只求一次:先交给一个临时参数,在里头用 `:` 问一次,过了才交出去。
    // 这样守卫问的是**值**,不是刚定义的名——不早定义,也就不留半截、不碰核心字段。
    private Expression Checked(VarDefinition v, Expression want)
    {
        var at = (AstNode)v;
        var p = Ident("__v", at);
        var check = new BinaryExpr(p, ":", want) { Line = at.Line, Column = at.Column };
        // 不过就是**拒收**,和模式里那一格一个字不差(`|` 接得住、`try` 接不住)
        var body = Block([
            ExprStmt(If(Not(check, at), RejectType(p, want, at), at), at),
            ExprStmt(p, at),
        ], at);
        var lam = new LambdaExpr(new Parameter("__v", Ident("object", at)), body)
            { Line = at.Line, Column = at.Column, Sugar = true };
        return Call(lam, Lower(v.Value));
    }

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

        // `xs[i] = v` / `xs[i] += v`:对象和下标各求**一次**,再用 `Set` 落。
        // 走两个参数的 lambda:求值次序就是 `x`、`i`、体,天然不重不漏。
        ExpressionStatement { Expr: BinaryExpr { Left: IndexExpr ix } b }
            when b.Op is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "**="
            => ExprStmt(IndexWrite(ix, b.Op, b.Right), b),

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

        // `x ??= v` / `a.b ??= v` 就是 `x = (x ?? v)`:读同一个点、写同一个点,
        // 不空时写回的就是原值(等于没动),空了才写 v —— 没有单独的原语。
        BinaryExpr { Op: "??=" } b => CoalesceAssign(b),

        // ── `do { … }` ──
        DoExpr d => Do(d),

        // ── 插值字符串 ──
        // `f"共 {n} 件"` 就是 `"" + "共 " + string(n) + " 件"`,一句不多、一句不少。
        // 解析器只把段摆好(字面量 / 表达式),拼串在这儿 —— 之后它和手写的 `+` 链
        // 走同一条路(下面那个 `BinaryExpr` 分支),求值器根本不知道有过 f-string。
        FStringExpr f => FString(f),

        // ── `x++` / `x--` ──
        // 折出来的就是一条 `BinaryExpr(x, "+=", 1)`,和手写的 `x += 1` 走**同一条路**
        // (变量一条、字段一条,见 `Interpreter.Binary` 的 StepCompoundAssign*)——
        // 所以 `by` 属性那种槽、只读字段那些规矩,一个都不用在这儿重说一遍。
        // **不交回值**(`y := x++` 是语法错误),所以解析器只在**语句位置**认它。
        UnaryExpr { Op: "++" or "--" } u => IncDec(u),

        // ── 二元运算符 → 成员调用 ──
        // `1 + 2` 就是 `1.+ 2`。运算符本来就住在左值的成员表里(`BindOperator` 找的正是
        // `left.MemberScope.LookupField(op)`,`BoxedValue.GetMember` 找的是同一格)。在这一层
        // 折成**成员访问 + 调用**,求值器就只剩一条路:两种写法同一个节点。
        //
        // **不折的那些**不是"成员表里的一格":`&&`/`||`(短路)、`:`(类型判断)、`|`(交替与位或
        // 两种身份)、`??`/`??=`(惰性,上面折过)、赋值一族(`=`/`:=`/`+=`…)——它们是求值器的
        // 控制动作。`<:`/`:>` 同理(类型之间比,不比值)。
        BinaryExpr b when IsMemberOperator(b.Op) => MemberCall(b),

        // ── 别的只是"往下走、原样重建" ──
        // `xs[i] = v` 在**表达式**位置:和语句位置走同一个写回帮手(值是写进去的那个)。
        BinaryExpr { Left: IndexExpr ix } b
            when b.Op is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "**="
            => IndexWrite(ix, b.Op, b.Right),
        BinaryExpr b => b with { Left = Lower(b.Left), Right = Lower(b.Right) },
        CallExpr c => c with { Function = Lower(c.Function), Argument = Lower(c.Argument) },
        MemberAccess m => m with { Object = Lower(m.Object) },
        // 读:`xs[i]` 是 `xs.At i`;右边写的是**区间字面量**就是 `xs.Slice 1..3`)
        // (`At` 只认单个下标 —— 切一段是另一条成员,不靠运行时看参数类型分派)
        IndexExpr ix => Call(
            Member(Lower(ix.Object), ix.Index is RangeExpr ? "Slice" : "At"),
            Lower(ix.Index)),
        UnaryExpr u => u with { Operand = Lower(u.Operand) },
        PipeExpr p => p with { Left = Lower(p.Left), Right = Lower(p.Right) },
        SlotExpr sl => sl with { Path = Lower(sl.Path) },
        ListLiteral l => l with { Elements = l.Elements.Select(Lower).ToList() },
        SetLiteral s => s with { Elements = s.Elements.Select(Lower).ToList() },
        // 两头都可能省了(`[1..]` / `[..1]`),省掉的那头是 `null`,不能往里塞
        RangeExpr r => r with
        {
            Lo = r.Lo is null ? null : Lower(r.Lo),
            Hi = r.Hi is null ? null : Lower(r.Hi),
        },
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

    private static bool IsMemberOperator(string op) => op is
        "+" or "-" or "*" or "/" or "%" or "**"
        or "==" or "!=" or "<" or ">" or "<=" or ">="
        or "<<" or ">>" or "<<<" or ">>>" or "&" or "^";

    private Expression MemberCall(BinaryExpr b)
        => new CallExpr(
               new MemberAccess(Lower(b.Left), b.Op) { Line = b.Line, Column = b.Column },
               Lower(b.Right))
           { Line = b.Line, Column = b.Column };

    private LambdaExpr Predicate(BinaryExpr b)
    {
        var f = Lower(b.Left);
        var g = Lower(b.Right);
        var name = Fresh("_" + b.Op);
        var at = Ident(name, b);

        var both = new BinaryExpr(Call(f, at), b.Op == "or" ? "||" : "&&", Call(g, at))
            { Line = b.Line, Column = b.Column };
        var body = new BlockExpr([new ExpressionStatement(both) { Line = b.Line, Column = b.Column }])
            { Line = b.Line, Column = b.Column };

        return new LambdaExpr(new Parameter(name, Ident("object", b)), body)
            { Line = b.Line, Column = b.Column, Sugar = true };
    }

    private Expression Coalesce(BinaryExpr b)
        => Call(Call(Ident("NilOr", b), Thunk(Lower(b.Left), b)), Thunk(Lower(b.Right), b));

    private Expression CoalesceAssign(BinaryExpr b)
    {
        var fallback = Thunk(Lower(b.Right), b);

        // 读同一个点、写同一个点:`x = (x ?? v)`。
        Expression Through(Expression target)
            => new BinaryExpr(target, "=",
                   Call(Call(Ident("NilOr", b), Thunk(target, b)), fallback))
               { Line = b.Line, Column = b.Column };

        switch (Lower(b.Left))
        {
            case IdentifierExpr id:
                return Through(id);

            case MemberAccess ma:
            {
                var recv = Fresh("_nil");
                var target = new MemberAccess(Ident(recv, b), ma.Member) { Line = b.Line, Column = b.Column };
                var lam = new LambdaExpr(new Parameter(recv, Ident("object", b)), AsBlock(Through(target), b))
                    { Line = b.Line, Column = b.Column, Sugar = true };
                return Call(lam, Lower(ma.Object));
            }

            default:
                // 解析器挡过一道了,按理到不了这儿;真到了说明它那条件松了
                throw Error(b, "'??=' 的左边得是变量或成员（就像 '=' 那样）");
        }
    }

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

        // 折出来的那段如果还是个**块**,它不是"值"而是一段语句:直接交出去,求值器会把它
        // 当成一枚 Block(第一类值)交回来 —— `do { y := 10; Some 2 }` 于是得到 `<block>`,
        // 连 `do { 1 }` 都是。块里得有人去**跑**它:包一层零参 lambda 当场调用。
        // (有 `:<` 时折出来的是 `m.Bind …` 那种调用,本来就已经跑过了,不用套。)
        return rest is BlockExpr block
            ? Call0(new LambdaExpr(new Parameter("_", Ident("void", d)), block) { Line = d.Line, Column = d.Column, Sugar = true })
            : rest;
    }

    private Expression Prepend(Statement s, Expression rest, AstNode at)
        => rest is BlockExpr b
            ? b with { Statements = [s, .. b.Statements] }
            : Block([s, new ExpressionStatement(rest) { Line = rest.Line, Column = rest.Column }], at);

    private Expression Lambda(LambdaExpr l)
    {
        // 参数的类型是**表达式**(`(x: 某个类型表达式) => …`),所以也得走一趟。
        var body = (BlockExpr)Lower(l.Body);
        // 引擎绑参数那一刻**不判类型** —— 判断是体里那条 `x : T`(见下)。
        // `Pattern` 留着:签名要照着印回人写的样子。
        var param = l.Param with { Type = Ident("object", l) };
        var isBy = l.Param.Attrs?.Contains(Attr.By) ?? false;

        // `#ignoreTypes true` 之下：注解不判（`()` 那格也不判），但模式照拆
        if (l.Param.IgnoreType)
        {
            if (l.Param.Pattern is { } ip)
            {
                var ib = new List<Statement>();
                Bind(ip, Ident(l.Param.Name, l), l.Param.Attrs, ib, outerDone: false,
                     who: AstPrinter.Describe(ip));
                body = body with { Statements = [.. ib, .. body.Statements] };
            }

            return l with { Param = param with { Type = Lower(l.Param.Type) }, Body = body };
        }

        // `()` 的参数只有一个取值 —— 判据是 `__p : void`,别的照 `()` 拆(什么也不拆)。
        if (l.Param.Pattern is { } unit && AstPrinter.IsUnitPattern(unit))
        {
            var u = Ident(l.Param.Name, l);
            var unitCheck = CheckParam(u, "()", unit, Ident("void", l), l);
            return l with
            {
                Param = param with { Type = Lower(l.Param.Type) },
                Body = body with { Statements = [unitCheck, .. body.Statements] },
            };
        }

        if (l.Param.Pattern is { } p)
        {
            // **修饰符再发一份给模式** —— 模式拆出来的那些名字才是用户看得见、写得动的。
            // 检查由 `Bind` 自己发(它会为每格带类型的名字生成 `source : want | reject`)。
            var binds = new List<Statement>();
            Bind(p, Ident(l.Param.Name, l), l.Param.Attrs, binds, outerDone: false,
                 who: AstPrinter.Describe(p));
            body = body with { Statements = [.. binds, .. body.Statements] };
            return l with { Param = param, Body = body };
        }

        // `()` 的参数就是 `void` —— 它只有一个值,没什么可判的。
        if (isBy || l.Param.Type is IdentifierExpr { Name: "object" or "void" })
            return l with { Param = param with { Type = Lower(l.Param.Type) }, Body = body };

        // **参数上的注解也只是一次 `x : T`** —— 和变量定义那条路一个待遇。
        // 实参先落在合成名 `__p` 上,体里先判、过了再把用户那个名字绑上去:
        // 判定发生在 `x` 绑上之前,所以 `(list: list) => …` 右边那个 `list`
        // 解析到的还是外层那个类型,不会被实参遮蔽。
        var fresh = Fresh("__p");
        var typeRef = Lower(l.Param.Type);
        var check = CheckParam(Ident(fresh, l), l.Param.Name, null, typeRef, l);
        var define = new VarDefinition(l.Param.Name, null, Ident(fresh, l), l.Param.Attrs)
        {
            Line = l.Line, Column = l.Column, Synthetic = true,
        };
        body = body with { Statements = [check, define, .. body.Statements] };
        // 签名靠 `Pattern` 印回人写的样子(`(x: int) => …`),而不是那个合成名。
        var shown = new NamePattern(l.Param.Name)
        {
            Type = l.Param.Type, Line = l.Line, Column = l.Column,
        };
        return l with { Param = param with { Name = fresh, Pattern = shown }, Body = body };
    }

    // 解构**赋值**(`[a b] = e`)期间为 true:里面的名字用 `=` 落,不许带修饰符。
    private bool _assignMode;

    private List<Statement> Unpack(Destructure d)
    {
        var prevAssign = _assignMode;
        _assignMode = d.Assign;
        try
        {
            return UnpackCore(d);
        }
        finally
        {
            _assignMode = prevAssign;
        }
    }

    private List<Statement> UnpackCore(Destructure d)
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
            var t = Fresh("__d");
            outs.Add(Define(t, null, value, TempAttrs(attrs), d));
            source = Ident(t, d);
        }

        Bind(pattern, source, attrs, outs);
        return outs;
    }

    // `参数 'n' 需要 Integer，得到 String` —— 判据就是 `:` 本身(`n : Integer`),
    // 不过就拒收:谁摆着 `reject` 谁接(`|` 的每次调用会摆一枚),没人摆就是普通错误。
    private Statement CheckParam(Expression value, string shownName, Pattern? pat, Expression typeRef, AstNode at)
    {
        var check = new BinaryExpr(value, ":", typeRef) { Line = at.Line, Column = at.Column };
        // 模式参数的名字是合成的(`__p…`),印出来等于什么都没说 —— 印模式本人。
        var what = pat is { } p
            ? Lit($"参数 {AstPrinter.Describe(p)} 需要 ", at)
            : Lit($"参数 '{shownName}' 需要 ", at);
        var msg = Concat(at, what, TypeName(typeRef, at), Lit("，得到 ", at), ValueType(value, at));
        return new ExpressionStatement(If(Not(check, at), Reject(msg, at), at))
        {
            Line = at.Line, Column = at.Column, Synthetic = true,
        };
    }

    private void Bind(Pattern p, Expression source, List<string>? attrs, List<Statement> outs,
                      bool outerDone = false, string? who = null)
    {
        var at = p;

        // **这一格自己写的那份修饰符**(`[private x]` / `{"k" -> readonly v}`)和外层传下来的
        // 那份**叠起来** —— 从外到里按写的次序摞:`(private [readonly a b])` 里 `a` 拿到
        // `private readonly`,而 `b` 只有 `private`。下面**每一处**都用 `eff`,不再碰 `attrs` ——
        // 中间量(`__g` / `__d`)也跟着走,和原来 `attrs` 的待遇一样(反正那些名字用户写不出来)。
        var eff = p.Attrs is null ? attrs
                : attrs is null ? p.Attrs
                : [.. attrs, .. p.Attrs];

        // **这一格自己带了类型**(`[x: int]` / `{n: int}` / `[[a b]: list]`):当场验,不成**拒收**。
        //
        // 为什么不是"给它挂个带注解的定义(`x : int = v`)让引擎去验":那条路发的是**注解**的
        // 说法,而模式说的是**形状**;这里统一走拒收(`reject`),和别的形状检查一条路,
        // `|` 接得住 —— 参数模式当"分派头一格"就靠这条。
        //
        // 判据用的是 `:`(不是注解那套"能转就转"):模式说的是**形状**,`[a: real] := [1]`
        // 该说"不是 real",而不是悄悄转成 1.0。
        // 参数模式这一格:要求在**体里**判(`outerDone` 只用来避免同一条要求发两遍),
        // 判不过就拒收,`|` 接得住 —— 参数模式当"分派头一格"就靠这条。
        var ownType = p.Type;
        if (p.Type is { } want && !p.IgnoreType)
        {
            if (!outerDone)
            {
                var isType = new BinaryExpr(source, ":", Lower(want)) { Line = at.Line, Column = at.Column };
                var msg = Concat(at, Lit(who is null ? "这一格要的是 " : $"参数 {who} 需要 ", at),
                                     TypeName(Lower(want), at), Lit("，得到 ", at), ValueType(source, at));
                // 脱糖造出来的那道 —— 求值照跑,印出来时跳过(它是"体里的检查",不是体)
                outs.Add(new ExpressionStatement(If(Not(isType, at), Reject(msg, at), at))
                {
                    Line = at.Line, Column = at.Column, Synthetic = true,
                });
            }
            p = p with { Type = null };                  // 验过了(或提上去了),下面按形状拆
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

            // `( … )`:**对象自己** —— 不换源、也不下沉,接着按**同一份 `source`** 拆里面那一格。
            // 所以 `(p := [a b])` 里 `p` 拿到的就是整块、`[a b]` 拆的也是同一个值。
            // (外层这一个自己的 `Type` / `When` 在上面那两段里已经验过了 —— 那正是"对整块说话"。)
            case WholePattern w:
                // 提上去的要是**里面那格**的要求(外层自己没要求),就传下去让它别再判一道;
                // 外层**自己**有要求的话,里面那格的照旧得判(`(x: int) : string` 两个都算数)。
                Bind(w.Inner, source, eff, outs, outerDone && ownType is null, who);
                return;

            // **字面量那一格**:先过那一族、再比相等。两道不是一道 —— 见 `LiteralPattern`。
            case LiteralPattern lit:
                // 域那道提到签名上去了(见 `Lambda`)就不在这儿再判一遍；
                // `#ignoreTypes true` 之下连"是不是那一族"也不问，只剩相等。
                if (!outerDone && !lit.IgnoreType)
                {
                    var sameKind = new BinaryExpr(source, ":", Lower(lit.Domain))
                        { Line = at.Line, Column = at.Column };
                    var head = who is null
                        ? Concat(at, Lit("这一格要的是 ", at), Lit(lit.Text, at))
                        : Concat(at, Lit($"参数 {who} 需要 ", at), TypeName(Lower(lit.Domain), at));
                    var msg = who is null
                        ? Concat(at, head, Lit("，得到 ", at), Call(Ident("string", at), source),
                                 Lit("（", at), ValueType(source, at), Lit("）", at))
                        : Concat(at, head, Lit("，得到 ", at), ValueType(source, at));
                    outs.Add(ExprStmt(If(Not(sameKind, at), Reject(msg, at), at), at));
                }

                // **`void` 只有一个值。** 域验过了(那一格要的是 `()`,而 `()` 是 `void` 唯一
                // 那个值),`== ()` 就是恒真,省掉。于是 `() => …` 体里**一句检查都不剩** ——
                // 和它从前那条"空参数表"的糖印出来一模一样。
                if (outerDone && lit.Value is VoidLiteral) return;

                var what = Concat(at, Lit("这一格要的是 ", at), Lit(lit.Text, at));
                var got = Call(Ident("string", at), source);
                var eq = new BinaryExpr(source, "==", Lower(lit.Value))
                    { Line = at.Line, Column = at.Column };
                outs.Add(ExprStmt(If(Not(eq, at),
                    Reject(Concat(at, what, Lit("，得到 ", at), got), at), at), at));
                return;

            case NamePattern n:
                outs.Add(BindName(n.Name, source, eff, at));
                CheckGuard(n, n.Name, Ident(n.Name, at), outs);
                // **值和它的拆法都要**:绑完名字,再拿**这个名字**当源按子模式拆一遍。
                // 用名字而不是重读 `source` —— 那边可能是一枚游标或一次调用,重读要出事。
                if (n.Sub is { } sub) Bind(sub, Ident(n.Name, at), eff, outs, who: who);
                return;

            // **位置那格**(`[l[2] l[1]] = xs`):赋值才有意义 —— 定义只给名字。
            case PlacePattern pp:
                outs.Add(BindPlace(pp.Place, source, at));
                return;

            case RestPattern r:
                // 剩下的 = 同一枚游标继续走到底。包成 `Generator` 才是个 `IEnumerable`
                // (`y` 是"往外送一个"的那个函数,和 `lib/generator.rav` 里各处一个写法)。
                var y = Fresh("_y");
                var yid = Ident(y, at);
                var drain = Call(Ident("while", at),
                    Block([ExprStmt(Call0(Member(source, "MoveNext")), at)], at));
                drain = Call(drain, Block([ExprStmt(Call(yid, Member(source, "Current")), at)], at));
                var gen = new LambdaExpr(new Parameter(y, Ident("function", at)), AsBlock(drain, at))
                    { Line = at.Line, Column = at.Column, Sugar = true };
                outs.Add(BindName(r.Name, Call(Ident("Generator", at), gen), eff, at));
                CheckGuard(r, r.Name, Ident(r.Name, at), outs);
                return;

            case MemberPattern m:
                foreach (var item in m.Names)
                {
                    // 取哪个成员:`{myX = x}` 里是 `x`,不带 `=` 的就是同名
                    var mem = item.Member ?? item.Name;
                    // 成员不在这一份上 —— 也是"形状对不上",不是引擎的硬错
                    outs.Add(ExprStmt(If(
                        Not(Call(Member(Call0(Member(source, "Fields")), "Contains"), Str(mem)), at),
                        Reject(Str($"要求成员 '{mem}'，可这一份上没有"), at), at), at));
                    Bind(item, Member(source, mem), eff, outs);   // 同上:走那道口子
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
                    // 只接拒收)。判据就是字典自己那条 —— **按值比的那些**,现在是接口
                    // `IValue`(`lib/values.rav`;见 `BuiltinClasses.CanBeKey`,两处要一起改)。
                    var keyable =
                        new BinaryExpr(
                            new BinaryExpr(
                                new BinaryExpr(key, ":", Ident("IValue", at)) { Line = at.Line, Column = at.Column },
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
                    Bind(e.Sub, Call(Member(source, "Get"), key), eff, outs);
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

                var g = Fresh("__g");
                var gid = Ident(g, at);
                outs.Add(Define(g, null, Call0(Member(source, "GetEnumerator")), TempAttrs(eff), at));

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
                    if (part is RestPattern) { Bind(part, gid, eff, outs); continue; }

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
                            Bind(n, Member(gid, "Current"), eff, outs);
                            break;
                        case PlacePattern pp:
                            // 位置那格不用中间量:游标上这一格直接写过去。
                            BindPlace(pp.Place, Member(gid, "Current"), at);
                            outs.Add(BindPlace(pp.Place, Member(gid, "Current"), at));
                            break;
                        default:
                        {
                            // 嵌套:先落到一个中间量上,再拿它当下一次解构的源
                            var t = Fresh("__d");
                            outs.Add(Define(t, null, Member(gid, "Current"), TempAttrs(eff), at));
                            Bind(part, Ident(t, at), eff, outs);
                            break;
                        }
                    }
                }

                return;
            }
        }

        throw Error(at, "Lowering 没认这个模式");
    }

    private static UnaryExpr Not(Expression operand, AstNode at)
        => new("!", operand) { Line = at.Line, Column = at.Column };

    private void CheckGuard(Pattern p, string name, Expression bound, List<Statement> outs)
    {
        if (p.Guard is not { } guard) return;
        var at = p;
        outs.Add(ExprStmt(If(Not(guard, at),
            Reject(Lit($"'{name}' 这一格的条件没过 —— 这一支不收这个值", at), at), at), at));
    }

    private Expression If(Expression cond, Expression then, AstNode at)
        => Call(Call(cond, AsBlock(then, at)), AsBlock(Num("0", at), at));

    // 拒收 = 调 `reject`:谁接由在场的 `reject` 决定 —— `|` 的每次调用会摆一枚,
    // 没人摆就是普通错误(那条 `reject` 自己把值交给 `throw`)。
    private Expression RejectType(Expression value, Expression type, AstNode at)
        => Call(Ident("reject", at), Call(Call(Member(Ident("System", at), "RejectType"), value), type));

    private Expression Reject(Expression message, AstNode at)
        => Call(Ident("reject", at), Call(Ident("TypeError", at), message));

    private static StringLiteral Str(string v) => new(v);

    private static NumberLiteral Num(string v, AstNode at)
        => new(v) { Line = at.Line, Column = at.Column };

    // ── 造节点的几个小帮手(解构这摊用得多,单拎出来)──

    private MemberAccess Member(Expression obj, string name)
        => new(obj, name) { Line = obj.Line, Column = obj.Column };

    private static List<string>? TempAttrs(List<string>? attrs)
    {
        if (attrs is null || !attrs.Contains(Attr.By)) return attrs;
        var rest = attrs.Where(a => a != Attr.By).ToList();
        // 摘空了要给 `null`,不是空表 —— 转储里两者印出来不一样(`Attrs:` 空着一行),
        // `ast` 的往返自检当场逮到(实测:原 `Attrs:` / 新没有这行)。
        return rest.Count > 0 ? rest : null;
    }

    private static CallExpr Call0(Expression f)
        => Call(f, new VoidLiteral { Line = f.Line, Column = f.Column });

    // 一个名字怎么落地:定义(`:=`)还是赋值(`=`)。解构赋值里那几格走后者。
    // 一个**位置**怎么落地:下标走 `Set`,成员走槽赋值。`:=` 底下没有这条路。
    private Statement BindPlace(Expression place, Expression value, AstNode at)
    {
        if (!_assignMode)
            throw Error(at, "这一格是个位置（`l[2]` / `a.b`）—— 定义只给名字，写回要用 '='");

        return place switch
        {
            IndexExpr ix => ExprStmt(IndexWrite(ix, "=", value), at),
            // 成员那格:`p.x = v` 本来就是一条赋值表达式,照原样发出去。
            MemberAccess ma => ExprStmt(
                new BinaryExpr(Lower(ma), "=", value) { Line = at.Line, Column = at.Column }, at),
            _ => throw Error(at, "这一格要么是名字、字面量，要么是一个位置（`l[2]` / `a.b`）"),
        };
    }

    private Statement BindName(string name, Expression value, List<string>? attrs, AstNode at)
    {
        if (!_assignMode) return Define(name, null, value, attrs, at);
        if (attrs is [.., var bad])
            throw Error(at, $"解构赋值里不能加修饰符 '{bad}' —— 修饰符只属于定义（要定义就写 ':='）");
        return new Assignment(name, value) { Line = at.Line, Column = at.Column };
    }

    private static Statement Define(string name, Expression? type, Expression value, List<string>? attrs, AstNode at)
        => attrs is [Attr.By] && type is null
            ? new SlotAssign(Ident(name, at), value, Define: true) { Line = at.Line, Column = at.Column }
            : new VarDefinition(name, type, value, attrs) { Line = at.Line, Column = at.Column };

    private Expression IndexWrite(IndexExpr ix, string op, Expression value)
    {
        var o = Fresh("__o");
        var i = Fresh("__i");
        var oid = Ident(o, ix);
        var iid = Ident(i, ix);
        var slot = Ident(Fresh("__v"), ix);

        Expression newValue = op == "="
            ? value
            : new BinaryExpr(Call(Member(oid, "At"), iid), op[..^1], value) { Line = ix.Line, Column = ix.Column };

        // 体:`__v := <新值>; (__o.Set __i) __v; __v;`
        var body = new List<Statement>
        {
            Define(slot.Name, null, newValue, null, ix),
            ExprStmt(Call(Call(Member(oid, "Set"), iid), slot), ix),
            ExprStmt(slot, ix),
        };

        var inner = new LambdaExpr(new Parameter(i, Ident("object", ix)), Block(body, ix))
            { Line = ix.Line, Column = ix.Column };
        var outer = new LambdaExpr(new Parameter(o, Ident("object", ix)), Block([ExprStmt(inner, ix)], ix))
            { Line = ix.Line, Column = ix.Column };
        return Call(Call(outer, Lower(ix.Object)), Lower(ix.Index));
    }

    private static Statement ExprStmt(Expression e, AstNode at)
        => new ExpressionStatement(e) { Line = at.Line, Column = at.Column };

    private BinaryExpr IncDec(UnaryExpr u)
        => new(Lower(u.Operand), u.Op == "++" ? "+=" : "-=",
               new NumberLiteral("1") { Line = u.Line, Column = u.Column })
        { Line = u.Line, Column = u.Column };

    private LambdaExpr Thunk(Expression e, AstNode at)
        => new(new Parameter("_", new IdentifierExpr("void") { Line = at.Line, Column = at.Column }), AsBlock(e, at))
            { Line = at.Line, Column = at.Column, Sugar = true };

    private BlockExpr AsBlock(Expression e, AstNode at)
        => e is BlockExpr b ? b : Block([new ExpressionStatement(e) { Line = e.Line, Column = e.Column }], at);

    private BlockExpr Block(List<Statement> stmts, AstNode at)
        => new(stmts) { Line = at.Line, Column = at.Column, Source = _source };

    private static CallExpr Call(Expression f, Expression arg)
        => new(f, arg) { Line = f.Line, Column = f.Column };

    // `f"共 {n} 件"` → `"" + "共 " + string(n) + " 件"`。**整门语言只有这一处**
    // 认识 f-string:上面那个 switch 把节点换成一棵 `+` 树,之后全是老路
    // (拼完再 `Lower` 一遍,`+` 就折成成员调用了)。
    private Expression FString(FStringExpr f)
    {
        Expression chain = Lit("", f);
        foreach (var part in f.Parts)
        {
            Expression piece = part.Interp is { } x
                ? Call(Ident("string", (AstNode)x), x)
                : Lit(part.Text, f);
            chain = new BinaryExpr(chain, "+", piece) { Line = f.Line, Column = f.Column };
        }
        return Lower(chain);
    }

    private static IdentifierExpr Ident(string name, AstNode at)
        => new(name) { Line = at.Line, Column = at.Column };

    private SyntaxException Error(AstNode at, string message)
        => new(message, new SourceSpot(_source, at.Line, at.Column));
}
