namespace Ravel;

using System.Linq;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    private int _holeCount;

    // ========================================
    //  入口
    // ========================================

    public Program Parse()
    {
        var statements = new List<Statement>();

        while (!IsAtEnd())
        {
            if (Match(TokenType.Newline))
                continue;
            statements.Add(ParseStatement());
        }

        return new Program(statements);
    }

    // ========================================
    //  语句
    // ========================================

    static readonly HashSet<string> _modifiers = ["init", "readonly", "override", "new", "public", "private", "protected", "outdated", "unreadable", "by", "core"];
    static bool IsMod(string kw) => _modifiers.Contains(kw) || kw.StartsWith("operator");
    /// <summary>类机制内部词——禁止作为变量名（base/this 是类内可用变量，不禁）</summary>
    static readonly HashSet<string> _reservedWords = ["init", "thistype", "block", "core"];

    private Statement ParseStatement()
    {
        // 修饰符
        var attrs = new List<string>();
        while (true)
        {
            if (Check(TokenType.Identifier))
            {
                var kw = Peek().Lexeme;
                bool isMod = IsMod(kw);
                if (!isMod) break;
                // 下一个 token 是定义符 → 当前是名字，不是修饰符
                if (_pos + 1 < _tokens.Count)
                {
                    var nt = _tokens[_pos + 1].Type;
                    if (nt == TokenType.ColonEqual || nt == TokenType.ColonColonEqual || nt == TokenType.ColonColon || nt == TokenType.Colon)
                        break;
                }
                _pos++; attrs.Add(kw);
            }
            else break;
        }
        if (attrs.Count > 0)
        {
            if (Check(TokenType.Identifier) && (CheckNext(TokenType.ColonEqual) || CheckNext(TokenType.Colon) || CheckNext(TokenType.ColonColonEqual) || CheckNext(TokenType.ColonColon)))
                return ParseDefinition(attrs);
            if (Check(TokenType.ColonEqual) || Check(TokenType.Colon) || Check(TokenType.ColonColonEqual) || Check(TokenType.ColonColon))
                return ParseDefinition(attrs);
            throw ParseError("修饰符后需要 ':='");
        }
        // 普通定义：IDENT := expr 或 IDENT : type = expr
        // 普通定义：IDENT := expr 或 IDENT : type = expr
        if (Check(TokenType.Identifier) &&
            (CheckNext(TokenType.ColonEqual) || CheckNext(TokenType.Colon) ||
             CheckNext(TokenType.ColonColonEqual) || CheckNext(TokenType.ColonColon)))
            return ParseDefinition();
        // IDENT = expr → 赋值
        if (Check(TokenType.Identifier) && CheckNext(TokenType.Equal))
            return ParseAssignment();

        // 独立的表达式语句
        _holeCount = 0;
        var expr = ParseExpression();
        SkipNewlines();
        if (HasHoles(expr)) expr = DesugarHoles(expr);
        return new ExpressionStatement(expr) { Line = expr.Line, Column = expr.Column };
    }

    /// <summary>name := expr  |  name: Type = expr</summary>
    private Statement ParseDefinition(List<string>? attrs = null)
    {
        string name;
        int line, col;
        if (attrs != null && attrs.Count > 0)
        {
            if (Check(TokenType.Identifier))
            {
                var n = Consume(TokenType.Identifier, "需要变量名");
                name = n.Lexeme; line = n.Line; col = n.Column;
            }
            else
            {
                name = attrs!.FirstOrDefault(a => a.StartsWith("operator")) ?? attrs[0];
                line = Previous().Line; col = Previous().Column;
            }
        }
        else
        {
            var nameToken = Consume(TokenType.Identifier, "需要变量名");
            name = nameToken.Lexeme; line = nameToken.Line; col = nameToken.Column;
        }

        string? typeAnnotation = null;
        bool autoName = false;

        if (_reservedWords.Contains(name))
            throw ParseError($"'{name}' 是保留字");

        if (Match(TokenType.ColonColonEqual))
        {
            autoName = true;
        }
        else if (Match(TokenType.ColonColon))
        {
            autoName = true;
            var typeToken = Consume(TokenType.Identifier, "'::' 后需要类型名");
            typeAnnotation = typeToken.Lexeme;
            Consume(TokenType.Equal, "类型注解后需要 '='");
        }
        else if (Match(TokenType.ColonEqual))
        {
            // := → 类型推断
        }
        else if (Match(TokenType.Colon))
        {
            var typeToken = Consume(TokenType.Identifier, "':' 后需要类型名");
            typeAnnotation = typeToken.Lexeme;
            Consume(TokenType.Equal, "类型注解后需要 '='");
        }
        else
        {
            throw ParseError("变量定义需要 ':='、'::='、': type =' 或 ':: type ='");
        }

        _holeCount = 0;
        var value = ParseExpression();
        SkipNewlines();
        if (HasHoles(value)) value = DesugarHoles(value);
        SkipNewlines();

        if (name == "_")
            return new ExpressionStatement(value) { Line = line, Column = col };

        var result = new VarDefinition(name, typeAnnotation, value, attrs, autoName)
        {
            Line = line,
            Column = col,
        };
        if (autoName)
            return result; // 标记由 EvalStmt 处理
        return result;
    }

    private Statement ParseAssignment()
    {
        var nameToken = Consume(TokenType.Identifier, "需要变量名");
        Consume(TokenType.Equal, "需要 '='");
        _holeCount = 0;
        var value = ParseExpression();
        SkipNewlines();
        if (HasHoles(value)) value = DesugarHoles(value);
        if (nameToken.Lexeme == "_")
            return new ExpressionStatement(value) { Line = nameToken.Line, Column = nameToken.Column };

        return new Assignment(nameToken.Lexeme, value)
        {
            Line = nameToken.Line,
            Column = nameToken.Column,
        };
    }

    // ========================================
    //  表达式 — 按优先级递归下降
    // ========================================

    private Expression ParseExpression(bool allowCall = true)
        => ParsePipe(allowCall);

    /// <summary>管道 &lt;|  （最低优先级，右结合）</summary>
    private Expression ParsePipe(bool allowCall = true)
    {
        var left = ParseAssignment(allowCall);

        if (Match(TokenType.PipeLeft))
        {
            var right = ParsePipe(allowCall);
            return new PipeExpr(left, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>赋值 = += -= *= /=  （右结合，仅表达式级别）</summary>
    private Expression ParseAssignment(bool allowCall = true)
    {
        var left = ParseLogic(allowCall);

        if (Match(TokenType.Equal) || Match(TokenType.PlusEqual) ||
            Match(TokenType.MinusEqual) || Match(TokenType.StarEqual) ||
            Match(TokenType.SlashEqual) || Match(TokenType.PercentEqual))
        {
            var op = Previous().Lexeme;
            var right = ParseAssignment(allowCall);
            return new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>逻辑 ||  （优先级低于 &&）</summary>
    private Expression ParseLogic(bool allowCall = true)
    {
        var left = ParsePipeOp(allowCall);

        while (Match(TokenType.OrOr))
        {
            var op = Previous().Lexeme;
            var right = ParsePipeOp(allowCall);
            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>函数交替 |  （低于 ||，高于 &&... 嗯，| 优先级在 || 和 && 之间）</summary>
    private Expression ParsePipeOp(bool allowCall = true)
    {
        var left = ParseAndBit(allowCall);

        while (Match(TokenType.Pipe))
        {
            var op = Previous().Lexeme;
            var right = ParseAndBit(allowCall);
            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>位/逻辑 & ^</summary>
    private Expression ParseAndBit(bool allowCall = true)
    {
        var left = ParseAnd(allowCall);

        while (Match(TokenType.And) || Match(TokenType.Caret))
        {
            var op = Previous().Lexeme;
            var right = ParseAnd(allowCall);
            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>逻辑 &&</summary>
    private Expression ParseAnd(bool allowCall = true)
    {
        var left = ParseComparison(allowCall);

        while (Match(TokenType.AndAnd))
        {
            var op = Previous().Lexeme;
            var right = ParseComparison(allowCall);
            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>比较 != == &lt; &gt; &lt;= &gt;=</summary>
    private Expression ParseComparison(bool allowCall = true)
    {
        var left = ParseTerm(allowCall);

        while (Match(TokenType.NotEqual) || Match(TokenType.EqualEqual) ||
               Match(TokenType.Less) || Match(TokenType.Greater) ||
               Match(TokenType.LessEqual) || Match(TokenType.GreaterEqual))
        {
            var op = Previous().Lexeme;
            var right = ParseTerm(allowCall);
            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>加减 + -</summary>
    private Expression ParseTerm(bool allowCall = true)
    {
        var left = ParseFactor(allowCall);

        while (Match(TokenType.Plus) || Match(TokenType.Minus))
        {
            var op = Previous().Lexeme;
            var right = ParseFactor(allowCall);
            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>乘除取模 * / %</summary>
    private Expression ParseFactor(bool allowCall = true)
    {
        var left = ParseCall(allowCall);

        while (Match(TokenType.Star) || Match(TokenType.Slash) || Match(TokenType.Percent))
        {
            var op = Previous().Lexeme;
            var right = ParseCall(allowCall);
            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>一元 ! -  .成员访问  和函数调用</summary>
    private Expression ParseCall(bool allowCall = true)
    {
        // 一元 !
        if (Match(TokenType.Bang))
        {
            var operand = ParseCall(allowCall);
            return new UnaryExpr("!", operand) { Line = Previous().Line, Column = Previous().Column };
        }

        // 一元 -
        if (Match(TokenType.Minus))
        {
            var operand = ParseCall(allowCall);
            return new UnaryExpr("-", operand) { Line = Previous().Line, Column = Previous().Column };
        }

        var expr = ParsePrimary();

        // .成员访问  — 在空格调用之前处理
        while (Match(TokenType.Dot))
        {
            var member = Consume(TokenType.Identifier, "'.' 后需要成员名");
            expr = new MemberAccess(expr, member.Lexeme) { Line = expr.Line, Column = expr.Column };
        }

        if (!allowCall) return expr;

        // f a b c  →  ((f a) b) c  柯里化
        while (StartsPrimary())
        {
            var arg = ParsePrimary();
            while (Match(TokenType.Dot))
            {
                var mem = Consume(TokenType.Identifier, "'.' 后需要成员名");
                arg = new MemberAccess(arg, mem.Lexeme) { Line = arg.Line, Column = arg.Column };
            }
            expr = new CallExpr(expr, [arg])
            {
                Line = expr.Line,
                Column = expr.Column,
            };
        }

        return expr;
    }

    // ========================================
    //  Primary
    // ========================================

    private Expression ParsePrimary()
    {
        if (Match(TokenType.Number))
        {
            var lexeme = Previous().Lexeme;
            var isFloat = lexeme.Contains('.');
            return new NumberLiteral(double.Parse(lexeme), isFloat)
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
            return ParseParen();

        if (Match(TokenType.LeftBracket))
        {
            int line = Previous().Line, col = Previous().Column;
            var elements = ParseSpaceSeparatedList(TokenType.RightBracket, "]");
            return new ListLiteral(elements) { Line = line, Column = col };
        }

        if (Match(TokenType.LeftBrace))
            return ParseBrace();

        throw ParseError($"需要表达式，但得到 {Peek()}");
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
                var pName = _tokens[_pos].Lexeme;
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
                    var block = new BlockExpr(blockStmts) { Line = line, Column = col };
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
        return inner;
    }

    /// <summary>{ ... } — 集合 / 字典 / 代码块（根据是否跨行判断）</summary>
    private Expression ParseBrace()
    {
        int line = Previous().Line, col = Previous().Column;

        if (Match(TokenType.RightBrace))
            throw ParseError("不允许空的 '{ }'");

        // 含 Newline（; 也算）→ 代码块，否则 → 集合或字典
        if (HasNewlineBeforeClose(TokenType.RightBrace))
        {
            var block = ParseBlockStatements();
            return new BlockExpr(block) { Line = line, Column = col };
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
        for (int i = _pos; i < _tokens.Count; i++)
        {
            var t = _tokens[i];
            if (t.Type == TokenType.Newline) return true;
            if (t.Type == TokenType.LeftBrace) depth++;
            if (t.Type == closing) { depth--; if (depth == 0) return false; }
            if (t.Type == TokenType.EndOfFile) return false;
        }
        return false;
    }

    /// <summary>前瞻：在匹配 closing 之前是否遇到运算符 token</summary>
    private bool HasOperatorsBeforeClose(TokenType closing)
    {
        int depth = 1;
        for (int i = _pos; i < _tokens.Count; i++)
        {
            var t = _tokens[i];
            if (t.Type is TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash
                or TokenType.Percent or TokenType.Equal or TokenType.EqualEqual or TokenType.NotEqual
                or TokenType.Less or TokenType.Greater or TokenType.LessEqual or TokenType.GreaterEqual
                or TokenType.Bang or TokenType.AndAnd or TokenType.OrOr or TokenType.Pipe
                or TokenType.PlusEqual or TokenType.MinusEqual or TokenType.StarEqual
                or TokenType.SlashEqual or TokenType.PercentEqual
                or TokenType.ColonEqual or TokenType.PipeLeft)
                return true;
            if (t.Type == TokenType.LeftBrace) depth++;
            if (t.Type == closing) { depth--; if (depth == 0) return false; }
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
            if (Match(TokenType.Newline)) continue;
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
            if (Match(TokenType.Newline)) continue;
        }
        Consume(TokenType.RightBrace, "字典条目后需要 '}'");
        return new DictLiteral(entries) { Line = line, Column = col };
    }

    /// <summary>强制解析为代码块</summary>
    private BlockExpr ParseMandatoryBlock(string context)
    {
        int line = Previous().Line, col = Previous().Column;
        Consume(TokenType.LeftBrace, $"需要 '{{' for {context}");
        var stmts = ParseBlockStatements();
        return new BlockExpr(stmts) { Line = line, Column = col };
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
            throw ParseError("Empty block '{ }' is not allowed");

        return list;
    }
    // ========================================
    //  辅助
    // ========================================

    private bool StartsPrimary()
    {
        if (IsAtEnd()) return false;
        return Peek().Type is TokenType.Number or TokenType.String or TokenType.Identifier
                            or TokenType.LeftParen or TokenType.LeftBracket
                            or TokenType.LeftBrace;
    }

    private Token Peek() => _tokens[_pos];
    private Token Previous() => _tokens[_pos - 1];
    private bool IsAtEnd() => _pos >= _tokens.Count || _tokens[_pos].Type == TokenType.EndOfFile;

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;
    private bool CheckNext(TokenType type)
    {
        if (_pos + 1 >= _tokens.Count) return false;
        return _tokens[_pos + 1].Type == type;
    }

    private bool Match(TokenType type)
    {
        if (Check(type)) { _pos++; return true; }
        return false;
    }

    private Token Consume(TokenType type, string errorMessage)
    {
        if (Check(type)) return _tokens[_pos++];
        throw ParseError(errorMessage);
    }

    private void SkipNewlines() { while (Match(TokenType.Newline)) { } }

    // ========================================
    //  _ 占位符消糖
    // ========================================

    static bool HasHoles(Expression e) => e switch
    {
        HoleExpr => true,
        BinaryExpr b => HasHoles(b.Left) || HasHoles(b.Right),
        UnaryExpr u => HasHoles(u.Operand),
        CallExpr c => HasHoles(c.Function) || c.Arguments.Any(HasHoles),
        MemberAccess m => HasHoles(m.Object),
        PipeExpr p => HasHoles(p.Left) || HasHoles(p.Right),
        ListLiteral l => l.Elements.Any(HasHoles),
        _ => false
    };

    Expression DesugarHoles(Expression e)
    {
        if (!HasHoles(e)) return e;
        // 收集所有 hole 索引，按出现顺序
        var holes = new List<int>();
        CollectHoles(e, holes);
        // 去重保持顺序
        var uniq = holes.Distinct().ToList();
        // 替换 HoleExpr → IdentifierExpr
        var body = ReplaceHoles(e, uniq.Count);
        // 嵌套 lambda：最外层参数对应第一个 hole
        for (int i = uniq.Count - 1; i >= 0; i--)
        {
            var block = new BlockExpr([new ExpressionStatement(body) { Line = body.Line, Column = body.Column }])
            { Line = body.Line, Column = body.Column };
            body = new LambdaExpr(new Parameter("_" + i, "object"), block) { Line = body.Line, Column = body.Column };
        }
        return body;
    }

    static void CollectHoles(Expression e, List<int> holes)
    {
        switch (e)
        {
            case HoleExpr h: holes.Add(h.Index); break;
            case BinaryExpr b: CollectHoles(b.Left, holes); CollectHoles(b.Right, holes); break;
            case UnaryExpr u: CollectHoles(u.Operand, holes); break;
            case CallExpr c: CollectHoles(c.Function, holes); foreach (var a in c.Arguments) CollectHoles(a, holes); break;
            case MemberAccess m: CollectHoles(m.Object, holes); break;
            case PipeExpr p: CollectHoles(p.Left, holes); CollectHoles(p.Right, holes); break;
            case ListLiteral l: foreach (var el in l.Elements) CollectHoles(el, holes); break;
        }
    }

    static Expression ReplaceHoles(Expression e, int count)
    {
        return e switch
        {
            HoleExpr h => new IdentifierExpr("_" + h.Index) { Line = e.Line, Column = e.Column },
            BinaryExpr b => new BinaryExpr(ReplaceHoles(b.Left, count), b.Op, ReplaceHoles(b.Right, count)) { Line = e.Line, Column = e.Column },
            UnaryExpr u => new UnaryExpr(u.Op, ReplaceHoles(u.Operand, count)) { Line = e.Line, Column = e.Column },
            CallExpr c => new CallExpr(ReplaceHoles(c.Function, count), c.Arguments.Select(a => ReplaceHoles(a, count)).ToList()) { Line = e.Line, Column = e.Column },
            MemberAccess m => new MemberAccess(ReplaceHoles(m.Object, count), m.Member) { Line = e.Line, Column = e.Column },
            PipeExpr p => new PipeExpr(ReplaceHoles(p.Left, count), ReplaceHoles(p.Right, count)) { Line = e.Line, Column = e.Column },
            ListLiteral l => new ListLiteral(l.Elements.Select(el => ReplaceHoles(el, count)).ToList()) { Line = e.Line, Column = e.Column },
            _ => e
        };
    }

    private Exception ParseError(string message)
    {
        var token = IsAtEnd() ? _tokens[^1] : Peek();
        return new Exception($"语法错误 {token.Line}:{token.Column}: {message}\n  附近: {token.Lexeme}");
    }
}
