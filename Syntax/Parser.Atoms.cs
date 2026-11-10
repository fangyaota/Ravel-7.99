namespace Ravel;

using System.Linq;
using Ravel.Runtime;

public partial class Parser
{
    // ========================================
    //  Primary
    // ========================================

    private Expression ParsePrimary(bool bareLambda = true)
    {
        // `do { … }` —— Monad 的串联糖(见 ParseDo)
        if (Check(TokenType.Identifier) && Peek().Lexeme == "do" && NextType() == TokenType.LeftBrace)
            return Nested(ParseDo);

        // `by a` / `by a.x` —— 取槽里的那份 property 本身(不过 getter;见 SlotExpr)。
        // 只在**表达式**位置认:语句开头的 `by` 是修饰符(声明或换槽那条路)。
        if (Check(TokenType.Identifier) && Peek().Lexeme == "by" && NextType() == TokenType.Identifier)
        {
            _pos++;                                     // by
            return new SlotExpr(ParseMemberChain(ParsePrimary())) { Line = Previous().Line, Column = Previous().Column };
        }

        // 运算符节 `+.2` / `is.int` —— 左操作数留空,等价于 `_ + 2` / `_ is int`
        // (脱糖成同一个 lambda)。右操作数只吃一个 primary(含成员访问),
        // 所以 `+.2 + 3` 是 `(+.2) + 3`。
        // `@` 落到"要表达式"的位置 = 两种情形之一:
        //   * 想给循环起名字 —— 那是**语句开头**的写法(`@outer while { … }`),得开 `--more-control-flow`;
        //   * 还是从前那个"把左边封口"的运算符 —— 它早改成 `|>` 了。
        // 直说清楚,比"需要表达式，但得到 '@'"有用。
        if (Check(TokenType.At))
            throw ParseError("'@' 是**标签**的前缀，写在语句开头（`@outer while { … }`，要开 `--more-control-flow`）；"
                           + "从前那个封口的运算符已经改成 '|>' 了");

        if (IsSectionStart())
        {
            var sym = Peek();
            _pos += 2;
            _holeCount++;
            var hole = new HoleExpr(_holeCount - 1) { Line = sym.Line, Column = sym.Column };
            var body = new BinaryExpr(hole, sym.Lexeme, ParseCall(allowCall: false))
                { Line = sym.Line, Column = sym.Column };
            // **就地脱糖成 lambda**,和括号那趟一个道理(见文件末尾):节是闭包边界,
            // 它那个洞不该让外层那趟再包一次 —— 否则 `isnot.string "a"` 会变成
            // `(_0) => { (_0 isnot string) "a"; }`(把实参也吞进体里),而不是"应用节"。
            return DesugarHoles(body);
        }

        // `~x` —— **`{x;}` 的语法糖**。**只吃一个原子**,吃完交回一个**块**:
        //
        //     ~5 + 3        ≡  {5;} + 3      （`+` 落在块的值上,不是块里）
        //     x => ~x       ≡  x => {x;}     （体就是那个块,所以**封口** —— 见 ParseUserLambdaBody）
        //
        // 吃的是**一个操作数**:`ParsePrimary` 加它那串 `.成员` / `?.`(那是**一条链**,
        // `ParseMemberChain` 就是干这个的)—— 所以 `~x.y?.z` 是 `{x.y?.z;}`。
        //
        // 到成员链为止,**并列的实参不吃**:`~f 1` 是 `{f;} 1`(调用是**应用**,不是后缀)。
        // 要更大的范围自己加括号:`~(f 1)`。
        //
        // 和一元的 `!` / `-` 不同层:那两个在 `ParseCall`,所以 `-~x` 读得出来、
        // `~-x` 读不出来(要写 `~(-x)`)—— 一行一格的糖,不值得再多一层。
        if (Match(TokenType.Tilde))
        {
            BumpExprDepth();                        // `~ ~ ~x`:一层一个 `{x;}`
            var tilde = Previous();
            var atom = ParseMemberChain(ParsePrimary(bareLambda: false), allowCall: false);
            return Block([new ExpressionStatement(atom) { Line = tilde.Line, Column = tilde.Column }], tilde);
        }

        // **裸模式参数**:`1 => …` / `"yes" => …` / `[x y] => …` / `{"a" -> v} => …` ——
        // 单参 lambda 那一格**直接写模式**,不套括号。就是一格模式,和 `(模式) => …` 等价。
        //
        // `() => …` **故意不在这条里**:那是**空参数表**(老写法,一个字不能动)。
        // 想写"参数必须是单位那个值"就套一层 —— `(()) => …`。
        // `true` / `false` 特意收进来:在这一格它们是**字面量**(和 `(true) => …` 一个意思),
        // 不是"叫 `true` 的参数"。光杆名字仍旧走下面那条 `ParseBareLambda`。
        if (bareLambda && BarePatternLambdaAhead())
            return ParseBarePatternLambda();

        if (Match(TokenType.Number))
        {
            var lexeme = Previous().Lexeme;
            var isFloat = lexeme.Contains('.');
            return new NumberLiteral(lexeme, isFloat)
                { Line = Previous().Line, Column = Previous().Column };
        }

        if (Match(TokenType.String))
        {
            var str = Previous();
            return str.Parts is { } parts
                ? Interpolated(str, parts)
                : new StringLiteral(str.Lexeme) { Line = str.Line, Column = str.Column };
        }

        if (Match(TokenType.Char))
            return new CharLiteral(Previous().Lexeme[0])
                { Line = Previous().Line, Column = Previous().Column };

        // `x => 体` —— **单参 lambda 不写括号**。见 ParseBareLambda 里那两条"故意不收"。
        // 和上面那道一样要过 `bareLambda` 口子:模式里取字面量那一格时 `true` 是**字面量**,
        // 不关掉的话它会被当成"叫 `true` 的参数",顺手把 `=>` 和整个体都吃进去。
        if (bareLambda && Check(TokenType.Identifier) && CheckNext(TokenType.Arrow))
            return ParseBareLambda();

        if (Match(TokenType.Identifier))
        {
            var lexeme = Previous().Lexeme;
            if (lexeme == "_")
            {
                _holeCount++;
                return new HoleExpr(_holeCount - 1) { Line = Previous().Line, Column = Previous().Column };
            }

            return new IdentifierExpr(lexeme)
                { Line = Previous().Line, Column = Previous().Column };
        }

        if (Match(TokenType.LeftParen))
            return Nested(ParseParen);

        if (Match(TokenType.LeftBracket))
        {
            int line = Previous().Line, col = Previous().Column;
            return Nested(() =>
            {
                // `[1..3]` 是**区间**(两端都含);看不见 `..` 就照旧是一个列表字面量
                if (TryParseRange(startClosed: true) is { } rng) return rng;

                var elements = ParseSpaceSeparatedList(TokenType.RightBracket, "]");
                return new ListLiteral(elements) { Line = line, Column = col };
            });
        }

        if (Match(TokenType.LeftBrace))
            return Nested(ParseBrace);

        throw ParseError($"需要表达式，但得到{Describe(Peek())}");
    }

