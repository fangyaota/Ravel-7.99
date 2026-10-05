namespace Ravel;

using System.Linq;
using Ravel.Runtime;

/// <summary>递归下降解析器的分片,按职责拆文件(和 Runtime/Interpreter.*.cs 一个路子):
/// `Parser.cs` 入口 + token 辅助,`Parser.Statements.cs` 语句,
/// `Parser.Expressions.cs` 优先级链,`Parser.Atoms.cs` 基本单元与括号/块。</summary>
public partial class Parser
{
    // ========================================
    //  Primary
    // ========================================

    /// <param name="bareLambda">这一格认不认"裸模式参数"(`1 => …` / `[x y] => …`)。
    /// 模式**里面**取字面量那一格和取字典键要传 `false` —— 那两处的值不可能是个 lambda;
    /// 不关掉的话 `1 => …` 会自己套自己(`ParsePrimary` → 裸模式 → `ParsePattern` →
    /// 又回到 `ParsePrimary`),当场栈溢出。</param>
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

    /// <summary>`do { … }` —— 把块读出来就行:**折成 `Bind` 链那一步归 `Lowering`**
    /// (见 `Syntax/Lowering.cs` 的 `Do`),解析器只交一枚 <see cref="DoExpr"/>。
    ///
    /// 几条**语法**规矩留在这儿(和 `??=` / `++` 那两条一个口径——在那儿报错,
    /// 插入符指得到地方):块里得有语句、最后一条得是个**值**。
    ///
    /// `_doDepth` 只管一件事:放行块里的 `名字 :&lt; 表达式`(`BindStatement`)。</summary>
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

    /// <summary>参数表里这一格是不是**开一个模式**(`[…]` / `{…}`)。</summary>
    private bool StartsParamGroup(int off)
        => TypeAt(off) is TokenType.LeftBracket or TokenType.LeftBrace;

    /// <summary>参数表还能再收一项吗(名字 / 模式 / 字面量 / 带括号的模式)。</summary>
    private bool StartsParam()
        => Check(TokenType.Identifier) || StartsParamGroup(0) || IsLiteralStart()
        || Check(TokenType.LeftParen);

    /// <summary>模式参数取合成名用(`__p{n}`)。它是 **lambda 的参数**,作用域只在那个
    /// lambda 里,所以每次解析从 0 数就够 —— 而且它会出现在**打印出来的函数体**里,
    /// 从 0 数输出才可复现(和 `__g{n}` 那种落在用户作用域里的不一样)。</summary>
    private int _paramCount;

    /// <summary>合成一个 `object` 注解节点 —— 省了注解的参数、`do` 的绑定、占位符消糖
    /// 都用它:那些地方只知道"有东西来了",标不出更细的类型。</summary>
    private static IdentifierExpr ObjectType(Token at) => new("object") { Line = at.Line, Column = at.Column };

    /// <summary>刚吃掉 '(' 之后这一段,是不是「参数表 `) =>`」。
    ///
    /// 参数 = `名字`,或 `名字: 类型`(**注解可省**)。所以不能像从前那样只看
    /// `名字 :` 两格 —— `(x) =>` 也得认。**必须一路确认到 `) =>` 才认**:`(a + b)`
    /// 那种普通的括号分组里也有名字,看到个名字就当参数会把分组吃成参数表。
    ///
    /// 只看 token、不动 `_pos`;不是 lambda 就还是走"括号分组"那条路,一个字符都没动过。</summary>
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

    /// <summary>`(` 后面这一段,配对的 `)` 后面紧跟 `=>` 吗 —— 认**带守卫**的参数表用。
    ///
    /// 正常的参数表由 <see cref="LooksLikeLambdaParams"/> 认;那条路认不出 `(v == s)`,
    /// 因为 `v` 后面跟的不是 `:` 也不是 `)`。守卫那条只能靠"`(...)` 后面是不是 `=>`"来认。</summary>
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

    /// <summary>从 off 跳过一段类型注解,返回它后面那格的偏移;读不动给 -1。
    /// 两种写法:`名字(.名字)*`,或者括号里的任意表达式(`(pick ())` —— 见 ParseTypeAnnotation)。</summary>
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

