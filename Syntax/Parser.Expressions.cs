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
        var e = ParsePipe(policy);
        // 裸推导式(`x for i in xs` 那种不带容器的形式):后缀,所以整条式子之后才接。
        // 要不要由 policy.Comp 决定(包裹语法的值用 CompHead,见 Parser.Comprehensions)。
        return policy.Comp && IsWord("for")
            ? ParseComprehension(e, null, CompKind.Generator)
            : e;
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
        var left = ParseTernary(policy);

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

    //  `a if c else b` —— 词形三元。位置夹在 `:=` 与 `??` 之间:比赋值紧(于是
    //  `x := a if c else b` 是"先挑值再定义"),比 `||`/`??` 松(于是 `a ?? b if c else d`
    //  是 `(a ?? b) if c else d`)。右结合:`a if c1 else b if c2 else d`
    //  = `a if c1 else (b if c2 else d)`。
    //
    //  两个分支都用 CompHead 解析:分支后面那个 `for` 归外层,否则
    //  `[x if p else y for i in xs]` 会变成"else 分支是个 Generator"。
    //  (推导式的 `if` 子句自己关掉三元,见 Parser.Comprehensions.ParseCompFilter。)
    private Expression ParseTernary(Postfix policy)
    {
        var value = ParseNullCoalesce(policy);
        if (!policy.Ternary || !IsWord("if")) return value;

        BumpExprDepth();
        _pos++;
        var cond = ParseTernary(Postfix.CompHead);
        ConsumeWord("else", "`if` 后面要跟 `else` —— 三元写成 `a if 条件 else b`;"
                          + "只想挑一支跑,就写 `cond { } { }`");
        BumpExprDepth();
        var otherwise = ParseTernary(Postfix.CompHead);
        return new ConditionalExpr(cond, value, otherwise) { Line = value.Line, Column = value.Column };
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

        // while 后面那一格是条件,不是实参
        if (_moreControlFlow && e is IdentifierExpr { Name: "while" })
            return ParsePostfixRest(e, policy.WithBlocks);

        while (Check(TokenType.LeftBrace))
        {
            e = new CallExpr(e, ParsePrimary()) { Line = e.Line, Column = e.Column };
            e = ParsePostfixRest(e, policy);         // 块后面还能接普通的并列实参
        }

        return e;
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
            if (!TryInfixBinding(t, policy, out var lbp, out var rbp)) break;

            if (lbp <= minBp) break;

            _pos++;

            if (TryParsePipeLeftOperand(policy, out var sealedRight))
            {
                left = new BinaryExpr(left, t.Lexeme, sealedRight) { Line = t.Line, Column = t.Column };
                continue;
            }
            BumpExprDepth();                        // 链上又挂了一节,树也跟着长一层
            var op = t.Lexeme;
            var right = ParseClimb(policy, rbp);
            if (IsCompareOp(op) && TryFoldCompare(ref left, op, right)) continue;

            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    // 中缀绑定力:查表,词运算符再验一眼
    private bool TryInfixBinding(Token t, Postfix policy, out int lbp, out int rbp)
    {
        // `{k -> v}` 的键那一格:`->` 是字典的分隔符,不是运算符
        if (t.Type == TokenType.DictArrow && !policy.Arrow)
        {
            lbp = rbp = 0;
            return false;
        }

        if (Operators.TryToken(t.Type, out var info))
        {
            (lbp, rbp) = (info.Lbp, info.Rbp);
            return true;
        }

        if (IsInfixWordOperator(t))
        {
            Operators.TryWord(t.Lexeme, out var infoOfWord);
            // 后面没操作数就是写错了
            if (!StartsPrimaryAt(1) && !IsOperatorToken(TypeAt(1)))
                throw ParseError($"'{t.Lexeme}' 是中缀运算符，后面得跟个操作数"
                               + $"（想传一个叫 `{t.Lexeme}` 的值就加括号：`({t.Lexeme})`）");
            (lbp, rbp) = (infoOfWord.Lbp, infoOfWord.Rbp);
            return true;
        }

        lbp = rbp = 0;
        return false;
    }

    // `+ |> f`:右手整条管道先封起来
    private bool TryParsePipeLeftOperand(Postfix policy, out Expression right)
    {
        right = null!;
        if (!Check(TokenType.PipeLeft)) return false;
        _pos++;
        BumpExprDepth();
        right = ParsePipe(policy);
        if (HasHoles(right)) right = DesugarHoles(right);
        return true;
    }

    // 比较链:x < y < z 摊成一串
    private static bool TryFoldCompare(ref Expression left, string op, Expression right)
    {
        if (left is BinaryExpr { Parenthesized: false } prev && IsCompareOp(prev.Op))
        {
            left = new ChainCompare([prev.Left, prev.Right, right], [prev.Op, op])
                { Line = left.Line, Column = left.Column };
            return true;
        }

        if (left is ChainCompare { Parenthesized: false } chain)
        {
            chain.Operands.Add(right);
            chain.Ops.Add(op);
            return true;
        }

        return false;
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
        // `-1 => …` 是裸模式参数,不是减号
        if (policy.Args && Check(TokenType.Minus) && BarePatternLambdaAhead(1))
            return ParseBarePatternLambda();

        if (TryParseUnaryCall(policy, out var unary)) return unary;

        // 成员先贴,再贴后面的后缀
        var expr = ParseMemberChain(ParsePrimary(), policy);
        return ParsePostfixRest(expr, policy);
    }

    // `!x` / `-x`:右结合,一层一个 UnaryExpr
    private bool TryParseUnaryCall(Postfix policy, out Expression expr)
    {
        expr = null!;
        if (!Match(TokenType.Bang) && !Match(TokenType.Minus)) return false;
        BumpExprDepth();
        var op = Previous();
        expr = new UnaryExpr(op.Lexeme, ParsePower(policy)) { Line = op.Line, Column = op.Column };
        return true;
    }

    private void BumpExprDepth()
    {
        if (++_exprDepth > MaxExprDepth)
            throw ParseError($"表达式嵌套太深（超过 {MaxExprDepth} 层运算符/调用链）");
    }
}
