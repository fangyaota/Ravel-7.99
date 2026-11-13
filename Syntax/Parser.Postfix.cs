namespace Ravel;

public partial class Parser
{
    // 后缀 — 下标 / 管道 / 成员 / 实参

    // 下标先贴:要紧挨着,`(…)` 里得有 `..`。空方括号(`s[]`)不是下标,是"转成列表",
    // 留给 ParseAdjacentCallAndConvert。
    private Expression ParseIndexPostfixes(Expression expr)
    {
        while (AdjacentToPrevious())
        {
            if (Check(TokenType.LeftBracket) && TypeAt(1) != TokenType.RightBracket)
            {
                expr = ParseIndex(expr);
                continue;
            }

            if (Check(TokenType.LeftParen) && RangeInsideParens())
            {
                expr = ParseIndex(expr);
                continue;
            }

            break;
        }

        return expr;
    }

    // 一层层贴:谁认领谁贴,都不认领就收手
    private Expression ParsePostfixRest(Expression expr, Postfix policy, bool guarded = false)
    {
        expr = ParseIndexPostfixes(expr);

        // 紧贴的那几种后缀在 `Bare` 下也认 —— 它们不是"并列实参",是同一个原子的延续。
        expr = ParseAdjacentCallAndConvert(expr, policy);

        if (!policy.Args && !policy.Pipe) return expr;

        LoopCtx? loop = null;

        while (true)
        {
            if (TryPostfixPipeInto(expr, policy, guarded) is { } piped)
            {
                expr = piped;
                continue;
            }

            if (TryPostfixArg(expr, policy, ref loop) is not { } called) break;
            expr = called;
        }

        return expr;
    }

    // `|>`:把左边喂给右边三种写法
    private Expression? TryPostfixPipeInto(Expression expr, Postfix policy, bool guarded)
    {
        // 不吃下一个 `|>`:留给外层
        if (!Check(TokenType.PipeInto) || !policy.Pipe) return null;
        _pos++;
        BumpExprDepth();                            // `a |> f |> g`:一层套一层

        if (Check(TokenType.Dot) || Check(TokenType.QuestionDot))
            return ParseMemberChain(expr, policy, guarded);

        if (PipeIntoOperatorAhead()) return ParsePipeIntoOperator(expr, policy);

        return ParsePipeIntoCall(expr, policy);
    }

    // `|>` 后面那个符号是不是运算符
    private bool PipeIntoOperatorAhead()
        => IsOperatorToken(Peek().Type)
           && Peek().Type is not (TokenType.Colon or TokenType.PipeInto);

    // `a |> + 1` 那一形:借一个符号当二元式
    private Expression ParsePipeIntoOperator(Expression left, Postfix policy)
    {
        var sym = Peek();
        _pos++;
        BumpExprDepth();
        var right = Operators.IsPowerLike(sym.Type)
            ? ParsePower(policy)
            : ParseClimb(policy, Operators.TryToken(sym.Type, out var pipeOp) ? pipeOp.Rbp : 0);
        return new BinaryExpr(left, sym.Lexeme, right) { Line = sym.Line, Column = sym.Column };
    }

    // `a |> g`:右边是函数,还可以拿 `|` 拼几个
    private Expression ParsePipeIntoCall(Expression left, Postfix policy)
    {
        if (!StartsPrimary() && !IsOperatorToken(Peek().Type))
            throw ParseError("'|>' 后面得跟点什么（`a |> .f ()` 是挂成员，`a |> g` 是把 a 喂给 g）");

        var fn = ParsePipeIntoClause(policy);

        while (Match(TokenType.Pipe))
        {
            BumpExprDepth();
            var bar = Previous();
            fn = new BinaryExpr(fn, "|", ParsePipeIntoClause(policy)) { Line = bar.Line, Column = bar.Column };
        }

        if (HasHoles(fn)) fn = DesugarHoles(fn);

        return new CallExpr(fn, left) { Line = left.Line, Column = left.Column };
    }

    // `|>` 右手的一格:成员先贴,后缀只吃 `|>`
    private Expression ParsePipeIntoClause(Postfix policy)
    {
        var one = ParseMemberChain(ParsePrimary(), policy);
        return ParsePostfixRest(one, policy.NoPipe);
    }