    /// <summary>**用户写的** lambda 体。和 <see cref="ParseMandatoryBlock"/> 只差一件事:
    /// 进去压一层"这段里出现过 `return` 吗",出来按需把体包成
    /// `callcc ((__return: object) => { … })`。
    ///
    /// **只有用户写的 `=>` 才算一层** —— `do` / `?.` / 占位符 / 运算符节那些**内部消糖**造的
    /// lambda 不走这儿。于是体里写 `return`,绑到的是外面那个真函数,不会被中间那些生成的
    /// lambda 截住(它们在外层 `callcc` 的作用域里,照样跳得出去)。
    ///
    /// 名字**固定**成 `__return`:嵌套靠词法遮蔽自己就分开了(内层那枚绑内层的),
    /// 用不着为每层起个新名字。</summary>
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

    /// <summary>折出来的那段要当 lambda 的体,得是个块:已经是就原样,不是就包一层。</summary>
    private BlockExpr AsBlock(Expression e, Token at)
        => e is BlockExpr b ? b : Block([new ExpressionStatement(e) { Line = e.Line, Column = e.Column }], at);

    /// <summary>嵌套深度护栏。递归下降解析器靠 C# 调用栈,而 StackOverflow *捕获不了*——
    /// 1000 层括号就能让进程直接死在 "Stack overflow." 上,连个语法错误都看不到。
    /// 实测 500 层没事、1000 层爆,所以卡在 400。</summary>
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

