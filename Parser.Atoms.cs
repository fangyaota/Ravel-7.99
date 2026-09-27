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
            return new StringLiteral(Previous().Lexeme)
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
    /// 绑定出来的变量只能标 `object`:lambda 的参数必须有注解,而这里对拿到什么一无所知。
    /// 想要具体类型就在块里自己过一手(`n: int = x`)。</summary>
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
        if (stmts[^1] is BindStatement) throw ParseError("do 块的最后一条语句要是个值（不能以 '=<' 收尾）");

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

    /// <summary>合成一个 `object` 注解节点 —— do 的绑定参数只能给最宽的那个类型。</summary>
    private static IdentifierExpr ObjectType(Token at) => new("object") { Line = at.Line, Column = at.Column };

    private BlockExpr Block(List<Statement> stmts, Token at)
        => new(stmts) { Line = at.Line, Column = at.Column, Source = source };

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
                var body = ParseMandatoryBlock("lambda 体");
                return new LambdaExpr(new Parameter("_", new IdentifierExpr("void") { Line = line, Column = col }), body) { Line = line, Column = col };
            }

            // () 独立 → void 字面量
            return new VoidLiteral { Line = line, Column = col };
        }

        // 尝试 lambda:  (IDENT : IDENT [IDENT : IDENT]*) =>
        if (Check(TokenType.Identifier) && CheckNext(TokenType.Colon))
        {
            var @params = new List<Parameter>();
            while (Check(TokenType.Identifier) && CheckNext(TokenType.Colon))
            {
                var pName = tokens[_pos].Lexeme;
                _pos++; // IDENT
                _pos++; // :
                @params.Add(new Parameter(pName, ParseTypeAnnotation()));
                SkipNewlines();
            }

            Consume(TokenType.RightParen, "lambda 参数后需要 ')'");
            Consume(TokenType.Arrow, "lambda 参数后需要 '=>'");
            var body = ParseMandatoryBlock("lambda 体");

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

        // 单行无换行：`IDENT :` 开头就是字典。
        // 但 `x:: …` / `x: = …` 是**定义符**(见 IsDefinitionOp),不是字典的键值对。
        // 判据全用前瞻,所以不匹配时一个 token 都没动过 —— 不必先存 _pos 再还回去。
        var isDict = Check(TokenType.Identifier) && TypeAt(1) == TokenType.Colon
                     && TypeAt(2) is not (TokenType.Colon or TokenType.Equal);
        return isDict ? ParseDict(line, col) : ParseSet(line, col);
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

    /// <summary>{ expr expr ... } → 集合</summary>
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

    /// <summary>{ key: val key: val ... } → 字典</summary>
    private Expression ParseDict(int line, int col)
    {
        var entries = new List<DictEntry>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            var key = Consume(TokenType.Identifier, "需要字典键").Lexeme;
            Consume(TokenType.Colon, "字典键后需要 ':'");
            var value = ParseExpression(allowCall: false);
            entries.Add(new DictEntry(key, value));
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
        // 块是新的语境:外层 do 的 `=<` 不该漏进来。漏了的话
        // `(y: int) => { y =< m; }` 会被当成绑定,而绑定只活在 do 的折叠过程里 ——
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