    // 还吃一个并列实参吗
    private Expression? TryPostfixArg(Expression expr, Postfix policy, ref LoopCtx? loop)
    {
        if (!policy.Args || !StartsPrimary() || IsInfixWordOperator(Peek())
            || IsCompBoundary()                                 // `for`/`if` 是推导式的边界,不是实参
            || IsWord("else")                                   // 三元的 `else` 同理:不是实参
            || (!policy.Block && Check(TokenType.LeftBrace))) return null;

        if (loop is null) loop = OpenPendingLoop(expr);

        var bareUnit = Check(TokenType.LeftParen) && TypeAt(1) == TokenType.RightParen;

        var arg = ParseAssignment(Postfix.Bare);

        var at = Previous();

        if (loop is { } ctx && ++ctx.BodySeen == ctx.BodyAt)
        {
            if (ctx.ContUsed) arg = WrapLoopBody(arg, ctx.Cont, at);
            expr = new CallExpr(expr, arg)
            {
                Line = expr.Line,
                Column = expr.Column,
            };
            if (ctx.BrkUsed && ctx.Label is null) expr = WrapLoopWhole(expr, ctx.Brk, at);
            _loops.RemoveAt(_loops.Count - 1);
            loop = null;
            return expr;
        }

        expr = new CallExpr(expr, arg)
        {
            Line = expr.Line,
            Column = expr.Column,
            BareUnitArg = bareUnit,
        };
        return expr;
    }

    // while/Foreach 的体在第几个实参
    private static int LoopBodyIndex(Expression expr) => expr switch
    {
        IdentifierExpr { Name: "while" } => 2,
        MemberAccess { Member: "Foreach" } => 1,
        _ => 0,
    };

    // 循环体第一格:挂上 LoopCtx
    private LoopCtx? OpenPendingLoop(Expression expr)
    {
        var bodyAt = LoopBodyIndex(expr);
        if (!_moreControlFlow || bodyAt == 0) return null;

        var loop = new LoopCtx { Label = _pendingLabel, Brk = _pendingBrk ?? "", BodyAt = bodyAt };
        if (_pendingLabel is not null) _pendingLabel = null;
        _loops.Add(loop);
        return loop;
    }

    // 紧贴的圆括号就是实参:`f(1)` 是 `f (1)`、`input()` 是 `input ()`。
    // 有空格时(`f (1)`)走下面的"并列实参",结果一样 —— 区别只在"不吃实参"的上下文里
    // (`print f(5)` 的 `print` 用的是 `Postfix.Bare`,紧贴的括号仍然得算调用)。
    //
    // 紧贴的空容器是转换:`s[]` → `s.ToList ()`、`s{}` → `s.ToSet ()`、`s{->}` → `s.ToDict ()`。
    // 有内容就不算:`{ A } { B }` 还是块实参,`xs[0]` 还是下标。
    private Expression ParseAdjacentCallAndConvert(Expression expr, Postfix policy)
    {
        var built = false;

        while (true)
        {
            if (!AdjacentToPrevious()) break;

            // 紧贴调用/转换之后接 `.成员` 也算同一个原子:`f(1).ToString()` 的 `.` 不能断。
            // (有空格的那些不认 —— `print a.b` 仍然只是"取成员的值",见 TryPostfixDot 之前的约定)
            if (built && Check(TokenType.Dot))
            {
                expr = ParseMemberChain(expr, policy);
                built = false;
                continue;
            }

            if (Check(TokenType.LeftParen))
            {
                var q = Peek();
                _pos++;
                BumpExprDepth();
                var unit = Check(TokenType.RightParen);
                var arg = unit
                    ? new VoidLiteral { Line = q.Line, Column = q.Column }
                    : ParseAssignment(Postfix.Full);
                Consume(TokenType.RightParen, "括号实参没有收尾的 ')'");
                expr = new CallExpr(expr, arg)
                {
                    Line = expr.Line,
                    Column = expr.Column,
                    BareUnitArg = unit,
                };
                built = true;
                continue;
            }

            if (Check(TokenType.LeftBracket) && TypeAt(1) == TokenType.RightBracket)
            {
                expr = new ContainerConvert(expr, ConvertKind.List) { Line = expr.Line, Column = expr.Column };
                _pos += 2;
                built = true;
                continue;
            }

            if (Check(TokenType.LeftBrace))
            {
                if (TypeAt(1) == TokenType.RightBrace)
                {
                    expr = new ContainerConvert(expr, ConvertKind.Set) { Line = expr.Line, Column = expr.Column };
                    _pos += 2;
                    built = true;
                    continue;
                }

                if (TypeAt(1) == TokenType.DictArrow && TypeAt(2) == TokenType.RightBrace)
                {
                    expr = new ContainerConvert(expr, ConvertKind.Dict) { Line = expr.Line, Column = expr.Column };
                    _pos += 3;
                    built = true;
                    continue;
                }
            }

            break;
        }

        return expr;
    }

