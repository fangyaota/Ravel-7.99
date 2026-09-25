namespace Ravel;

using System.Linq;
using Ravel.Runtime;

/// <summary>递归下降解析器,按优先级链逐层收窄(见 ParseExpression 起的各层)。
/// `_` 占位符的消糖是独立的一趟 AST 改写,在 Parser.Holes.cs。</summary>
public partial class Parser(List<Token> tokens, string? source = null)
{
    private int _pos;

    private int _holeCount;

    /// <summary>当前括号/块的嵌套深度(见 Nested)</summary>
    private int _depth;
    private const int MaxDepth = 400;

    // ========================================
    //  入口
    // ========================================

    /// <summary>词法 + 语法一步到位(调用方不必重复 new Lexer/new Parser 两行)。
    /// fileName 会挂到块上,求值器报错时用它指出是哪个文件。</summary>
    public static Program ParseSource(string source, string? fileName = null)
        => new Parser(new Lexer(source, fileName).Tokenize(), fileName).Parse();

    /// <summary>同上,但结果作为块(模块体 / eval 代码片段用)</summary>
    public static BlockExpr ParseBlock(string source, string? fileName = null)
        => new(ParseSource(source, fileName).Statements) { Source = fileName };

    public Program Parse()
    {
        var statements = new List<Statement>();

        while (!IsAtEnd())
        {
            if (Match(TokenType.Newline))
                continue;
            statements.Add(ParseStatement());
        }

        return new Program(statements) { Source = source };
    }

    // ========================================
    //  语句
    // ========================================

    // 注意 init 不在此列:构造器是名字叫 init 的变量(类体里写 `init := ...`),不是修饰符
    private static bool IsMod(string kw) => Attr.All.Contains(kw);

    /// <summary>能作为运算符定义的符号 token(`+ := f` / `a.+`)。一元 `!` 和短路 `&&`/`||` 不在内——
    /// 它们是求值器特判的,不支持自定义。</summary>
    private static bool IsOperatorToken(TokenType t) => t switch
    {
        TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent
            or TokenType.EqualEqual or TokenType.NotEqual
            or TokenType.Less or TokenType.Greater or TokenType.LessEqual or TokenType.GreaterEqual
            or TokenType.And or TokenType.Pipe or TokenType.Caret => true,
        _ => false,
    };

    /// <summary>类机制内部词——禁止作为变量名（this/init 是类内可用变量，不禁）</summary>
    private static readonly HashSet<string> ReservedWords = ["thistype", "block", "core"];

