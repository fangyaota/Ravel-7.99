namespace Ravel;

using System.Linq;
using Ravel.Runtime;

/// <summary>递归下降解析器的分片,按职责拆文件(和 Runtime/Interpreter.*.cs 一个路子):
/// `Parser.cs` 入口 + token 辅助,`Parser.Statements.cs` 语句,
/// `Parser.Expressions.cs` 优先级链,`Parser.Atoms.cs` 基本单元与括号/块。</summary>
public partial class Parser
{
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

}