    /// <summary>( ... ) — lambda / 分组（括号内与顶层一致：柯里化）</summary>
    private Expression ParseParen()
    {
        int line = Previous().Line, col = Previous().Column;

        if (Match(TokenType.RightParen))
        {
            if (Match(TokenType.Arrow))
            {
                // () => {...}  语法糖 →  (_:void) => {...}
                var body = ParseUserLambdaBody("lambda 体", Previous());
                return new LambdaExpr(new Parameter("_", new IdentifierExpr("void") { Line = line, Column = col }), body) { Line = line, Column = col };
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
                    @params.Add(new Parameter("__p" + _paramCount++, ObjectType(at), ParsePattern()));
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
                        throw ParseError($"参数名 '{p.Name}' 写了两遍 —— 每个参数一个名字");
                    @params.Add(p);
                }

                if (Match(TokenType.Colon))
                {
                    Declare(new Parameter(pName.Lexeme, ParseTypeAnnotation()));
                }
                else if (Check(TokenType.Identifier) || Check(TokenType.RightParen) || StartsParamGroup(0))
                {
                    // **注解可省**:省了就按 `object` 收(谁都收得下)。和 `do` 的绑定、占位符消糖
                    // 一个待遇 —— 那些地方也只知道"有东西来了",标不出更细的类型。
                    // 要更细就在体里自己过一手:`n: int = x`。
                    // (后面跟 `[…]` / `{…}` 也算"这一项完了"——那是下一个模式参数。)
                    Declare(new Parameter(pName.Lexeme, ObjectType(pName)));
                }
                else
                {
                    // **守卫**:这一项整个是一条表达式(`v == s` / `v |> IsPrime`)。
                    // 退回去整条重读一遍,参数名取它**最左边那个标识符**(草稿那句"只看最左边的")。
                    _pos = saveAt;
                    guard = ParseExpression();
                    // **名字按 token 顺序取最左边那个标识符** —— 不走 AST:`v |> IsPrime`
                    // 在 AST 里是 `CallExpr(IsPrime, v)`(`|>` 解析期就折成调用了),
                    // 按树取"最左"会取到 IsPrime。草稿那句"只看最左边的"说的就是源码顺序。
                    var nm = FirstIdentName(saveAt, _pos)
                        ?? throw ParseError("守卫里得有个参数名 —— 它最左边那个标识符就是");
                    // 这个名字前面**已经声明过**就只挂守卫,别再声明一个 ——
                    // 不然 `(x: int x > 10)` 会变成两个参数,后一个还是 `object`,把类型注解盖掉。
                    if (!@params.Any(q => q.Name == nm))
                        @params.Add(new Parameter(nm, ObjectType(at)));
                    if (!Check(TokenType.RightParen))
                        throw ParseError("守卫要写在参数表的**最后一项** —— 它是一条表达式,"
                                       + "会把后面那一项吞进去（`(x: int v == 1)` 这么写）");
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

    /// <summary>{ ... } — 集合 / 字典 / 代码块（根据是否跨行判断）</summary>
    private Expression ParseBrace()
    {
        int line = Previous().Line, col = Previous().Column;

        // 单行 `{}` 是空字典。空块本来就禁止(见 ParseBlockStatements),所以没有歧义;
        // 这样空集合的三种写法才一致:[] / {} / set default
        if (Match(TokenType.RightBrace))
            return new DictLiteral([]) { Line = line, Column = col };

        // 含 Newline（; 也算）→ 代码块，否则 → 集合或字典
        if (HasNewlineBeforeClose(TokenType.RightBrace))
        {
            var block = ParseBlockStatements();
            return new BlockExpr(block) { Line = line, Column = col, Source = source };
        }

        // 单行无换行：**先当表达式把第一个元素读出来**,看它后面跟的是不是 `:` —— 是就是字典。
        // 键从"标识符即字符串"改成了**表达式**(`{"a" -> 1}` / `{1 -> "x"}` / `{k -> v}`),
        // 判据不能再靠前瞻,得真读一次。
        //
        // **分隔符是 `->`**(从前是 `:`)—— 于是 `:` 整个让给了类型判断,这一族也不再有
        // "`x: int = 5` 到底算字典还是算块"那条特判:花括号里见 `->` 才是字典。
        //
        // 这一次解析的**结果两条路都用**(字典那条当第一个键、集合那条当第一个元素),
        // 不走"读完退回去再读一遍":`_` 的序号是个单调计数器(见 `_holeCount`),
        // 读两遍会让洞的编号和消糖时按顺序发的参数对不上(`{_ + 1}` 直接报「未定义的变量 '_1'」)。
        var first = ParseExpression(allowCall: false);
        return Check(TokenType.DictArrow) ? ParseDict(line, col, first) : ParseSet(line, col, first);
    }

    /// <summary>前瞻：在匹配 closing 之前是否遇到 Newline</summary>
    private bool HasNewlineBeforeClose(TokenType closing)
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

    /// <summary>{ expr expr ... } → 集合。`first` 是判据那一步已经读好的第一个元素
    /// (见 ParseBrace:那一次解析的成果两条路共用)。</summary>
    /// <summary>插值字符串 —— 拼成 `"" + 段 + 段 …`:字面量段原样,表达式段套一层
    /// `string (…)`(那个转换器管着"什么都转得成字符串":数、字符、对象都行)。
    ///
    /// 拼出来的是普通的 `+` 链,和手写 `"a" + (string x)` **一模一样** —— 求值、报错、
    /// 显示全走现成那条路,没有第二套语义。词法只把串切开(`Token.Parts`),
    /// 每段表达式的**源码**在这里另起一次解析,位置按它在原文件里的行列挪回去。
    ///
    /// (`string` 这个转换器按名字找 —— 它由 predefined.rav 放在全局;要是有谁把这个名字
    ///  遮住,插值就跟着用他那个,和手写 `string x` 的行为保持一致。)</summary>
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

    private Expression ParseSet(int line, int col, Expression first)
    {
        var elements = new List<Expression> { first };
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            elements.Add(ParseExpression(allowCall: false));
        }

        Consume(TokenType.RightBrace, "集合元素后需要 '}'");
        return new SetLiteral(elements) { Line = line, Column = col };
    }

    /// <summary>{ key: val key: val ... } → 字典。**键是表达式**(见 ParseBrace 的判据),
    /// 和值一样按 `allowCall: false` 那一档读 —— 于是 `{"a" + "b": 1}` 写得出来,
    /// 而 `{f x: 1}` 这种"实参里再套应用"不行(和值那边的口径一致)。</summary>
    private Expression ParseDict(int line, int col, Expression first)
    {
        var entries = new List<DictEntry>();
        var key = first;
        while (true)
        {
            Consume(TokenType.DictArrow, "字典键后需要 '->'");
            var value = ParseExpression(allowCall: false);
            entries.Add(new DictEntry(key, value));
            if (Check(TokenType.RightBrace) || IsAtEnd()) break;
            key = ParseExpression(allowCall: false);
        }

        Consume(TokenType.RightBrace, "字典条目后需要 '}'");
        return new DictLiteral(entries) { Line = line, Column = col };
    }

    /// <summary>强制解析为代码块。**单行必须用 `;` 收尾** —— 块和集合/字典在没换行时
    /// 长得一样(`{ x }` 是集合、`{ a: 1 }` 是字典),`;` 和换行都是 Newline token,
    /// 它才是「这是块」的标记。这条规则对 lambda 体同样成立:`=> { x }` 是错的,
    /// `=> { x; }` 才对。(曾经去掉过这里的检查,以为 `=>` 后面块是强制的、
    /// 没有歧义——语言规则不该按解析器好不好写来定。)</summary>
    /// <summary>**单行 lambda 的简写**:`(x: int) => x + 1` —— 体就是**一条表达式语句**,
    /// 交回的值就是那个表达式的值(块的值也 = 最后一条语句的值,两边一脉相承)。
    ///
    /// 和 `{ … }` 那条的分岔**只有一个:`=>` 后面见不见 `{`**:
    ///   * `(x) => { x; }` 照旧是"体是一块"(老写法,一个字没变);
    ///   * `(x) => { x }` 照旧报「lambda 体需要代码块」—— 单行花括号是**集合**,不是简写。
    ///     **这个糖交不回一个块**,那是它的边界(也就没有"简写里再写语句"这回事)。
    ///
    /// 想交回集合/字典,用括号把它跟 `=>` 隔开:`() => ({1 2 3})` / `() => ({"a" -> 1})`。
    ///
    /// 体到哪儿为止交给 <see cref="ParseExpression"/> —— 和语句那条路一个口径
    /// (换行 / 收尾符为止)。所以 `f := (x) => x + 1` 读到行尾,
    /// 而 `xs.Map ((x) => x * 2)` 读到那个 `)`。</summary>
    /// <summary>`x => 体` —— 单参 lambda 的**无括号**写法。折出来和 `(x) => 体` 一模一样
    /// (参数按 `object` 收,体走 <see cref="ParseUserLambdaBody"/> 那条路)。
    ///
    /// 两处**故意不收**,各给一句说清的:
    ///   * **不收注解** —— `x: int => …` 里那个 `x: int` 首先是**类型判断**(上一笔刚把
    ///     `:` 统一成这个读法),两种读法挤在一行。要带类型就加括号:`(x: int) => …`。
    ///   * **`_` 不当参数名** —— `_` 是占位符/丢弃的记号(`_ := v` 是丢掉),拿它当参数名
    ///     那个名字根本引不到。想写"一个参数"用 `x => 体`;想用洞就直接写 `_ + 1`。
    ///
    /// 识别口子只有一个:**光一个标识符 + `=>`**。所以
    ///   * `f x => e` 是 `f (x => e)`(从前的报错位,现在有了读法);
    ///   * `a.b => e` **不是** lambda —— `b` 走的是成员链那条路,参数不能是成员。
    /// </summary>
    /// <summary>`1 => …` / `"yes" => …` / `[x y] => …` / `{"a" -> v} => …` —— "裸一格模式
    /// 直接接 `=>`"的判据。
    ///
    /// 括号那两样用现成的 <see cref="SkipBalanced"/> 跳过去,看收尾符后面是不是 `=>` ——
    /// **只看这一处**,不往回退着重读整项,所以不会翻倍。
    ///
    /// `()` 不算:那是**空参数表**,`ParseParen` 最开头就接走了(这儿也拦一道,免得
    /// "单位那个值"和"没有参数"两件事在同一个写法上撞)。
    /// `_` 也不算:它在**位置那一格**是洞(`x => 体` 除外),`(_) => …` 才当通配写。</summary>
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

    /// <summary>读一个"裸模式参数"的 lambda。合成名(`__p{n}`)和括号那条一个规矩,
    /// 拆法照旧归 `Lowering`。</summary>
    private Expression ParseBarePatternLambda()
    {
        var at = Peek();
        var pattern = ParsePattern();
        Consume(TokenType.Arrow, "模式后需要 '=>'");
        var arrow = Previous();
        var body = ParseUserLambdaBody("lambda 体", arrow);
        return new LambdaExpr(new Parameter("__p" + _paramCount++, ObjectType(at), pattern), body)
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

    /// <summary>`[from, to)` 里**最左边那个标识符** —— 守卫项的参数名就是它
    /// (`(v == s)` → `v`,`(v |> IsPrime)` → `v`)。</summary>
    private string? FirstIdentName(int from, int to)
    {
        for (var i = from; i < to && i < tokens.Count; i++)
            if (tokens[i].Type == TokenType.Identifier)
                return tokens[i].Lexeme;
        return null;
    }

    /// <summary>把参数表的**守卫**折成体的第一句:
    ///
    ///     if { try { 守卫; } (e: TypeError) => { false; }; } { 0; } { reject "…"; }
    ///
    /// 两层意思:
    ///   * **守卫不成立** → `reject`(抛一枚 `RejectedException`)—— `|` 的交替接住它、试下一支。
    ///     所以守卫 + 多子句 = 多子句函数;
    ///   * 求值期间冒出来的 **TypeError** 也算"这一支不收" —— 那正是"类型/形状对不上"的统一说法
    ///     (`v == 1` 里 v 是个字符串、`v |> IsPrime` 里 IsPrime 不收这个实参,都是这一类)。
    ///     用的是现成的 `try`(lib/exceptions.rav),**引擎那一侧一个字都不用添**。
    ///
    /// 代价:每次调用都要过一次 `try`(一次 callcc + 压一个 handler)。守卫是拿便利换的,
    /// 热路径上宁可直接写 `if`。</summary>
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
        var handlerBody = Call(Call(Call(Ident("if", at), AsBlock(isTypeError, at), at),
                                    AsBlock(Ident("false", at), at), at),
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
        var gated = Call(Call(Call(Ident("if", at), AsBlock(checkedOk, at), at),
                              AsBlock(new NumberLiteral("0") { Line = at.Line, Column = at.Column }, at), at),
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

    private BlockExpr ParseMandatoryBlock(string context)
    {
        int line = Previous().Line, col = Previous().Column;
        Consume(TokenType.LeftBrace, $"{context}需要 '{{' 开头");
        if (!HasNewlineBeforeClose(TokenType.RightBrace))
            throw ParseError($"{context}需要代码块（单行要用 ';' 收尾，多行要换行）"
                           + "——只想交回一个值就直接写 `(x) => 表达式`，不用花括号");
        var stmts = ParseBlockStatements();
        return new BlockExpr(stmts) { Line = line, Column = col, Source = source };
    }

    /// <summary>刚吃掉 `[` 或 `(` 之后,试一把**区间**:`lo .. hi` 后面紧跟收尾的右括号。
    ///
    /// 那一对括号各带**一半的意思**(`[` `]` 含那一端、`(` `)` 不含),所以收尾**两种都收**
    /// —— `[1..5)` 这种混着写是合法的 —— 按实际收到的那个定 `EndClosed`。
    ///
    /// 不是区间就把 `_pos` 退回去,让调用点照原路走(列表字面量 / 括号分组 / lambda);
    /// 试读半路抛语法错也退,**最后报出来的还是原来那条路的错** —— `[]`、`(a + b)`、
    /// `(x: int) => …` 全都不受影响。
    ///
    /// 试读用**完整表达式**:`[1 2 3]` 会读成 `1 2 3`(一个调用链),看不见 `..` 就退,
    /// 于是普通列表照旧;而 `[f 1..3]` 读成 `(f 1)..3` —— 和这套语言"实参吃到运算符为止"
    /// 一个脾气。**裸的 `a..b` 不认**:这个入口只在括号里说话。</summary>
    private Expression? TryParseRange(bool startClosed)
    {
        // 先拿一眼就能看出来的便宜判据挡一道:**这一层有没有 `..`**。
        // 没有就直接说"不是区间",一次试读都不做。
        if (!HasDotDotAhead()) return null;

        var save = _pos;
        try
        {
            var lo = ParseExpression();
            if (!Match(TokenType.DotDot))
            {
                _pos = save;
                return null;
            }

            var hi = ParseExpression();

            bool endClosed;
            if (Match(TokenType.RightBracket)) endClosed = true;
            else if (Match(TokenType.RightParen)) endClosed = false;
            else
            {
                _pos = save;
                return null;
            }

            return new RangeExpr(lo, hi, startClosed, endClosed) { Line = lo.Line, Column = lo.Column };
        }
        catch (SyntaxException)
        {
            _pos = save;
            return null;
        }
    }

    /// <summary>这一段括号里、**就在自己这一层**,有没有 `..`。
    ///
    /// 只是个"绝不可能是区间"的便宜门 —— 为什么非要有它:`TryParseRange` 的试读是拿
    /// **完整表达式**跑一遍,而括号可以嵌套:每一层都试读一次,里面那层的试读又套一层……
    /// 1000 层括号会直接炸成天文数字(`tests/152` 的 `171_nesting_depth` 就压在那儿,
    /// 实测真挂住了)。这个扫描只数 token、不做任何解析,顺带把 `{}` 里、内层括号里的
    /// `..` 都排除掉(那些不属于这一层)。</summary>
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

    /// <summary>代码块 — 以换行分隔的语句，当前已在 { 之后)</summary>
    private List<Statement> ParseBlockStatements()
    {
        SkipNewlines();
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
