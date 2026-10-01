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

    /// <summary>赋值 = += -= *= /= %= ??= 与成员定义 :=  （右结合，仅表达式级别）。
    /// 顶层语句的 `x := v` 在 ParseStatement 就分派给 ParseDefinition 了，
    /// 这里收的是左操作数为成员访问的 `obj.field := v`（定义/覆盖字段）。</summary>
    private Expression ParseAssignment(bool allowCall = true)
    {
        var left = ParseNullCoalesce(allowCall);

        // `??=` 单独一条:它**不能**走下面"把左值先求出来"那条路 ——
        // 「不空就一个字符都不碰」(成员那条连 setter 都不该调)得靠"读、写分开"才做得到。
        if (Match(TokenType.CoalesceEqual))
        {
            var op = Previous();
            var right = ParseAssignment(allowCall);
            return CoalesceAssign(left, right, op);
        }

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

    /// <summary>空值合并 `a ?? b` —— 右结合,比 `||` 低、比赋值高。
    ///
    /// **惰性**:右边只在左边是空的时候才求值。做法是把两边各包成一块交给库里的
    /// `NilOr`(见 `lib/predefined.rav`),右边那块该不该跑由它说了算 ——
    /// 和 `if` / `do` 一个路子:引擎只管脱糖,语义在库里。
    ///
    /// 折成的形状:`NilOr (() => { a; }) (() => { b; })`。</summary>
    private Expression ParseNullCoalesce(bool allowCall = true)
    {
        var left = ParseLogic(allowCall);
        if (!Match(TokenType.Coalesce)) return left;

        var op = Previous();
        var right = ParseNullCoalesce(allowCall);          // 右结合:`a ?? b ?? c` 是 `a ?? (b ?? c)`
        return Call(Call(Ident("NilOr", op), Thunk(left, op), op), Thunk(right, op), op);
    }

    /// <summary>`x ??= v` / `a.b ??= v` —— **空才写**(不空一个字符都不碰:连 setter 都不调)。
    ///
    /// 折成 `NilFill (() => { x; }) ((w) => { x = w; }) (() => { v; })`:读一点、写一点、
    /// 后备一块,三样分开交给库(见 `lib/predefined.rav`),「空不空」由它判。
    ///
    /// 成员那条把**接收者先求一次**再包进闭包(`((r) => { … }) (a)`)——
    /// 否则 `f ().b ??= v` 会把 `f` 跑两遍。</summary>
    private Expression CoalesceAssign(Expression left, Expression right, Token op)
    {
        var fallback = Thunk(right, op);

        Expression FillWith(Expression target)
            => Call(Call(Call(Ident("NilFill", op), Thunk(target, op), op), Setter(target, op), op),
                    fallback, op);

        switch (left)
        {
            case IdentifierExpr id:
                return FillWith(id);

            case MemberAccess ma:
            {
                var recv = "_nil" + _nilCount++;
                var rid = new IdentifierExpr(recv) { Line = op.Line, Column = op.Column };
                var target = new MemberAccess(rid, ma.Member) { Line = op.Line, Column = op.Column };
                var lam = new LambdaExpr(new Parameter(recv, ObjectType(op)), AsBlock(FillWith(target), op))
                    { Line = op.Line, Column = op.Column, Sugar = true };
                return Call(lam, ma.Object, op);
            }

            default:
                throw ParseError("'??=' 的左边得是变量或成员（就像 '=' 那样）");
        }

        // 写回去那一条:`((w) => { target = w; })`。赋值表达式本身交出写进去的那个值,
        // 所以库里 `NilFill` 交回来的也就是它。
        LambdaExpr Setter(Expression target, Token at)
        {
            var w = "_nil" + _nilCount++;
            var wid = new IdentifierExpr(w) { Line = at.Line, Column = at.Column };
            var assign = new BinaryExpr(target, "=", wid) { Line = at.Line, Column = at.Column };
            return new LambdaExpr(new Parameter(w, ObjectType(at)), AsBlock(assign, at))
                { Line = at.Line, Column = at.Column, Sugar = true };
        }
    }

    /// <summary>把一条表达式包成"要用才跑"的块 `() => { expr; }`。参数名是 `_`(void),
    /// 和 `() => …` 的 AST 写法一致(见 `ParseParen`)。</summary>
    private LambdaExpr Thunk(Expression e, Token at)
        => new(new Parameter("_", new IdentifierExpr("void") { Line = at.Line, Column = at.Column }), AsBlock(e, at))
            { Line = at.Line, Column = at.Column, Sugar = true };

    private static IdentifierExpr Ident(string name, Token at) => new(name) { Line = at.Line, Column = at.Column };

    private static CallExpr Call(Expression fn, Expression arg, Token at)
        => new(fn, arg) { Line = at.Line, Column = at.Column };

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
        var expr = ParseMemberChain(ParsePrimary(), allowCall);
        return ParsePostfixRest(expr, allowCall);
    }

    /// <summary>主表达式后面那一串**并列的调用/实参**(`f a b` / `@` / `$`)。
    ///
    /// 单拎出来是因为 `?.`:`a?.b c` 里**整条链**(成员访问 + 后面的调用)都归那枚守卫
    /// 闭包管 —— 它在自己的 lambda 体内得再走一趟这段(见 <see cref="GuardedChain"/>)。
    /// `guarded: true` = 已经在一层守卫里了,再撞上 `?.` 就当普通 `.` 使(守卫只包一层)。</summary>
    private Expression ParsePostfixRest(Expression expr, bool allowCall, bool guarded = false)
    {
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
                if (!Check(TokenType.Dot) && !Check(TokenType.QuestionDot) && !StartsPrimary())
                    throw ParseError("'@' 后面得跟点什么（它的意思是「把左边封口，接着往下写」）");
                expr = ParseMemberChain(expr, allowCall, guarded);
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
            // **没有例外** —— 括号开头的实参也一样:`f (1) + 2` 是 `f ((1) + 2)`。
            // 运算符作用在调用**结果**上时,是**调用**那一层的事,自己加括号:
            //   `xs.Count () == 0` 要写成 `(xs.Count ()) == 0`(库和用例里的写法都按这条改过)。
            var arg = ParseExpression(allowCall: false);
            expr = new CallExpr(expr, arg)
            {
                Line = expr.Line,
                Column = expr.Column,
            };
        }

        return expr;
    }

    /// <summary>主表达式后面挂的一串 `.成员`(`a.b.c`)。实参和函数各要用一次。
    ///
    /// `?.` 也在这一串里 —— 它和 `.` 一个位置,只是"空就短路"。</summary>
    private Expression ParseMemberChain(Expression e, bool allowCall = true, bool guarded = false)
    {
        while (true)
        {
            if (Match(TokenType.Dot))
            {
                var member = ParseMemberName();
                e = new MemberAccess(e, member) { Line = e.Line, Column = e.Column };
                continue;
            }

            if (Check(TokenType.QuestionDot))
            {
                var q = Peek();
                _pos++;

                // 已经在一层守卫里了:当普通 `.` 使(守卫只包一层)。
                // 各包各的会变成 `Some (Some (…))` —— `a?.b?.c` 要的是"一条链一个守卫"。
                if (guarded)
                {
                    var m = ParseMemberName();
                    e = new MemberAccess(e, m) { Line = q.Line, Column = q.Column };
                    continue;
                }

                // 守卫把后面整条链(含调用)都接管进那枚 lambda 了,这一层到此为止。
                return GuardedChain(e, allowCall, q);
            }

            break;
        }

        return e;
    }

    /// <summary>`a?.b c` —— 「空就短路;有值就拿着里面的值往下走」。
    ///
    /// **`?.` 后面整条链都归它管**(成员访问 + 并列调用):`u?.Name ()` 是"有 u 才调 Name",
    /// 不是在 `None` 上调它。折成 `NilChain (() => { a; }) (v) => { v.b c; }` ——
    /// 哪样算"空"、Option 要不要包回去,由库里的 `NilChain` 说了算(见 `lib/predefined.rav`)。
    ///
    /// 参数名 `_nil{n}` 和占位符消糖的 `_n` 分开:两种糖撞在同一条语句里也不会互相遮蔽。</summary>
    private Expression GuardedChain(Expression receiver, bool allowCall, Token q)
    {
        var name = "_nil" + _nilCount++;
        var id = new IdentifierExpr(name) { Line = q.Line, Column = q.Column };
        Expression rest = new MemberAccess(id, ParseMemberName()) { Line = q.Line, Column = q.Column };
        rest = ParseMemberChain(rest, allowCall, guarded: true);
        rest = ParsePostfixRest(rest, allowCall, guarded: true);

        var step = new LambdaExpr(new Parameter(name, ObjectType(q)), AsBlock(rest, q))
            { Line = q.Line, Column = q.Column, Sugar = true };
        return Call(Call(Ident("NilChain", q), Thunk(receiver, q), q), step, q);
    }

}
