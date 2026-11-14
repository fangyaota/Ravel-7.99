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
        // `do { … }`:Monad 串联糖
        if (Check(TokenType.Identifier) && Peek().Lexeme == "do" && NextType() == TokenType.LeftBrace)
            return Nested(ParseDo);

        // `by a`:取槽里那份 property,不过 getter;只在表达式位置认
        if (Check(TokenType.Identifier) && Peek().Lexeme == "by" && NextType() == TokenType.Identifier)
        {
            _pos++;                                     // by
            return new SlotExpr(ParseMemberChain(ParsePrimary(), Postfix.Full)) { Line = Previous().Line, Column = Previous().Column };
        }

        // @ 现在只是标签前缀,不再封口
        if (Check(TokenType.At))
            throw ParseError(Messages.T("parse.parser.label_prefix_misplaced"));

        // 运算符节 `+.2`:左操作数留空,脱糖成 lambda
        if (IsSectionStart())
        {
            var sym = Peek();
            _pos += 2;
            _holeCount++;
            var hole = new HoleExpr(_holeCount - 1) { Line = sym.Line, Column = sym.Column };
            var body = new BinaryExpr(hole, sym.Lexeme, ParseCall(Postfix.Bare))
                { Line = sym.Line, Column = sym.Column };
            // 就地脱糖:节是闭包边界,外层别再包
            return DesugarHoles(body);
        }

        // ~x 就是 {x;}:只吃一个原子,体到它为止
        if (Match(TokenType.Tilde))
        {
            BumpExprDepth();                        // `~ ~ ~x`:一层一个 `{x;}`
            var tilde = Previous();
            var atom = ParseMemberChain(ParsePrimary(bareLambda: false), Postfix.Bare);
            return Block([new ExpressionStatement(atom) { Line = tilde.Line, Column = tilde.Column }], tilde);
        }

        // 裸模式参数:`1 => …` 就是一格模式
        // `()` 故意不在此列;true/false 当字面量
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
                ? new FStringExpr(FStringParts(parts)) { Line = str.Line, Column = str.Column }
                : new StringLiteral(str.Lexeme) { Line = str.Line, Column = str.Column };
        }

        if (Match(TokenType.Char))
            return new CharLiteral(Previous().Lexeme[0])
                { Line = Previous().Line, Column = Previous().Column };

        // `x => 体`:单参 lambda 不写括号;模式里读字面量时得关掉
        if (bareLambda && Check(TokenType.Identifier) && CheckNext(TokenType.Arrow))
            return ParseBareLambda();

        if (Match(TokenType.Identifier))
        {
            var lexeme = Previous().Lexeme;
            if (lexeme == "_" && !_discardIsName)
            {
                _holeCount++;
                return new HoleExpr(_holeCount - 1) { Line = Previous().Line, Column = Previous().Column };
            }

            return new IdentifierExpr(lexeme)
                { Line = Previous().Line, Column = Previous().Column };
        }

        if (Match(TokenType.LeftParen))
            return Nested(MarkParen);

        if (Match(TokenType.LeftBracket))
        {
            int line = Previous().Line, col = Previous().Column;
            return Nested(() =>
            {
                // `[1..3]` 是区间;看不见 `..` 就当列表
                if (TryParseRange(startClosed: true) is { } rng) return rng;

                // `[x for i in xs …]` —— 本层有 `for` 就是推导式
                if (ForAhead())
                {
                    var comp = ParseComprehension(ParseCompHead(), null, CompKind.List);
                    Consume(TokenType.RightBracket, "推导式后需要 ']'");
                    return comp with { Line = line, Column = col };
                }

                var elements = ParseSpaceSeparatedList(TokenType.RightBracket, "]");
                return new ListLiteral(elements) { Line = line, Column = col };
            });
        }

        if (Match(TokenType.LeftBrace))
            return Nested(ParseBrace);

        throw ParseError(Messages.T("parse.parser.expected_expression", Describe(Peek())));
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
        if (stmts.Count == 0) throw ParseError(Messages.T("parse.parser.empty_do_block"));
        if (stmts[^1] is BindStatement) throw ParseError(Messages.T("parse.parser.do_block_last_not_value"));

        return new DoExpr(stmts) { Line = at.Line, Column = at.Column };
    }

    private string FreshParam() => TempNames.Next("__p", source);

    private static IdentifierExpr ObjectType(Token at) => new("object") { Line = at.Line, Column = at.Column };

    private BlockExpr Block(List<Statement> stmts, Token at)
        => new(stmts) { Line = at.Line, Column = at.Column, Source = source };

    private BlockExpr ParseUserLambdaBody(string what, Token at)
    {
        _returnUsed.Add(false);
        // `=>` 后见 `{` 是块,`~x` 也是块,别的是单行简写
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
        if (_depth >= MaxDepth) throw ParseError(Messages.T("parse.parser.expression_nesting_too_deep"));

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

    // 括号是链式比较的"到此为止"记号
    private Expression MarkParen() => ParseParen() with { Parenthesized = true };

    // 四种括号:空单元 / 参数表 / 区间 / 分组
    private Expression ParseParen()
    {
        int line = Previous().Line, col = Previous().Column;

        if (Match(TokenType.RightParen))
            return ParseUnitParen(line, col);

        if (LooksLikeLambdaParams() || IsLambdaAfterParen())
            return ParseLambdaParen(line, col);

        if (TryParseRange(startClosed: false) is { } rng) return rng;

        return ParseGroupedParen();
    }

    // `()` 单写是 void 字面量;`() => …` 的参数就是 `()` 那个值
    private Expression ParseUnitParen(int line, int col)
    {
        if (!Match(TokenType.Arrow))
            return new VoidLiteral { Line = line, Column = col };

        var body = ParseUserLambdaBody("lambda 体", Previous());
        var unit = new LiteralPattern(
            new VoidLiteral { Line = line, Column = col },
            new IdentifierExpr("void") { Line = line, Column = col },
            "()") { Line = line, Column = col };
        // 包一层 Whole:和 `(()) => …` 的树一模一样
        var whole = new WholePattern(unit) { Line = line, Column = col };
        var param = new Parameter(FreshParam(),
            new IdentifierExpr("object") { Line = line, Column = col }, whole)
        { IgnoreType = _ignoreTypes };
        return new LambdaExpr(param, body) { Line = line, Column = col };
    }

    // `(参数…) => …`:参数表,末尾可以是一条守卫
    private Expression ParseLambdaParen(int line, int col)
    {
        var @params = new List<Parameter>();
        var guard = ParseParamList(@params);

        Consume(TokenType.RightParen, "lambda 参数后需要 ')'");
        // #ignoreTypes 之下注解不判,每个参数都打记号
        if (_ignoreTypes) @params = @params.Select(q => q with { IgnoreType = true }).ToList();
        Consume(TokenType.Arrow, "lambda 参数后需要 '=>'");
        var arrow = Previous();
        var body = ParseUserLambdaBody("lambda 体", arrow);
        if (guard is not null) body = PrependGuard(body, guard, @params[^1].Name, arrow);

        return BuildLambda(@params, body, line, col);
    }

    // 参数一项一项读;守卫一并带回
    private Expression? ParseParamList(List<Parameter> @params)
    {
        while (true)
        {
            if (ParseParamItem(@params, out var guard)) return guard;
        }
    }

    // 读一项;返回 true 表示参数表到头了
    private bool ParseParamItem(List<Parameter> @params, out Expression? guard)
    {
        guard = null;
        var at = Peek();
        var saveAt = _pos;
        // 参数前可写修饰符,和变量定义同一张表
        var attrs = CollectModifiers();

        // 模式参数:整个参数按形状拆,参数没有名字
        if (Check(TokenType.LeftBracket) || Check(TokenType.LeftBrace) || IsLiteralStart()
            || Check(TokenType.LeftParen))
        {
            ParsePatternParam(@params, attrs, at);
            SkipNewlines();
            return !StartsParam();
        }

        var pName = Consume(TokenType.Identifier, "lambda 参数需要一个名字（`(x: int) => …`）");

        if (Match(TokenType.Colon))
            ParseTypedParam(@params, pName, attrs);
        else if (Check(TokenType.Identifier) || Check(TokenType.RightParen) || StartsParamGroup(0))
            ParseUntypedParam(@params, pName, attrs);
        else
        {
            guard = ParseGuardParam(@params, at, saveAt, attrs);
            return true;                            // 守卫一定在最后
        }

        SkipNewlines();
        return !StartsParam();
    }

    // `([x y])` / `(1)` / `(())`:一格模式,参数用合成名
    private void ParsePatternParam(List<Parameter> @params, List<string>? attrs, Token at)
        => @params.Add(new Parameter(FreshParam(), ObjectType(at), ParsePattern())
            { Attrs = attrs });

    // `(x: int)`:名字带注解,注解那格挂类型
    private void ParseTypedParam(List<Parameter> @params, Token name, List<string>? attrs)
        => DeclareParam(@params, new Parameter(name.Lexeme, ParseTypeAnnotation()) { Attrs = attrs });

    // `(x)`:注解省了,按 object 收(谁都收得下)
    private void ParseUntypedParam(List<Parameter> @params, Token name, List<string>? attrs)
        => DeclareParam(@params, new Parameter(name.Lexeme, ObjectType(name)) { Attrs = attrs });

    // 参数名不许重:后一个会静默盖掉前一个
    private void DeclareParam(List<Parameter> @params, Parameter p)
    {
        if (@params.Any(q => q.Name == p.Name))
            throw ParseError(Messages.T("parse.parser.duplicate_parameter_name", p.Name));
        @params.Add(p);
    }

    // 守卫:给参数加条件的表达式
    private Expression ParseGuardParam(List<Parameter> @params, Token at, int saveAt, List<string>? attrs)
    {
        // 守卫不声明东西,修饰符没处落
        if (attrs is not null)
            throw ParseError(Messages.T("parse.parser.guard_modifier_not_allowed", attrs[0]));

        _pos = saveAt;
        // 守卫里的 `_` 是参数名,不是占位符
        var prevDiscard = _discardIsName;
        _discardIsName = true;
        Expression guard;
        try
        {
            // 不吃并列实参:否则 `(x < 0 y: int)` 的 y 被吞
            guard = ParsePostfixRest(ParseClimb(Postfix.Bare, 0),
                                     Postfix.PipeOnly);
        }
        finally
        {
            _discardIsName = prevDiscard;
        }

        // 按 token 顺序取名字:`|>` 已折成调用,按树取会取错
        // 到不了:进这一支时参数名那个 Identifier 已经吃掉,条件里最左边必有名字。
        var nm = FirstIdentName(saveAt, _pos)
            ?? throw new InvalidOperationException("引擎内部:守卫那一格找不到可绑的名字");
        DeclareParam(@params, new Parameter(nm, ObjectType(at)));
        return guard;
    }

    // 单参直接返回;多参右结合,一层层套单参 lambda
    private Expression BuildLambda(List<Parameter> @params, BlockExpr body, int line, int col)
    {
        if (@params.Count == 1)
            return new LambdaExpr(@params[0], body) { Line = line, Column = col };

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

    // 分组:括号只界定范围,里面还是表达式
    private Expression ParseGroupedParen()
    {
        // 括号是显式边界:里面能带裸推导式(`[i for i in (j for j in xs)]`)。
        var inner = ParseExpression();
        Consume(TokenType.RightParen, "表达式后需要 ')'");
        // 占位符在括号内就地消糖:`(_ + 1) 41` 的 41 才落在外
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

    // 别在这里拼串:折成 `"" + …` 是 Lowering 的活
    private List<FStringPart> FStringParts(IReadOnlyList<StringPart> parts)
    {
        var list = new List<FStringPart>(parts.Count);
        foreach (var p in parts)
            list.Add(p.IsExpr
                ? new FStringPart("", Parser.ParseExpressionSnippet(p.Text, source, p.Line, p.Column))
                : new FStringPart(p.Text, null));
        return list;
    }

    private Expression ParseSet(int line, int col)
    {
        // `{x for i in xs …}` —— 花括号这一层有 `for` 就是推导式(字典那条路在 ParseDict)
        if (ForAhead())
        {
            var comp = ParseComprehension(ParseCompHead(), null, CompKind.Set);
            Consume(TokenType.RightBrace, "推导式后需要 '}'");
            return comp with { Line = line, Column = col };
        }

        var elements = new List<Expression>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            elements.Add(ParseExpression(Postfix.Bare));
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

            // `{->}` 空字典,只认第一条之前那个
            if (entries.Count == 0 && Check(TokenType.DictArrow))
            {
                Consume(TokenType.DictArrow, "字典键后需要 '->'");
                SkipNewlines();
                break;
            }

            var key = ParseExpression(Postfix.DictKey);
            Consume(TokenType.DictArrow, "字典键后需要 '->'");

            // `{k -> v for i in xs …}` —— 值后面紧跟子句就是字典推导式
            if (ForAhead())
            {
                var comp = ParseComprehension(key, ParseCompHead(), CompKind.Dict);
                Consume(TokenType.RightBrace, "推导式后需要 '}'");
                return comp with { Line = line, Column = col };
            }

            var value = ParseExpression(Postfix.Bare);
            entries.Add(new DictEntry(key, value));
        }

        Consume(TokenType.RightBrace, "字典条目后需要 '}'");
        return new DictLiteral(entries) { Line = line, Column = col };
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
        // `_ => 体`:通配参数,收什么都行;体里的 `_` 仍当洞
        if (_pos > 0 && tokens[_pos - 1].Type == TokenType.Colon)
            throw ParseError(Messages.T("parse.parser.bare_lambda_annotation"));
        _pos++;
        Consume(TokenType.Arrow, "lambda 参数后需要 '=>'");
        var body = ParseUserLambdaBody("lambda 体", Previous());
        return new LambdaExpr(new Parameter(name.Lexeme, ObjectType(name)), body)
            { Line = name.Line, Column = name.Column };
    }

    private BlockExpr PrependGuard(BlockExpr body, Expression guard, string param, Token at)
    {
        // 只把 TypeError 当"不收",别的错原样往上报
        // 重抛走 System.Unhandled:位置和调用栈都还在
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
        // 拒收是引擎机制,不挂小写全局名
        var rejected = Call(Ident("reject", at),
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

    private BlockExpr ParseMandatoryBlock(string context)
    {
        int line = Previous().Line, col = Previous().Column;
        Consume(TokenType.LeftBrace, $"{context}需要 '{{' 开头");
        // 只问"像不像块":有 '->' 也算块
        if (!AnyNewlineBeforeClose(TokenType.RightBrace))
            throw ParseError(Messages.T("parse.parser.expected_block", context));
        var stmts = ParseBlockStatements();
        return new BlockExpr(stmts) { Line = line, Column = col, Source = source };
    }

    private Expression? TryParseRange(bool startClosed)
    {
        // 先挡一道:这一层没有 `..` 就免谈
        if (!HasDotDotAhead()) return null;

        var save = _pos;
        try
        {
            // 省了起点:`[..1]`
            var lo = Check(TokenType.DotDot) ? null : ParseExpression();
            var dot = Peek();                        // 两头都省时用来定位
            if (!Match(TokenType.DotDot))
            {
                _pos = save;
                return null;
            }

            // 省了终点:`[1..]`
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

            // 位置取实有的那头;两头都省就指那个 `..`
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

    // ========================================
    //  列表解析辅助
    // ========================================

    private List<Expression> ParseSpaceSeparatedList(TokenType closing, string closingName)
    {
        var list = new List<Expression>();
        if (Match(closing)) return list;

        while (true)
        {
            list.Add(ParseExpression(Postfix.Bare));
            if (Match(closing)) return list;
            if (Check(TokenType.Comma))
                throw ParseError(Messages.T("parse.parser.comma_not_allowed"));
            if (Match(TokenType.Newline)) continue;
            if (IsAtEnd())
                throw ParseError(Messages.T("parse.parser.expected_list_terminator", closingName));
        }
    }

    private List<Statement> ParseBlockStatements()
    {
        SkipNewlines();
        // `{;}` 是空体,展开成 `{();}`;`{ }` 仍不许
        if (Previous().Type == TokenType.Newline && Check(TokenType.RightBrace))
        {
            var at = Previous();
            _pos++;
            return [new ExpressionStatement(new LiteralExpr(VoidVal.Instance) { Line = at.Line, Column = at.Column })
                { Line = at.Line, Column = at.Column }];
        }

        var list = new List<Statement>();
        // 块是新语境:外层的 `:<` 不该漏进来
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
            // 到不了:空体在方法开头就被 `{;}` 那条收走了,`{ }` 连块都不算。
            throw new InvalidOperationException("引擎内部:块里一条语句都没有");

        return list;
    }
}