    private Statement ParseStatement()
    {
        // 运算符定义:`+ := f`(定义) / `+ = f`(覆盖)。符号本身就是成员名。
        if (IsOperatorToken(Peek().Type) && _pos + 1 < tokens.Count)
        {
            var nt = tokens[_pos + 1].Type;
            if (nt is TokenType.ColonEqual or TokenType.Equal)
                return ParseOperatorDefinition();
        }

        // 旧写法 operator+ add := ... 已废弃。不拦的话会被当表达式 `operator + add` 求值,
        // 报的却是「未定义的变量 'operator'」,看不出所以然。
        if (Check(TokenType.Identifier) && Peek().Lexeme == "operator" &&
            _pos + 1 < tokens.Count && IsOperatorToken(tokens[_pos + 1].Type))
            throw ParseError("运算符定义已改成直接用符号：`+ := f`（定义）或 `+ = f`（覆盖）");

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
                if (_pos + 1 < tokens.Count)
                {
                    var nt = tokens[_pos + 1].Type;
                    if (nt == TokenType.ColonEqual || nt == TokenType.ColonColonEqual || nt == TokenType.ColonColon ||
                        nt == TokenType.Colon)
                        break;
                }

                _pos++;
                attrs.Add(kw);
            }
            else break;
        }

        if (attrs.Count > 0)
        {
            if (Check(TokenType.Identifier) && (CheckNext(TokenType.ColonEqual) || CheckNext(TokenType.Colon) ||
                                                CheckNext(TokenType.ColonColonEqual) ||
                                                CheckNext(TokenType.ColonColon)))
                return ParseDefinition(attrs);
            if (Check(TokenType.ColonEqual) || Check(TokenType.Colon) || Check(TokenType.ColonColonEqual) ||
                Check(TokenType.ColonColon))
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

    /// <summary>`.` 后面那一段:普通成员名,或运算符符号(`2.+` / `"a".==`)</summary>
    private string ParseMemberName()
    {
        if (Check(TokenType.Identifier))
        {
            var t = Peek();
            _pos++;
            return t.Lexeme;
        }

        if (IsOperatorToken(Peek().Type))
        {
            var t = Peek();
            _pos++;
            return t.Lexeme;
        }

        throw ParseError("'.' 后需要成员名");
    }

    /// <summary>`+ := f` 定义运算符;`+ = f` 覆盖已有的(父类层定义的)那个。符号即成员名。</summary>
    private Statement ParseOperatorDefinition()
    {
        var sym = Peek();
        _pos++;
        if (Match(TokenType.ColonEqual))
            return new VarDefinition(sym.Lexeme, null, ParseExpression()) { Line = sym.Line, Column = sym.Column };

        Match(TokenType.Equal);
        return new Assignment(sym.Lexeme, ParseExpression()) { Line = sym.Line, Column = sym.Column };
    }

    /// <summary>name := expr  |  name: Type = expr</summary>
    private Statement ParseDefinition(List<string>? attrs = null)
    {
        string name;
        int line, col;
        if (attrs is { Count: > 0 })
        {
            if (Check(TokenType.Identifier))
            {
                var n = Consume(TokenType.Identifier, "需要变量名");
                name = n.Lexeme;
                line = n.Line;
                col = n.Column;
            }
            else
            {
                // 修饰符本身当名字用(如 `readonly := ...`)
                name = attrs[0];
                line = Previous().Line;
                col = Previous().Column;
            }
        }
        else
        {
            var nameToken = Consume(TokenType.Identifier, "需要变量名");
            name = nameToken.Lexeme;
            line = nameToken.Line;
            col = nameToken.Column;
        }

        string? typeAnnotation = null;
        bool autoName = false;

        if (ReservedWords.Contains(name))
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

    /// <summary>赋值 = += -= *= /= 与成员定义 :=  （右结合，仅表达式级别）。
    /// 顶层语句的 `x := v` 在 ParseStatement 就分派给 ParseDefinition 了，
    /// 这里收的是左操作数为成员访问的 `obj.field := v`（定义/覆盖字段）。</summary>
    private Expression ParseAssignment(bool allowCall = true)
    {
        var left = ParseLogic(allowCall);

        if (Match(TokenType.Equal) || Match(TokenType.ColonEqual) || Match(TokenType.PlusEqual) ||
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
            var member = ParseMemberName();
            expr = new MemberAccess(expr, member) { Line = expr.Line, Column = expr.Column };
        }

        if (!allowCall) return expr;

        // f a b c  →  ((f a) b) c  柯里化
        while (StartsPrimary())
        {
            var arg = ParsePrimary();
            while (Match(TokenType.Dot))
            {
                var mem = ParseMemberName();
                arg = new MemberAccess(arg, mem) { Line = arg.Line, Column = arg.Column };
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

        throw ParseError($"需要表达式，但得到 {Peek()}");
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
    private BlockExpr ParseMandatoryBlock(string context)
    {
        int line = Previous().Line, col = Previous().Column;
        Consume(TokenType.LeftBrace, $"需要 '{{' for {context}");
        if (!HasNewlineBeforeClose(TokenType.RightBrace))
            throw ParseError($"{context} 需要代码块（用换行或分号分隔）");
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

    private Token Peek() => tokens[_pos];
    private Token Previous() => tokens[_pos - 1];
    private bool IsAtEnd() => _pos >= tokens.Count || tokens[_pos].Type == TokenType.EndOfFile;

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

    private bool CheckNext(TokenType type)
    {
        if (_pos + 1 >= tokens.Count) return false;
        return tokens[_pos + 1].Type == type;
    }

    private bool Match(TokenType type)
    {
        if (Check(type))
        {
            _pos++;
            return true;
        }

        return false;
    }

    private Token Consume(TokenType type, string errorMessage)
    {
        if (Check(type)) return tokens[_pos++];
        throw ParseError(errorMessage);
    }

    private void SkipNewlines()
    {
        while (Match(TokenType.Newline))
        {
        }
    }

    /// <summary>位置不再写进消息里——ErrorReport 会画 `--> file:line:col` 和插入符,
    /// 重复写一遍只是噪音。`附近` 也由插入符接管(读不到源文件的 eval 片段除外)。</summary>
    private SyntaxException ParseError(string message)
    {
        var token = IsAtEnd() ? tokens[^1] : Peek();
        return new SyntaxException(message, new SourceSpot(source, token.Line, token.Column));
    }
}
