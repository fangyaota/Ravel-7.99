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

    /// <summary>比较 != == &lt; &gt; &lt;= &gt;=,类型判定 `is` / `isnot`
    /// (`1 is int` —— 词形运算符,优先级和比较一样),以及类型之间的 `&lt;:` / `:>`
    /// (`int &lt;: object` —— 两边都得是**类型**)</summary>
    private Expression ParseComparison(bool allowCall = true)
    {
        var left = ParseTerm(allowCall);

        while (Match(TokenType.NotEqual) || Match(TokenType.EqualEqual) ||
               Match(TokenType.Less) || Match(TokenType.Greater) ||
               Match(TokenType.LessEqual) || Match(TokenType.GreaterEqual) ||
               Match(TokenType.Subtype) || Match(TokenType.Supertype) ||
               MatchWordOperator())
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
        // 一元 ! 和 -(都往本层递归,所以 `! !x` / `- -1` 写得出来)。
        // 位置取**运算符**那个 token:从前是把操作数解析完才回头 Previous(),
        // 拿到的是操作数的最后一个 token,报错时插入符指在算式末尾。
        if (Match(TokenType.Bang) || Match(TokenType.Minus))
        {
            var op = Previous();
            return new UnaryExpr(op.Lexeme, ParseCall(allowCall)) { Line = op.Line, Column = op.Column };
        }

        // .成员访问 — 在空格调用之前处理
        var expr = ParseMemberChain(ParsePrimary());
        if (!allowCall) return expr;

        // f a b c  →  ((f a) b) c  柯里化。
        // 词形运算符在这儿要停:它看着像个 primary,但 `1 is int` 里那个 `is` 是中缀
        // (节形式的 `f is.int` 除外,那是参数)。
        while (true)
        {
            // `@` —— **括号,把左边封口**:后面的成员链挂到左边那一串的**结果**上。
            //     x.f () @ .g ()   ≡   (x.f ()).g ()
            // 没有它就只能自己写括号:`.成员` 比并列的调用绑得紧,`x.f ().g ()` 会被读成
            // `x.f ((().g ()))`。后面跟的不是 `.成员` 时,它就是个"到这儿为止"的记号
            // (`a @ b c` ≡ `(a) b c`,和 `a b c` 本来就一样)。
            if (Match(TokenType.At))
            {
                // 封口之后总得接点什么:`.成员` 或者下一段实参。`1 @` 那种尾巴上多出来的 `@`
                // 不该被静默吃掉(`$` 那边是交给 ParseExpression 报「需要表达式」的)。
                if (!Check(TokenType.Dot) && !StartsPrimary())
                    throw ParseError("'@' 后面得跟点什么（它的意思是「把左边封口，接着往下写」）");
                expr = ParseMemberChain(expr);
                continue;
            }

            // `$` —— **括号,把右边封口**:从这儿到表达式结束全算**一个**实参。
            //     f $ a b   ≡   f (a b)        f $ g $ x   ≡   f (g (x))   (右结合)
            if (Match(TokenType.Dollar))
            {
                expr = new CallExpr(expr, ParseExpression())
                {
                    Line = expr.Line,
                    Column = expr.Column,
                };
                continue;
            }

            if (!StartsPrimary() || IsInfixWordOperator()) break;

            // **实参吃到运算符为止**:`print 1 + 2` ≡ `print (1 + 2)`。
            // 并列的应用比运算符**松** —— 调用"抓住"它右边的一整条算式,而不是先算完调用再拿结果去算。
            // (`allowCall: false` 那一档正好是"运算符链、但不吃并列的实参":所以 `f a b` 还是两个参数,
            //  柯里化不受影响;`f 1 + 2 * 3` 是 `f (1 + 2*3)`。)
            //
            // **括号开头的实参除外** —— 括号组自己就是个完整的表达式,运算符留给调用**之后**:
            //   `xs.Count () + 1` 是「数完再加一」,不是「数 (() + 1)」;`xs.At (i) + 1` 同理。
            //   (库和用例里 `x.Count () == 0` / `typeof (x) == y` 这类写法全靠这条。)
            var arg = Check(TokenType.LeftParen)
                ? ParseMemberChain(ParsePrimary())
                : ParseExpression(allowCall: false);
            expr = new CallExpr(expr, arg)
            {
                Line = expr.Line,
                Column = expr.Column,
            };
        }

        return expr;
    }

    /// <summary>主表达式后面挂的一串 `.成员`(`a.b.c`)。实参和函数各要用一次。</summary>
    private Expression ParseMemberChain(Expression e)
    {
        while (Match(TokenType.Dot))
        {
            var member = ParseMemberName();
            e = new MemberAccess(e, member) { Line = e.Line, Column = e.Column };
        }

        return e;
    }

}
