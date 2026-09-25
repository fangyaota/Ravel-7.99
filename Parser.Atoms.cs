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
        // 运算符节 `+.2` —— 左操作数留空,等价于 `_ + 2`(脱糖成同一个 lambda)。
        // 右操作数只吃一个 primary(含成员访问),所以 `+.2 + 3` 是 `(+.2) + 3`。
        if (IsOperatorToken(Peek().Type) && _pos + 1 < tokens.Count && tokens[_pos + 1].Type == TokenType.Dot)
        {
            var sym = Peek();
            _pos += 2;
            _holeCount++;
            var hole = new HoleExpr(_holeCount - 1) { Line = sym.Line, Column = sym.Column };
            return new BinaryExpr(hole, sym.Lexeme, ParseCall(allowCall: false))
                { Line = sym.Line, Column = sym.Column };
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
                var body = ParseMandatoryBlock("lambda body");
                return new LambdaExpr(new Parameter("_", "void"), body) { Line = line, Column = col };
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
                var pType = Consume(TokenType.Identifier, "参数需要类型名").Lexeme;
                @params.Add(new Parameter(pName, pType));
                SkipNewlines();
            }

            Consume(TokenType.RightParen, "lambda 参数后需要 ')'");
            Consume(TokenType.Arrow, "lambda 参数后需要 '=>'");
            var body = ParseMandatoryBlock("lambda body");

            // 单参数：直接返回（兼容原有行为）
            if (@params.Count == 1)
                return new LambdaExpr(@params[0], body) { Line = line, Column = col };

            // 多参数消糖： (a:int b:string) => { body }
            // → (a:int) => { (b:string) => { body } }
            Expression result = body;
            for (int i = @params.Count - 1; i >= 0; i--)
            {
                if (i == @params.Count - 1)
                {
                    // 最内层：直接使用原始 body（它已是 BlockExpr）
                    result = new LambdaExpr(@params[i], (BlockExpr)result) { Line = line, Column = col };
                }
                else
                {
                    // 外层：把内层 lambda 包在一个 block 中返回
                    var blockStmts = new List<Statement>
                    {
                        new ExpressionStatement(result) { Line = line, Column = col }
                    };
                    var block = new BlockExpr(blockStmts) { Line = line, Column = col, Source = source };
                    result = new LambdaExpr(@params[i], block) { Line = line, Column = col };
                }
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

        // 单行无换行：检测是否为字典（IDENT : 模式）
        var saved = _pos;
        if (Check(TokenType.Identifier))
        {
            _pos++; // skip IDENT
            if (Check(TokenType.Colon))
            {
                _pos++; // skip :
                if (!Check(TokenType.Colon) && !Check(TokenType.Equal))
                {
                    _pos = saved;
                    return ParseDict(line, col);
                }
            }
        }

        _pos = saved;
        return ParseSet(line, col);
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

    /// <summary>前瞻：在匹配 closing 之前是否遇到运算符 token</summary>
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

    /// <summary>强制解析为代码块——后面不是 block（单行无换行，即集合/字典）时报错</summary>
    /// <summary>`=>` 后面的块。这里**不做** HasNewlineBeforeClose 那套判断——
    /// 那条规则是给 ParseBrace 用的(表达式位置要分出 Set/Dict/Block),
    /// 而 lambda 体必须是块,没有歧义。从前照抄了那条规则,于是 `() => { x + 1 }`
    /// 报「lambda body 需要代码块」,得写成 `{ x + 1; }` 才行——
    /// 教程里满篇的单行 lambda 全是错的,`tests/59`/`72` 更是被它抢先报错、
    /// 根本没测到自己要测的 readonly/unreadable。</summary>
    private BlockExpr ParseMandatoryBlock(string context)
    {
        int line = Previous().Line, col = Previous().Column;
        Consume(TokenType.LeftBrace, $"需要 '{{' for {context}");
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
                throw ParseError($"需要 '{closingName}' after expression list");
        }
    }

    /// <summary>代码块 — 以换行分隔的语句，当前已在 { 之后</summary>
    private List<Statement> ParseBlockStatements()
    {
        SkipNewlines();
        var list = new List<Statement>();

        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            list.Add(ParseStatement());
            SkipNewlines();
        }

        Consume(TokenType.RightBrace, "代码块末尾需要 '}'");

        if (list.Count == 0)
            throw ParseError("不允许空的 '{ }'");

        return list;
    }
}
