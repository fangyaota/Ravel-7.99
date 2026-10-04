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

    private Expression ParsePrimary()
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

    /// <summary>`do { … }` —— 把一串"从 Monad 里取值"的语句**折成 Bind 链**:
    ///
    /// <code>
    /// do { x =&lt; m1; y =&lt; m2; Some (x + y); }
    /// ≡  m1.Bind ((x: object) =&gt; { m2.Bind ((y: object) =&gt; { Some (x + y); }); })
    /// </code>
    ///
    /// 每遇到一条 `名字 =&lt; 表达式`,就把它**后面剩下的全部**包成 lambda 交给 `Bind`,
    /// 于是链在解析期就地长出来,运行时不必为这个语法添任何东西。最后一条语句
    /// (不许是 `=&lt;`)的值就是整块的值 —— 也就是最内层那个 lambda 体的值。
    ///
    /// 绑定出来的变量标 `object`(最宽的那个)—— 这里对拿到什么一无所知,而 lambda 的参数
    /// 也只知道"有东西来了"。想要具体类型就在块里自己过一手(`n: int = x`)。</summary>
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

        // 从最后一条往前折:`rest` 始终是"后面那些语句"折出来的那段
        Expression rest = Block([stmts[^1]], at);
        for (var i = stmts.Count - 2; i >= 0; i--)
        {
            if (stmts[i] is not BindStatement bind)
            {
                // 普通语句就搁在折好的那段前面,它于是落在同一个 lambda 体里
                rest = Prepend(stmts[i], rest, at);
                continue;
            }

            var lam = new LambdaExpr(new Parameter(bind.Name, ObjectType(at)), AsBlock(rest, at))
                { Line = at.Line, Column = at.Column };
            rest = new CallExpr(new MemberAccess(bind.Monad, "Bind") { Line = bind.Line, Column = bind.Column },
                lam) { Line = at.Line, Column = at.Column };
        }

        return rest;
    }

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
            if (TypeAt(off) != TokenType.Identifier) return false;
            off++;
            if (TypeAt(off) == TokenType.Colon)
            {
                off = SkipTypeAnnotation(off + 1);
                if (off < 0) return false;               // 类型读不动(括号没闭上之类)
            }

            if (TypeAt(off) == TokenType.RightParen) return TypeAt(off + 1) == TokenType.Arrow;
            // 还没到 ')' —— 后面只可能是**下一个参数的名字**(不是就当场判否)
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
        var body = ParseMandatoryBlock(what);
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

    /// <summary>把一条普通语句搁到已经折好的那段**前面**。</summary>
    private Expression Prepend(Statement s, Expression rest, Token at)
        => rest is BlockExpr b
            ? b with { Statements = [s, .. b.Statements] }
            : Block([s, new ExpressionStatement(rest) { Line = rest.Line, Column = rest.Column }], at);

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

        // 尝试 lambda:  (IDENT [: 类型] [IDENT [: 类型]]*) =>
        if (LooksLikeLambdaParams())
        {
            var @params = new List<Parameter>();
            while (true)
            {
                var pName = Consume(TokenType.Identifier, "lambda 参数需要一个名字（`(x: int) => …`）");
                // **注解可省**:省了就按 `object` 收(谁都收得下)。和 `do` 的绑定、占位符消糖
                // 一个待遇 —— 那些地方也只知道"有东西来了",标不出更细的类型。
                // 要更细就在体里自己过一手:`n: int = x`。
                var pType = Match(TokenType.Colon) ? ParseTypeAnnotation() : ObjectType(pName);
                @params.Add(new Parameter(pName.Lexeme, pType));
                SkipNewlines();
                if (!Check(TokenType.Identifier)) break;      // 到 ')' 了
            }

            Consume(TokenType.RightParen, "lambda 参数后需要 ')'");
            Consume(TokenType.Arrow, "lambda 参数后需要 '=>'");
            var body = ParseUserLambdaBody("lambda 体", Previous());

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
        // 键从"标识符即字符串"改成了**表达式**(`{"a": 1}` / `{1: "x"}` / `{k: v}`),
        // 判据不能再靠前瞻,得真读一次。`x:: …` / `x: = …` 是**定义符**不是键值对,排除。
        //
        // 这一次解析的**结果两条路都用**(字典那条当第一个键、集合那条当第一个元素),
        // 不走"读完退回去再读一遍":`_` 的序号是个单调计数器(见 `_holeCount`),
        // 读两遍会让洞的编号和消糖时按顺序发的参数对不上(`{_ + 1}` 直接报「未定义的变量 '_1'」)。
        var first = ParseExpression(allowCall: false);
        var isDict = Check(TokenType.Colon)
                     && TypeAt(1) is not (TokenType.Colon or TokenType.Equal);
        return isDict ? ParseDict(line, col, first) : ParseSet(line, col, first);
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
            Consume(TokenType.Colon, "字典键后需要 ':'");
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
    private BlockExpr ParseMandatoryBlock(string context)
    {
        int line = Previous().Line, col = Previous().Column;
        Consume(TokenType.LeftBrace, $"{context}需要 '{{' 开头");
        if (!HasNewlineBeforeClose(TokenType.RightBrace))
            throw ParseError($"{context}需要代码块（单行要用 ';' 收尾，多行要换行）");
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