    private bool AdjacentToPrevious()
    {
        var prev = Previous();
        var next = Peek();
        return prev.Line == next.Line && prev.Column + prev.Length == next.Column;
    }

    private Expression ParseIndex(Expression expr)
    {
        var opener = Peek();
        _pos++;                                     // `[` 或 `(`

        Expression index;
        if (opener.Type == TokenType.LeftParen)
        {
            index = TryParseRange(false)
                ?? throw ParseError("'(' 开头的索引得是区间（`xs(0..3)`）");
        }
        else
        {
            index = TryParseRange(true) ?? ParseIndexExpression();
        }

        return new IndexExpr(expr, index) { Line = expr.Line, Column = expr.Column };
    }

    private Expression ParseIndexExpression()
    {
        var index = ParseExpression();
        SkipNewlines();
        Consume(TokenType.RightBracket, "索引要一个 ']' 收尾（`xs[0]`）");
        return index;
    }

    private bool RangeInsideParens()
    {
        var depth = 0;
        for (var i = _pos + 1; i < tokens.Count; i++)
        {
            switch (tokens[i].Type)
            {
                case TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace:
                    depth++;
                    break;
                case TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace:
                    if (depth == 0) return false;
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

    // 成员链:`.名字` 与 `?.`
    private Expression ParseMemberChain(Expression e, Postfix policy, bool guarded = false)
    {
        while (true)
        {
            if (TryPostfixDot(ref e)) continue;

            if (!Check(TokenType.QuestionDot)) break;

            if (guarded)
            {
                ParseGuardedDot(ref e);
                continue;
            }

            return OpenGuardedChain(e, policy);
        }

        return e;
    }

    // `a.b.c…`:成员访问也是一层接一层
    private bool TryPostfixDot(ref Expression e)
    {
        if (!Match(TokenType.Dot)) return false;
        BumpExprDepth();
        var member = ParseMemberName();
        e = new MemberAccess(e, member) { Line = e.Line, Column = e.Column };
        return true;
    }

    // 守卫链里 `?.` 就是一节成员
    private void ParseGuardedDot(ref Expression e)
    {
        var q = Peek();
        _pos++;

        BumpExprDepth();
        var m = ParseMemberName();
        e = new MemberAccess(e, m) { Line = q.Line, Column = q.Column };
    }

    // 非守卫时 `?.` 整条链转成 Map
    private Expression OpenGuardedChain(Expression e, Postfix policy)
    {
        var q = Peek();
        _pos++;
        return GuardedChain(e, policy, q);
    }

    private Expression GuardedChain(Expression receiver, Postfix policy, Token q)
    {
        var name = "_nil" + _nilCount++;
        var id = new IdentifierExpr(name) { Line = q.Line, Column = q.Column };
        Expression rest = new MemberAccess(id, ParseMemberName()) { Line = q.Line, Column = q.Column };
        rest = ParseMemberChain(rest, policy, guarded: true);
        rest = ParsePostfixRest(rest, policy, guarded: true);

        var step = new LambdaExpr(new Parameter(name, ObjectType(q)), AsBlock(rest, q))
            { Line = q.Line, Column = q.Column, Sugar = true };
        return Call(new MemberAccess(receiver, "Map") { Line = q.Line, Column = q.Column }, step, q);
    }
}
