namespace Ravel;

using System.Linq;
using Ravel.Runtime;

public partial class Parser
{
    //  表达式 — 按优先级递归下降

    private Expression ParseExpression() => ParseExpression(Postfix.Full);

    private Expression ParseExpression(Postfix policy)
    {
        using var _expr = ExprDepth.Begin(this);
        return ParsePipe(policy);
    }

    private Expression ParsePipe(Postfix policy)
    {
        var left = ParseAssignment(policy);

        if (Match(TokenType.PipeLeft))
        {
            BumpExprDepth();
            var right = ParsePipe(policy);
            if (HasHoles(right)) right = DesugarHoles(right);
            return new PipeExpr(left, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    private Expression ParseAssignment(Postfix policy)
    {
        var left = ParseNullCoalesce(policy);

        if (Match(TokenType.CoalesceEqual))
        {
            var op = Previous();
            if (left is not (IdentifierExpr or MemberAccess))
                throw ParseError("'??=' 的左边得是变量或成员（就像 '=' 那样）");
            BumpExprDepth();
            var right = ParseAssignment(policy);
            return new BinaryExpr(left, op.Lexeme, right) { Line = left.Line, Column = left.Column };
        }

        if (Match(TokenType.Equal) || Match(TokenType.ColonEqual) || Match(TokenType.PlusEqual) ||
            Match(TokenType.MinusEqual) || Match(TokenType.StarEqual) || Match(TokenType.StarStarEqual) ||
            Match(TokenType.SlashEqual) || Match(TokenType.PercentEqual))
        {
            var op = Previous().Lexeme;
            BumpExprDepth();
            var right = ParseAssignment(policy);
            return new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    private Expression ParseNullCoalesce(Postfix policy)
    {
        var left = ParseBlockArgs(policy);
        if (!Match(TokenType.Coalesce)) return left;

        BumpExprDepth();
        var op = Previous();
        var right = ParseNullCoalesce(policy);
        return new BinaryExpr(left, op.Lexeme, right) { Line = left.Line, Column = left.Column };
    }

    private Expression ParseBlockArgs(Postfix policy)
    {
        var e = ParseClimb(policy, 0);
        if (!policy.Args) return e;

        //
        if (moreControlFlow && e is IdentifierExpr { Name: "while" })
            return ParsePostfixRest(e, policy.WithBlocks);

        while (Check(TokenType.LeftBrace))
        {
            e = new CallExpr(e, ParsePrimary()) { Line = e.Line, Column = e.Column };
            e = ParsePostfixRest(e, policy);         // 块后面还能接普通的并列实参
        }

        return e;
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

    private LambdaExpr Thunk(Expression e, Token at)
        => new(new Parameter("_", new IdentifierExpr("void") { Line = at.Line, Column = at.Column }), AsBlock(e, at))
            { Line = at.Line, Column = at.Column, Sugar = true };

    private static IdentifierExpr Ident(string name, Token at) => new(name) { Line = at.Line, Column = at.Column };

    private static CallExpr Call(Expression fn, Expression arg, Token at)
        => new(fn, arg) { Line = at.Line, Column = at.Column };

    private static bool IsInfixWordOperator(Token t)
        => t.Type == TokenType.Identifier && Operators.TryWord(t.Lexeme, out _);

    internal static bool IsInfixOperator(Token t)
        => Operators.IsInfixToken(t.Type) || IsInfixWordOperator(t)
        || t.Type is TokenType.Coalesce or TokenType.PipeInto;

    private Expression ParseClimb(Postfix policy, int minBp)
    {
        using var _climb = ExprDepth.Begin(this);
        var left = ParsePower(policy);

        while (true)
        {
            var t = Peek();
            int lbp, rbp;

            if (Operators.TryToken(t.Type, out var info))
                (lbp, rbp) = (info.Lbp, info.Rbp);
            else if (IsInfixWordOperator(t))
            {
                Operators.TryWord(t.Lexeme, out var infoOfWord);
                // 一个字的线索都没有,还指着语句开头。)
                if (!StartsPrimaryAt(1) && !IsOperatorToken(TypeAt(1)))
                    throw ParseError($"'{t.Lexeme}' 是中缀运算符，后面得跟个操作数"
                                   + $"（想传一个叫 `{t.Lexeme}` 的值就加括号：`({t.Lexeme})`）");
                (lbp, rbp) = (infoOfWord.Lbp, infoOfWord.Rbp);
            }
            else break;

            if (lbp <= minBp) break;

            _pos++;

            if (Check(TokenType.PipeLeft))
            {
                _pos++;
                BumpExprDepth();
                var sealedRight = ParsePipe(policy);
                if (HasHoles(sealedRight)) sealedRight = DesugarHoles(sealedRight);
                left = new BinaryExpr(left, t.Lexeme, sealedRight) { Line = t.Line, Column = t.Column };
                continue;
            }
            BumpExprDepth();                        // 链上又挂了一节,树也跟着长一层
            var op = t.Lexeme;
            var right = ParseClimb(policy, rbp);
            if (IsCompareOp(op))
            {
                if (left is BinaryExpr { Parenthesized: false } prev && IsCompareOp(prev.Op))
                {
                    left = new ChainCompare([prev.Left, prev.Right, right], [prev.Op, op])
                        { Line = left.Line, Column = left.Column };
                    continue;
                }

                if (left is ChainCompare { Parenthesized: false } chain)
                {
                    chain.Operands.Add(right);
                    chain.Ops.Add(op);
                    continue;
                }
            }

            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    private static bool IsCompareOp(string op) => Operators.IsCompare(op);


    private Expression ParsePower(Postfix policy)
    {
        var left = ParseCall(policy);

        if (Operators.IsPowerLike(Peek().Type))
        {
            BumpExprDepth();                        // 右结合
            var t = Peek();
            _pos++;
            var right = ParsePower(policy);
            return new BinaryExpr(left, t.Lexeme, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    private Expression ParseCall(Postfix policy)
    {
        //
        if (policy.Args && Check(TokenType.Minus) && BarePatternLambdaAhead(1))
            return ParseBarePatternLambda();

        if (Match(TokenType.Bang) || Match(TokenType.Minus))
        {
            BumpExprDepth();
            var op = Previous();
            return new UnaryExpr(op.Lexeme, ParsePower(policy)) { Line = op.Line, Column = op.Column };
        }

        // .成员访问 — 在空格调用之前处理
        var expr = ParseMemberChain(ParsePrimary(), policy);
        return ParsePostfixRest(expr, policy);
    }

    private void BumpExprDepth()
    {
        if (++_exprDepth > MaxExprDepth)
            throw ParseError($"表达式嵌套太深（超过 {MaxExprDepth} 层运算符/调用链）");
    }

    private Expression ParsePostfixRest(Expression expr, Postfix policy, bool guarded = false)
    {
        //
        while (AdjacentToPrevious()
               && (Check(TokenType.LeftBracket) || (Check(TokenType.LeftParen) && RangeInsideParens())))
            expr = ParseIndex(expr);

        if (!policy.Args && !policy.Pipe) return expr;

        LoopCtx? loop = null;

        while (true)
        {
            //
            //
            //
            if (Check(TokenType.PipeInto))
            {
                if (!policy.Pipe) break;               // 右边那半不吃下一个 `|>`
                _pos++;
                BumpExprDepth();                       // `a |> f |> g`:一层套一层

                if (Check(TokenType.Dot) || Check(TokenType.QuestionDot))
                {
                    expr = ParseMemberChain(expr, policy, guarded);
                    continue;
                }

                if (IsOperatorToken(Peek().Type)
                    && Peek().Type is not (TokenType.Colon or TokenType.PipeInto))
                {
                    var sym = Peek();
                    _pos++;
                    BumpExprDepth();
                    var right = Operators.IsPowerLike(sym.Type)
                        ? ParsePower(policy)
                        : ParseClimb(policy, Operators.TryToken(sym.Type, out var pipeOp) ? pipeOp.Rbp : 0);
                    expr = new BinaryExpr(expr, sym.Lexeme, right) { Line = sym.Line, Column = sym.Column };
                    continue;
                }

                if (!StartsPrimary() && !IsOperatorToken(Peek().Type))
                    throw ParseError("'|>' 后面得跟点什么（`a |> .f ()` 是挂成员，`a |> g` 是把 a 喂给 g）");

                //
                Expression Clause()
                {
                    var one = ParseMemberChain(ParsePrimary(), policy);
                    return ParsePostfixRest(one, policy.NoPipe);
                }

                var left = expr;
                var fn = Clause();

                while (Match(TokenType.Pipe))
                {
                    BumpExprDepth();
                    var bar = Previous();
                    fn = new BinaryExpr(fn, "|", Clause()) { Line = bar.Line, Column = bar.Column };
                }

                if (HasHoles(fn)) fn = DesugarHoles(fn);

                expr = new CallExpr(fn, left) { Line = left.Line, Column = left.Column };
                continue;
            }

            if (!policy.Args || !StartsPrimary() || IsInfixWordOperator(Peek())
                || (!policy.Block && Check(TokenType.LeftBrace))) break;

            //
            //
            //
            //
            var bodyAt = expr switch
            {
                IdentifierExpr { Name: "while" } => 2,
                MemberAccess { Member: "Foreach" } => 1,
                _ => 0,
            };
            if (moreControlFlow && loop is null && bodyAt != 0)
            {
                loop = new LoopCtx { Label = _pendingLabel, Brk = _pendingBrk ?? "", BodyAt = bodyAt };
                if (_pendingLabel is not null) _pendingLabel = null;
                _loops.Add(loop);
            }

            var bareUnit = Check(TokenType.LeftParen) && TypeAt(1) == TokenType.RightParen;

            var arg = ParseAssignment(Postfix.Bare);

            var at = Previous();

            if (loop is not null && ++loop.BodySeen == loop.BodyAt)
            {
                if (loop.ContUsed) arg = WrapLoopBody(arg, loop.Cont, at);
                expr = new CallExpr(expr, arg)
                {
                    Line = expr.Line,
                    Column = expr.Column,
                };
                if (loop.BrkUsed && loop.Label is null) expr = WrapLoopWhole(expr, loop.Brk, at);
                _loops.RemoveAt(_loops.Count - 1);
                loop = null;
                continue;
            }

            expr = new CallExpr(expr, arg)
            {
                Line = expr.Line,
                Column = expr.Column,
                BareUnitArg = bareUnit,
            };
        }

        return expr;
    }

    private Expression ParseMemberChain(Expression e, Postfix policy, bool guarded = false)
    {
        while (true)
        {
            if (Match(TokenType.Dot))
            {
                BumpExprDepth();                    // `a.b.c…`:成员访问也是一层接一层
                var member = ParseMemberName();
                e = new MemberAccess(e, member) { Line = e.Line, Column = e.Column };
                continue;
            }

            if (Check(TokenType.QuestionDot))
            {
                var q = Peek();
                _pos++;

                if (guarded)
                {
                    BumpExprDepth();
                    var m = ParseMemberName();
                    e = new MemberAccess(e, m) { Line = q.Line, Column = q.Column };
                    continue;
                }

                return GuardedChain(e, policy, q);
            }

            break;
        }

        return e;
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