    private Expression ParseDo()
    {
        var at = Peek();
        _pos++;                                     // do
        Consume(TokenType.LeftBrace, "do 后面需要 '{'");
        SkipNewlines();

        var stmts = new List<Statement>();
        _doDepth++;
        try
        {
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                stmts.Add(ParseStatement());
                SkipNewlines();
            }
        }
        finally
        {
            _doDepth--;
        }

        Consume(TokenType.RightBrace, "do 块末尾需要 '}'");
        if (stmts.Count == 0) throw ParseError("do 块里得有语句");
        if (stmts[^1] is BindStatement) throw ParseError("do 块的最后一条语句要是个值（不能以 ':<' 收尾）");

        return new DoExpr(stmts) { Line = at.Line, Column = at.Column };
    }

    private bool StartsParamGroup(int off)
        => TypeAt(off) is TokenType.LeftBracket or TokenType.LeftBrace;

    private bool StartsParam()
        => Check(TokenType.Identifier) || StartsParamGroup(0) || IsLiteralStart()
        || Check(TokenType.LeftParen);

    private bool StartsBindingAt(int off) => TypeAt(off) switch
    {
        // 名字(`true` / `false` / `_` 也是 Identifier)、字面量那一族的开头、模式的三对括号、
        // 负数字面量(`-1 => …` / `[-1 v]` —— 一格**开头**的负号只可能是负号)、`..rest`
        TokenType.Identifier or TokenType.Number or TokenType.String
            or TokenType.LeftBracket or TokenType.LeftBrace or TokenType.LeftParen
            or TokenType.Minus or TokenType.DotDot => true,
        _ => false,
    };

    private List<string>? CollectModifiers()
    {
        var attrs = new List<string>();
        while (Check(TokenType.Identifier) && Attr.All.Contains(Peek().Lexeme))
        {
            var save = _pos;
            var word = Peek().Lexeme;
            _pos++;                       // 先吃下去,再往后看看有没有"绑定项"接着
            SkipNewlines();
            if (!StartsBindingAt(0)) { _pos = save; break; }
            attrs.Add(word);
        }
        return attrs.Count > 0 ? attrs : null;
    }

    private string FreshParam() => TempNames.Next("__p", source);

    private static IdentifierExpr ObjectType(Token at) => new("object") { Line = at.Line, Column = at.Column };

    private bool LooksLikeLambdaParams()
    {
        var off = 0;
        while (true)
        {
            if (StartsParamGroup(off))
            {
                // **模式参数**:一整个 `[…]` / `{…}` 算一项,而且**后面不能跟注解**
                off = SkipBalanced(off);
                if (off < 0) return false;               // 括号没闭上
            }
            else if (TypeAt(off) == TokenType.Identifier)
            {
                off++;
                if (TypeAt(off) == TokenType.Colon)
                {
                    off = SkipTypeAnnotation(off + 1);
                    if (off < 0) return false;           // 类型读不动(括号没闭上之类)
                }
            }
            else return false;

            if (TypeAt(off) == TokenType.RightParen) return TypeAt(off + 1) == TokenType.Arrow;
            // 还没到 ')' —— 后面只可能是**下一个参数**(名字或又一个模式)
        }
    }

    private bool IsLambdaAfterParen()
    {
        var off = 0;
        var depth = 1;      // **从 1 起**:那个开括号还没配上(从 0 起的话 `((x: object) => …)`
                            // 这种**分组**会在内层那个 `)` 上就判成"参数表",把括号吃错)
        while (true)
        {
            var t = TypeAt(off);
            if (t == TokenType.EndOfFile) return false;
            if (t == TokenType.LeftParen) depth++;
            else if (t == TokenType.RightParen && --depth == 0) return TypeAt(off + 1) == TokenType.Arrow;
            off++;
        }
    }

    private int SkipTypeAnnotation(int off)
    {
        if (TypeAt(off) == TokenType.LeftParen)
        {
            var depth = 0;
            while (true)
            {
                if (TypeAt(off) == TokenType.EndOfFile) return -1;      // 括号没闭上
                if (TypeAt(off) == TokenType.LeftParen) depth++;
                else if (TypeAt(off) == TokenType.RightParen && --depth == 0) return off + 1;
                off++;
            }
        }

        if (TypeAt(off) != TokenType.Identifier) return -1;
        off++;
        while (TypeAt(off) == TokenType.Dot && TypeAt(off + 1) == TokenType.Identifier) off += 2;
        return off;
    }

    private BlockExpr Block(List<Statement> stmts, Token at)
        => new(stmts) { Line = at.Line, Column = at.Column, Source = source };

    private BlockExpr ParseUserLambdaBody(string what, Token at)
    {
        _returnUsed.Add(false);
        // `=>` 后面见 `{` 就走老路(块 / 集合那条判据);别的都是**单行简写**。
        //
        // `~x` 走**块**那条:它整出来的就是个块,体到那块为止(**封口**)——
        // 于是 `x => ~x` ≡ `x => {x;}`(而不是 `x => ({x;})`,那种还能往后吃)。
        var body = Check(TokenType.LeftBrace) ? ParseMandatoryBlock(what)
                 : Check(TokenType.Tilde) ? (BlockExpr)ParsePrimary()
                 : ParseShorthandBody(at);
        var used = _returnUsed[^1];
        _returnUsed.RemoveAt(_returnUsed.Count - 1);
        if (!used) return body;

        var param = new Parameter("__return", ObjectType(at));
        var lam = new LambdaExpr(param, body) { Line = at.Line, Column = at.Column };
        var call = new CallExpr(new IdentifierExpr("callcc") { Line = at.Line, Column = at.Column }, lam)
            { Line = at.Line, Column = at.Column };
        return Block([new ExpressionStatement(call) { Line = at.Line, Column = at.Column }], at);
    }

    private BlockExpr AsBlock(Expression e, Token at)
        => e is BlockExpr b ? b : Block([new ExpressionStatement(e) { Line = e.Line, Column = e.Column }], at);

    private Expression Nested(Func<Expression> parse)
    {
        if (_depth >= MaxDepth) throw ParseError($"表达式嵌套太深（超过 {MaxDepth} 层）");

        _depth++;
        try
        {
            return parse();
        }
        finally
        {
            _depth--;
        }
    }

    // ========================================
    //  括号内容解析
    // ========================================

    private Expression ParseParen()
    {
        int line = Previous().Line, col = Previous().Column;

        if (Match(TokenType.RightParen))
        {
            // `() => …` —— **不再是"空参数表"那条糖**。`()` 在这儿就是"单位那个值"的
            // **字面量模式**,和 `(()) => …` 走**同一条路**(底下那个参数循环也是这么读它的:
            // `()` 走不到括号那条,字面量那条先接走)。于是 `()` 和 `(())` 在树上**就是一个构造**
            // —— 一个"要 `()`"的参数。
            //
            // 从前那条糖(`(_: void) => …`)和 `(())` 是"碰巧收同一批实参"的两套机制
            // (那一头是**注解检查**,这一头是**模式拒收**)—— 现在合成一套。
            // 印出来还是 `() => …`:`AstPrinter` 按模式印签名。
            if (Match(TokenType.Arrow))
            {
                var body = ParseUserLambdaBody("lambda 体", Previous());
                var voidLit = new LiteralPattern(
                    new VoidLiteral { Line = line, Column = col },
                    new IdentifierExpr("void") { Line = line, Column = col },
                    "()") { Line = line, Column = col };
                // **包一层"对象自己"**,和 `(()) => …` 走出来的形状**一模一样** ——
                // 那一条是"参数表里装着一个括号分组",分组就是这一层。两条路合流成一个构造。
                var whole = new WholePattern(voidLit) { Line = line, Column = col };
                var param = new Parameter(FreshParam(),
                    new IdentifierExpr("object") { Line = line, Column = col }, whole);
                return new LambdaExpr(param, body) { Line = line, Column = col };
            }

            // () 独立 → void 字面量
            return new VoidLiteral { Line = line, Column = col };
        }

        // 尝试 lambda:  (IDENT [: 类型] [IDENT [: 类型]]*) 或者最后一项写**守卫**
        if (LooksLikeLambdaParams() || IsLambdaAfterParen())
        {
            var @params = new List<Parameter>();
            Expression? guard = null;
            while (true)
            {
                var at = Peek();
                var saveAt = _pos;

                // **参数前面能写修饰符**(和变量定义**同一张表**:`Attr.All`)——
                // `(private x: int)` / `(readonly [a b])` / `(outdated v: int)`。
                // 判据见 `CollectModifiers`。
                var attrs = CollectModifiers();

                // **模式参数**:`([x y]) => …` / `({a b}) => …` / `(1) => …` / `(()) => …` ——
                // 整个参数按形状拆。参数本身没有名字(名字在模式里,字面量那格连名字都没有),
                // 所以取一个**合成名**;拆法归 `Lowering`。
                //
                // 字面量也算进来,于是 `(1) => … | (_) => …` 这种按**值**分派能写。
                // **`() => {…}` 不受影响** —— 空参数表在 `ParseParen` 最开头就返回了,
                // 根本走不到这儿(`()` 当字面量只在**模式里**)。
                // **不能带注解** —— 形状本身就是它对实参的要求(要更严就在体里再过一手)。
                // 加括号的那几样(`(…)`)也算一格模式 —— 括号**只是分组**,里面还是模式
                // (`(((x:(int)))) => x`)。`()` 走不到这儿:空参数表在 ParseParen 开头就返回了。
                if (Check(TokenType.LeftBracket) || Check(TokenType.LeftBrace) || IsLiteralStart()
                    || Check(TokenType.LeftParen))
                {
                    // 类型写在模式**自己那一格**上(`([x y] : list) => …` 里那个 `:` 挂最外那格),
                    // 由 `ParsePattern` 吃 —— 这儿不用另立一条。
                    @params.Add(new Parameter(FreshParam(), ObjectType(at), ParsePattern())
                        { Attrs = attrs });
                    SkipNewlines();
                    if (!StartsParam()) break;               // 到 ')' 了
                    continue;
                }

                var pName = Consume(TokenType.Identifier, "lambda 参数需要一个名字（`(x: int) => …`）");

                // **参数名不许重**:`(a a) => …` 从前不报错,后一个悄悄盖掉前一个 ——
                // 喂 `1 2` 交回 `2`,而写的人多半是手滑。这正是这门语言最恨的那种静默。
                //
                // 只在**真声明一个新参数**的两条岔路上查(下面那两条)。守卫那条**不能查**:
                // `(x: int x < 0)` 走的正是"这个名字声明过 → 只挂守卫、不再声明一个",
                // 靠"同名"这个信号办事,查了就把教程 5.5 那种写法误杀了。
                void Declare(Parameter p)
                {
                    if (@params.Any(q => q.Name == p.Name))
                        throw ParseError($"参数名 '{p.Name}' 写了两遍 —— 每个参数一个名字"
                                       + "（要**盯类型**就把整个参数括起来写 `|>` 那条："
                                       + "`((v: int) |> IsPrime)`）");
                    @params.Add(p);
                }

                if (Match(TokenType.Colon))
                {
                    Declare(new Parameter(pName.Lexeme, ParseTypeAnnotation()) { Attrs = attrs });
                }
                else if (Check(TokenType.Identifier) || Check(TokenType.RightParen) || StartsParamGroup(0))
                {
                    // **注解可省**:省了就按 `object` 收(谁都收得下)。和 `do` 的绑定、占位符消糖
                    // 一个待遇 —— 那些地方也只知道"有东西来了",标不出更细的类型。
                    // 要更细就在体里自己过一手:`n: int = x`。
                    // (后面跟 `[…]` / `{…}` 也算"这一项完了"——那是下一个模式参数。)
                    Declare(new Parameter(pName.Lexeme, ObjectType(pName)) { Attrs = attrs });
                }
                else
                {
                    // **守卫那一格不接修饰符。** 守卫是"给已经声明过的那个参数加条件",
                    // 它自己**不声明**任何东西 —— 修饰符没处落。不拦的话上面那个收集循环
                    // 会把 `private` 吃掉、这一支再退回去重读,于是参数名**静默**成了 `private`。
                    if (attrs is not null)
                        throw ParseError($"守卫上不能写修饰符 '{attrs[0]}' —— 守卫是给已经声明过的"
                                       + "那个参数加条件,它自己什么都不声明");
                    // **守卫**:这一项整个是一条表达式(`v == s` / `v |> IsPrime`)。
                    // 退回去整条重读一遍,参数名取它**最左边那个标识符**(草稿那句"只看最左边的")。
                    _pos = saveAt;
                    // **守卫不吃并列的实参** —— 和**模式**那条守卫一个规矩(见 `ParsePatternBare`):
                    // 不然 `(x < 0 y: int)` 里那个 `y: int` 会被当成 `0` 的实参**吃进去**,
                    // 于是"守卫 + 下一个参数"静默变成一个参数的 lambda(之后那个 `y: int`
                    // 轮不到解析器看,重名检查也就看不见它)。`|>` 照收 —— 它是运算符、不是并列,
                    // 而 `(v |> IsPrime)` 正靠它。
                    guard = ParsePostfixRest(ParseClimb(allowCall: false, 0),
                                             allowCall: false, allowPipe: true);
                    // **名字按 token 顺序取最左边那个标识符** —— 不走 AST:`v |> IsPrime`
                    // 在 AST 里是 `CallExpr(IsPrime, v)`(`|>` 解析期就折成调用了),
                    // 按树取"最左"会取到 IsPrime。草稿那句"只看最左边的"说的就是源码顺序。
                    var nm = FirstIdentName(saveAt, _pos)
                        ?? throw ParseError("守卫里得有个参数名 —— 它最左边那个标识符就是");
                    // **守卫里那个名字不许是已经声明过的参数**。
                    //
                    // 从前是"声明过就只挂守卫、不再声明一个":`(x: int x < 0)` 于是成了
                    // **一个**参数 + 一个守卫。它**跑得对**,可写出来就是两个 `x` ——
                    // 读的人当双参、写的人多半也是手滑(想盯哪个参数不写名字,写错了就悄悄
                    // 多出一个 `object` 参数)。参数表里一个名字出现两遍**就是两遍**,当场报。
                    //
                    // 代价说清楚:"带类型的参数 + 守卫"因此没了简写,要写就写成谓词那支
                    // (`(v |> IsPrime)` —— 守卫自己命名那个参数)或者传个闭包。
                    if (@params.Any(q => q.Name == nm))
                        throw ParseError($"参数名 '{nm}' 写了两遍 —— 每个参数一个名字"
                                       + "（要**盯类型**就把整个参数括起来写 `|>` 那条："
                                       + "`((v: int) |> IsPrime)`）");
                    @params.Add(new Parameter(nm, ObjectType(at)));
                    if (!Check(TokenType.RightParen))
                        throw ParseError("守卫要写在参数表的**最后一项** —— 它是一条表达式,"
                                       + "后面再跟东西会被当成它的实参（`(x < 0 y: int)` 里那个 `y: int` 就是）。"
                                       + "要**盯类型**就把整个参数括起来写 `|>` 那条："
                                       + "`((v: int) |> IsPrime)` —— 类型在那一格上,名字只写一遍");
                    break;
                }
                SkipNewlines();
                if (!StartsParam()) break;                    // 到 ')' 了
            }

            Consume(TokenType.RightParen, "lambda 参数后需要 ')'");
            Consume(TokenType.Arrow, "lambda 参数后需要 '=>'");
            var arrow = Previous();
            var body = ParseUserLambdaBody("lambda 体", arrow);
            if (guard is not null) body = PrependGuard(body, guard, @params[^1].Name, arrow);

            // 单参数：直接返回（兼容原有行为）
            if (@params.Count == 1)
                return new LambdaExpr(@params[0], body) { Line = line, Column = col };

            // 多参数消糖： (a:int b:string) => { body }
            // → (a:int) => { (b:string) => { body } }
            // 从最里层往外套:最内层收的就是原始 body(它本来就是块),
            // 外面几层各自再包一个「只有一条语句」的块。
            Expression result = body;
            for (int i = @params.Count - 1; i >= 0; i--)
            {
                var layer = i == @params.Count - 1
                    ? body
                    : new BlockExpr([new ExpressionStatement(result) { Line = line, Column = col }])
                        { Line = line, Column = col, Source = source };
                result = new LambdaExpr(@params[i], layer) { Line = line, Column = col };
            }

            return result;
        }

        // `(3..5)` 是**区间**(两端都不含)。排在 lambda 判定**之后**:那一段本来就不是 lambda
        // (`(1..3)` 里第一个 token 是数字,过不了 `名字 :` 那道前瞻)。
        if (TryParseRange(startClosed: false) is { } rng) return rng;

        // 否则：就是普通括号分组 — 内部允许柯里化（与顶层一致）
        // (f a b)  →  f(a)(b)
        // (a)      →  a
        var inner = ParseExpression(allowCall: true);
        Consume(TokenType.RightParen, "表达式后需要 ')'");

        // 括号里的占位符就地在括号内消糖:占位符消糖本来只作用于整条语句,
        // 那会让 `(_ + 1) 41` 变成 `(_0) => (_0 + 1) 41`(整个式子被包起来、41 没被应用)。
        // 括号界定了 section 的范围,所以在这里收口:先把 `(_ + 1)` 变成 lambda,再让 41 应用。
        return HasHoles(inner) ? DesugarHoles(inner) : inner;
    }

    private Expression ParseBrace()
    {
        int line = Previous().Line, col = Previous().Column;

        if (Match(TokenType.RightBrace))
            return new SetLiteral([]) { Line = line, Column = col };

        var (arrow, newline) = ScanBraceBody();
        if (arrow) return ParseDict(line, col);
        if (newline)
        {
            var block = ParseBlockStatements();
            return new BlockExpr(block) { Line = line, Column = col, Source = source };
        }

        return ParseSet(line, col);
    }

    private (bool Arrow, bool Newline) ScanBraceBody()
    {
        int depth = 1;
        bool arrow = false, newline = false;
        for (int i = _pos; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.EndOfFile) break;
            switch (t.Type)
            {
                case TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace:
                    depth++;
                    break;
                case TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace:
                    if (--depth == 0) return (arrow, newline);
                    break;
                case TokenType.Newline:
                    newline = true;
                    break;
                case TokenType.DictArrow when depth == 1:
                    arrow = true;
                    break;
            }
        }

        return (arrow, newline);
    }

    private Expression Interpolated(Token str, IReadOnlyList<StringPart> parts)
    {
        Expression expr = new StringLiteral("") { Line = str.Line, Column = str.Column };
        foreach (var p in parts)
        {
            Expression piece = p.IsExpr
                ? new CallExpr(
                    new IdentifierExpr("string") { Line = p.Line, Column = p.Column },
                    Parser.ParseExpressionSnippet(p.Text, source, p.Line, p.Column))
                    { Line = p.Line, Column = p.Column }
                : new StringLiteral(p.Text) { Line = p.Line, Column = p.Column };

            expr = new BinaryExpr(expr, "+", piece) { Line = p.Line, Column = p.Column };
        }

        return expr;
    }

    private Expression ParseSet(int line, int col)
    {
        var elements = new List<Expression>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            elements.Add(ParseExpression(allowCall: false));
        }

        Consume(TokenType.RightBrace, "集合元素后需要 '}'");
        return new SetLiteral(elements) { Line = line, Column = col };
    }

    private Expression ParseDict(int line, int col)
    {
        var entries = new List<DictEntry>();
        while (true)
        {
            SkipNewlines();
            if (Check(TokenType.RightBrace) || IsAtEnd()) break;

            // `{->}` —— 空字典。只认在条目之前(entries 空)的那一个,别把 `{a -> 1 ->}` 放过去。
            if (entries.Count == 0 && Check(TokenType.DictArrow))
            {
                Consume(TokenType.DictArrow, "字典键后需要 '->'");
                SkipNewlines();
                break;
            }

            var key = ParseExpression(allowCall: false);
            Consume(TokenType.DictArrow, "字典键后需要 '->'");
            var value = ParseExpression(allowCall: false);
            entries.Add(new DictEntry(key, value));
        }

        Consume(TokenType.RightBrace, "字典条目后需要 '}'");
        return new DictLiteral(entries) { Line = line, Column = col };
    }

    private bool BarePatternLambdaAhead(int off = 0)
    {
        switch (TypeAt(off))
        {
            case TokenType.Number or TokenType.String:
                return TypeAt(off + 1) == TokenType.Arrow;
            case TokenType.Minus:                                  // `-1 => …`
                return TypeAt(off + 1) == TokenType.Number && TypeAt(off + 2) == TokenType.Arrow;
            case TokenType.Identifier:
                return tokens[_pos + off].Lexeme is "true" or "false" && TypeAt(off + 1) == TokenType.Arrow;
            case TokenType.LeftBracket or TokenType.LeftBrace:
            {
                var end = SkipBalanced(off);
                return end >= 0 && TypeAt(end) == TokenType.Arrow;
            }
            default:
                return false;
        }
    }

    private Expression ParseBarePatternLambda()
    {
        var at = Peek();
        var pattern = ParsePattern();
        Consume(TokenType.Arrow, "模式后需要 '=>'");
        var arrow = Previous();
        var body = ParseUserLambdaBody("lambda 体", arrow);
        return new LambdaExpr(new Parameter(FreshParam(), ObjectType(at), pattern), body)
            { Line = at.Line, Column = at.Column };
    }

    private Expression ParseBareLambda()
    {
        var name = Peek();
        // `_ => 体` —— **通配参数**:收什么都行,不绑东西(和 `(_) => 体` 一模一样)。
        //
        // 从前这儿是拦住的(「`_` 不能当参数名」),因为怕"悄悄绑出一个叫 `_` 的变量"。
        // 但**读不到它**:体里那个 `_` 一律当**洞**消糖(见 `ParsePrimary` 里 `_` 那一支),
        // 所以绑出来也只是个占着名字的槽 —— 不会和洞混起来。既然要的是"这一支什么都收",
        // 那 `_` 正是该写的记号。
        if (_pos > 0 && tokens[_pos - 1].Type == TokenType.Colon)
            throw ParseError("不加括号的 lambda 不能带注解 —— `x: int => …` 里那个 `x: int` 读成了类型判断。"
                           + "要带类型就加括号：`(x: int) => …`");
        _pos++;
        Consume(TokenType.Arrow, "lambda 参数后需要 '=>'");
        var body = ParseUserLambdaBody("lambda 体", Previous());
        return new LambdaExpr(new Parameter(name.Lexeme, ObjectType(name)), body)
            { Line = name.Line, Column = name.Column };
    }

    private string? FirstIdentName(int from, int to)
    {
        for (var i = from; i < to && i < tokens.Count; i++)
            if (tokens[i].Type == TokenType.Identifier)
                return tokens[i].Lexeme;
        return null;
    }

    private BlockExpr PrependGuard(BlockExpr body, Expression guard, string param, Token at)
    {
        // 处理体:`(e: Exception) => { if { e: TypeError; } { false; } { System.Unhandled e; } }`
        //
        // **只把 TypeError 当"不收"**,别的错照旧往上走 —— 守卫里把名字打错(`IsPrime` 没定义)
        // 是**笔误**,不该静默变成"这一支不匹配"。重抛走 `System.Unhandled`:它把引擎这次
        // 交出去的那枚 C# 异常**原样**再抛出(位置和调用栈都还在),所以报出来的是原来那句。
        // (直接用 handler 参数类型 `(e: TypeError)` 收最省事,但非 TypeError 会让库的
        //  handler 自己报「参数 'e' 需要 TypeError，得到 NameError」—— 指进库内部,难看。)
        var isTypeError = new BinaryExpr(Ident("e", at), ":", Ident("TypeError", at))
            { Line = at.Line, Column = at.Column };
        var rethrow = Call(new MemberAccess(Ident("System", at), "Unhandled")
            { Line = at.Line, Column = at.Column }, Ident("e", at), at);
        var handlerBody = Call(Call(isTypeError, AsBlock(Ident("false", at), at), at),
                               AsBlock(rethrow, at), at);
        var caught = new LambdaExpr(new Parameter("e", Ident("Exception", at)),
                                    AsBlock(handlerBody, at))
            { Line = at.Line, Column = at.Column, Sugar = true };
        var checkedOk = Call(Call(Ident("try", at), AsBlock(guard, at), at), caught, at);
        // 拒收是**引擎内部机制**,不挂小写全局名(免得诱人乱用 —— 见 System.Reject 那段注解)
        var rejected = Call(new MemberAccess(Ident("System", at), "Reject") { Line = at.Line, Column = at.Column },
                            Call(Ident("TypeError", at),
                                 new StringLiteral($"实参不满足 '{param}' 的守卫") { Line = at.Line, Column = at.Column },
                                 at), at);
        var gated = Call(Call(checkedOk, AsBlock(new NumberLiteral("0") { Line = at.Line, Column = at.Column }, at), at),
                         AsBlock(rejected, at), at);

        var stmts = new List<Statement> { new ExpressionStatement(gated) { Line = at.Line, Column = at.Column } };
        stmts.AddRange(body.Statements);
        return Block(stmts, at);
    }

    private BlockExpr ParseShorthandBody(Token at)
    {
        _holeCount = 0;
        var e = ParseExpression();
        if (HasHoles(e)) e = DesugarHoles(e);
        return Block([new ExpressionStatement(e) { Line = e.Line, Column = e.Column }], at);
    }

    private bool AnyNewlineBeforeClose(TokenType closing)
    {
        int depth = 1;
        for (int i = _pos; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.Newline) return true;
            if (t.Type == TokenType.LeftBrace) depth++;
            if (t.Type == closing)
            {
                depth--;
                if (depth == 0) return false;
            }

            if (t.Type == TokenType.EndOfFile) return false;
        }

        return false;
    }

    private BlockExpr ParseMandatoryBlock(string context)
    {
        int line = Previous().Line, col = Previous().Column;
        Consume(TokenType.LeftBrace, $"{context}需要 '{{' 开头");
        // 只问"看起来是不是块":强制块里出现 `->` 也不算字典(字典不该被当成块体)。
        if (!AnyNewlineBeforeClose(TokenType.RightBrace))
            throw ParseError($"{context}需要代码块（单行要用 ';' 收尾，多行要换行）"
                           + "——只想交回一个值就直接写 `(x) => 表达式`，不用花括号");
        var stmts = ParseBlockStatements();
        return new BlockExpr(stmts) { Line = line, Column = col, Source = source };
    }

    private Expression? TryParseRange(bool startClosed)
    {
        // 先拿一眼就能看出来的便宜判据挡一道:**这一层有没有 `..`**。
        // 没有就直接说"不是区间",一次试读都不做。
        if (!HasDotDotAhead()) return null;

        var save = _pos;
        try
        {
            // 省了起点:`[..1]` —— `..` 就顶在最前头
            var lo = Check(TokenType.DotDot) ? null : ParseExpression();
            var dot = Peek();                        // 位置兜底用(两头都省时 `lo`/`hi` 都没得指)
            if (!Match(TokenType.DotDot))
            {
                _pos = save;
                return null;
            }

            // 省了终点:`[1..]` —— 收尾的括号紧跟在后
            var hi = Check(TokenType.RightBracket) || Check(TokenType.RightParen)
                ? null
                : ParseExpression();

            bool endClosed;
            if (Match(TokenType.RightBracket)) endClosed = true;
            else if (Match(TokenType.RightParen)) endClosed = false;
            else
            {
                _pos = save;
                return null;
            }

            // 位置取实有的那一头;两头都省(`[..]`)就指那个 `..` 自己
            return new RangeExpr(lo, hi, startClosed, endClosed)
            {
                Line = lo?.Line ?? hi?.Line ?? dot.Line,
                Column = lo?.Column ?? hi?.Column ?? dot.Column,
            };
        }
        catch (SyntaxException)
        {
            _pos = save;
            return null;
        }
    }

    private bool HasDotDotAhead()
    {
        var depth = 0;
        for (var i = _pos; i < tokens.Count; i++)
        {
            switch (tokens[i].Type)
            {
                case TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace:
                    depth++;
                    break;
                case TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace:
                    if (depth == 0) return false;      // 这一层到头了,没看见 `..`
                    depth--;
                    break;
                case TokenType.DotDot when depth == 0:
                    return true;
                case TokenType.EndOfFile:
                    return false;
            }
        }

        return false;
    }

    // ========================================
    //  列表解析辅助
    // ========================================

    private List<Expression> ParseSpaceSeparatedList(TokenType closing, string closingName)
    {
        var list = new List<Expression>();
        if (Match(closing)) return list;

        while (true)
        {
            list.Add(ParseExpression(allowCall: false));
            if (Match(closing)) return list;
            if (Check(TokenType.Comma))
                throw ParseError("不允许 ',' —— 请用空格代替逗号");
            if (Match(TokenType.Newline)) continue;
            if (IsAtEnd())
                throw ParseError($"列表末尾需要 '{closingName}'");
        }
    }

    private List<Statement> ParseBlockStatements()
    {
        SkipNewlines();
        // `{;}` 是**空体**的字面写法，展开成 `{();}` —— 一个"什么都不做"的表达式语句。
        // （`{ }` 仍然不许：分号才是那个"我故意写了个空语句"的记号。）
        if (Previous().Type == TokenType.Newline && Check(TokenType.RightBrace))
        {
            var at = Previous();
            _pos++;
            return [new ExpressionStatement(new LiteralExpr(VoidVal.Instance) { Line = at.Line, Column = at.Column })
                { Line = at.Line, Column = at.Column }];
        }

        var list = new List<Statement>();
        // 块是新的语境:外层 do 的 `:<` 不该漏进来。漏了的话
        // `(y: int) => { y :< m; }` 会被当成绑定,而绑定只活在 do 的折叠过程里 ——
        // 留下一个没人认识的 BindStatement 一路带进求值器。
        var outerDo = _doDepth;
        _doDepth = 0;
        try
        {
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                list.Add(ParseStatement());
                SkipNewlines();
            }
        }
        finally
        {
            _doDepth = outerDo;
        }

        Consume(TokenType.RightBrace, "代码块末尾需要 '}'");

        if (list.Count == 0)
            throw ParseError("不允许空的 '{ }'");

        return list;
    }
}
