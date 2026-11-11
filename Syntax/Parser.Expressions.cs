namespace Ravel;

using System.Linq;
using Ravel.Runtime;

public partial class Parser
{
    // ========================================
    //  表达式 — 按优先级递归下降
    // ========================================

    private Expression ParseExpression(bool allowCall = true)
    {
        // 一条表达式从哪儿开始、到哪儿结束,链长就在这一段里算:出去(哪怕是抛异常出去)
        // 都还回原值。没有这一层,`obj.field := v` 这种**语句级**的赋值会把 `_exprDepth`
        // 越攒越高 —— 攒过 400 条,第 401 条好端端的语句就被判成"嵌套太深"。
        using var _expr = ExprDepth.Begin(this);
        return ParsePipe(allowCall);
    }

    private Expression ParsePipe(bool allowCall = true)
    {
        var left = ParseAssignment(allowCall);

        if (Match(TokenType.PipeLeft))
        {
            BumpExprDepth();                        // `<|` 右结合:`a <| b <| c` 是 `a <| (b <| c)`
            var right = ParsePipe(allowCall);
            return new PipeExpr(left, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    private Expression ParseAssignment(bool allowCall = true)
    {
        var left = ParseNullCoalesce(allowCall);

        // `??=` 单独一条:它**不能**走下面"把左值先求出来"那条路 ——
        // 「不空就一个字符都不碰」(成员那条连 setter 都不该调)得靠"读、写分开"才做得到,
        // 那是 `Lowering` 折 `NilFill` 时的事。这儿只管形状:`??=` 的左边得是变量或成员
        // (和 `=` 一样),不是就当场报 —— 插入符指着 `??=`,比指着整条语句准。
        if (Match(TokenType.CoalesceEqual))
        {
            var op = Previous();
            if (left is not (IdentifierExpr or MemberAccess))
                throw ParseError("'??=' 的左边得是变量或成员（就像 '=' 那样）");
            BumpExprDepth();                    // `a ??= b ??= c`:右结合,一层一个栈帧
            var right = ParseAssignment(allowCall);
            return new BinaryExpr(left, op.Lexeme, right) { Line = left.Line, Column = left.Column };
        }

        if (Match(TokenType.Equal) || Match(TokenType.ColonEqual) || Match(TokenType.PlusEqual) ||
            Match(TokenType.MinusEqual) || Match(TokenType.StarEqual) || Match(TokenType.StarStarEqual) ||
            Match(TokenType.SlashEqual) || Match(TokenType.PercentEqual))
        {
            var op = Previous().Lexeme;
            BumpExprDepth();                    // `a := b := c` / `a = b = c`:右结合,一层一个栈帧
            var right = ParseAssignment(allowCall);
            return new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    private Expression ParseNullCoalesce(bool allowCall = true)
    {
        var left = ParseBlockArgs(allowCall);
        if (!Match(TokenType.Coalesce)) return left;

        BumpExprDepth();                            // 右结合:`a ?? b ?? c` 是 `a ?? (b ?? c)`,每层一个栈帧
        var op = Previous();
        var right = ParseNullCoalesce(allowCall);          // 右结合:`a ?? b ?? c` 是 `a ?? (b ?? c)`
        return new BinaryExpr(left, op.Lexeme, right) { Line = left.Line, Column = left.Column };
    }

    private Expression ParseBlockArgs(bool allowCall)
    {
        var e = ParseClimb(allowCall, 0);
        if (!allowCall) return e;

        // `while` 的块**仍归原来那套**(在 `ParsePostfixRest` 里)—— 它的条件和体之间
        // **没有运算符**,所以"块挂到整条表达式上"这条对它不适用;而循环上下文
        // (压 `LoopCtx`、体的 `break` / `continue` 出口)全在那儿。
        //
        // (从前这里还有个 `foreach`;2026-10-07 起它成了**成员调用** `序列.Foreach 体`,
        //  体是普通的 lambda 实参,走不上这条路。)
        if (moreControlFlow && e is IdentifierExpr { Name: "while" })
            return ParsePostfixRest(e, allowCall, allowBlock: true);

        while (Check(TokenType.LeftBrace))
        {
            // 位置照**被调用那一侧**记(和 `ParsePostfixRest` 里那条一个规矩:
            // 报错要指回"这个调用写在哪",而块写在右边)。
            e = new CallExpr(e, ParsePrimary()) { Line = e.Line, Column = e.Column };
            e = ParsePostfixRest(e, allowCall);      // 块后面还能接普通的并列实参
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
            // `(` 开:起点不含。收尾的 `]` / `)` 由 `TryParseRange` 吃掉。
            index = TryParseRange(false)
                ?? throw ParseError("'(' 开头的索引得是区间（`xs(0..3)`）");
        }
        else
        {
            // `[` 开:区间(收尾 `]` 含、`)` 不含)或者一个普通的表达式。
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

    // 紧跟的这个 `(` 里面(这一层)有没有 `..` —— 有才是区间索引,不然是普通调用。
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

    private static readonly Dictionary<TokenType, (int Lbp, int Rbp)> BinOpBp = new()
    {
        [TokenType.OrOr] = (2, 3),
        [TokenType.Pipe] = (4, 5),
        [TokenType.And] = (6, 7), [TokenType.Caret] = (6, 7),
        [TokenType.AndAnd] = (8, 9),
        [TokenType.Colon] = (10, 11),
        [TokenType.NotEqual] = (10, 11), [TokenType.EqualEqual] = (10, 11),
        [TokenType.Less] = (10, 11), [TokenType.Greater] = (10, 11),
        [TokenType.LessEqual] = (10, 11), [TokenType.GreaterEqual] = (10, 11),
        [TokenType.Subtype] = (10, 11), [TokenType.Supertype] = (10, 11),
        [TokenType.ShiftLeft] = (12, 13), [TokenType.ShiftRight] = (12, 13),
        [TokenType.RotateLeft] = (12, 13), [TokenType.RotateRight] = (12, 13),
        [TokenType.Plus] = (14, 15), [TokenType.Minus] = (14, 15),
        [TokenType.Star] = (16, 17), [TokenType.Slash] = (16, 17), [TokenType.Percent] = (16, 17),
    };

    private static readonly Dictionary<string, (int Lbp, int Rbp)> WordOpBp = new()
    {
        ["or"] = (2, 3),
        ["and"] = (8, 9),
    };

    private static bool IsInfixWordOperator(Token t)
        => t.Type == TokenType.Identifier && WordOpBp.ContainsKey(t.Lexeme);

    internal static bool IsInfixOperator(Token t)
        => BinOpBp.ContainsKey(t.Type) || IsInfixWordOperator(t)
        || t.Type is TokenType.Coalesce or TokenType.PipeInto;

    private Expression ParseClimb(bool allowCall, int minBp)
    {
        using var _climb = ExprDepth.Begin(this);
        var left = ParsePower(allowCall);

        while (true)
        {
            var t = Peek();
            int lbp, rbp;

            if (BinOpBp.TryGetValue(t.Type, out var bp))
                (lbp, rbp) = bp;
            else if (IsInfixWordOperator(t))
            {
                // 运算符后面总得跟个操作数。`print or` 是最常见的那一脚 —— 从前它报
                // 「未定义的名字 'or'」(那时 `or` 还是实参),现在得说准话:它成了运算符,
                // 想传这个值就加括号。(不然报的是"需要表达式,但得到 表达式结束",
                // 一个字的线索都没有,还指着语句开头。)
                if (!StartsPrimaryAt(1) && !IsOperatorToken(TypeAt(1)))
                    throw ParseError($"'{t.Lexeme}' 是中缀运算符，后面得跟个操作数"
                                   + $"（想传一个叫 `{t.Lexeme}` 的值就加括号：`({t.Lexeme})`）");
                (lbp, rbp) = WordOpBp[t.Lexeme];
            }
            else break;

            if (lbp <= minBp) break;

            _pos++;
            BumpExprDepth();                        // 链上又挂了一节,树也跟着长一层
            var op = t.Lexeme;
            var right = ParseClimb(allowCall, rbp);
            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    private Expression ParsePower(bool allowCall = true)
    {
        var left = ParseCall(allowCall);

        if (Match(TokenType.StarStar))
        {
            BumpExprDepth();                        // `**` 右结合:右边那个就是下一层
            var op = Previous().Lexeme;
            var right = ParsePower(allowCall);      // 往本层递归 = 右结合
            return new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    private Expression ParseCall(bool allowCall = true)
    {
        // 一元 ! 和 -(都往本层递归,所以 `! !x` / `- -1` 写得出来)。
        // 位置取**运算符**那个 token:从前是把操作数解析完才回头 Previous(),
        // 拿到的是操作数的最后一个 token,报错时插入符指在算式末尾。
        //
        // **操作数走 `ParsePower` 而不是本层**:`-2 ** 2` 要读成 `-(2 ** 2)`
        // (`**` 比一元紧)。走本层的话负号先落地,就成了 `(-2) ** 2`。
        // `-1 => …` —— 负数字面量当**裸模式参数**。负号在**这一层**就被吃了,所以判据得
        // 先在这儿看一眼:不拦的话读成 `-(1 => …)`,运行期报一句「一元 '-' 不支持 Function」。
        // 只认 `allowCall` 那一侧:模式内部(守卫、字面量那一格)走的是 `allowCall: false`,
        // 那儿出现 `-1 => …` 不是 lambda。
        if (allowCall && Check(TokenType.Minus) && BarePatternLambdaAhead(1))
            return ParseBarePatternLambda();

        if (Match(TokenType.Bang) || Match(TokenType.Minus))
        {
            BumpExprDepth();                        // `! ! !x` / `- - -1`:一元也是一层递归
            var op = Previous();
            return new UnaryExpr(op.Lexeme, ParsePower(allowCall)) { Line = op.Line, Column = op.Column };
        }

        // .成员访问 — 在空格调用之前处理
        var expr = ParseMemberChain(ParsePrimary(), allowCall);
        return ParsePostfixRest(expr, allowCall);
    }

    private void BumpExprDepth()
    {
        if (++_exprDepth > MaxExprDepth)
            throw ParseError($"表达式嵌套太深（超过 {MaxExprDepth} 层运算符/调用链）");
    }

    private Expression ParsePostfixRest(Expression expr, bool allowCall, bool guarded = false,
                                        bool stopAtPipe = false, bool allowPipe = false,
                                        bool allowBlock = false)
    {
        // `allowCall: false` 从前是"**什么后缀都不收**"。守卫那儿需要的其实是更窄的一档
        // (`allowPipe: true`):**不吃并列的实参**(不然 `(x < 0 y: int)` 里那个 `y: int`
        // 会被当成 `0` 的实参吃进去,"守卫 + 下一个参数"静默变成一个参数的 lambda),
        // 但 `|>` **照收** —— 它是运算符,不是并列的应用(`(v |> IsPrime)` 靠它)。
        // 下标是**后缀**,和"并列的实参"不是一回事 —— 所以放在 `allowCall` 这道门**外面**:
        // `print l[0]` 里那个 `[0]` 得挂在 `l` 上,不能被外层当成"再喂一个实参"。
        //
        // 四种区间写法都在这一条路上:`xs[1..2]` / `xs[1..3)` / `xs(0..2]` / `xs(0..3)`。
        // `(` 只有"紧挨着、里面是区间"才当索引 —— 否则 `f(x)` 还是普通的调用。
        while (AdjacentToPrevious()
               && (Check(TokenType.LeftBracket) || (Check(TokenType.LeftParen) && RangeInsideParens())))
            expr = ParseIndex(expr);

        if (!allowCall && !allowPipe) return expr;

        // 这一串里正在开的那个循环。**得跨实参活下来** —— `while` / `foreach` 的体是
        // **第 2 个**实参,而每一轮循环体都会重新声明局部变量。
        LoopCtx? loop = null;

        // f a b c  →  ((f a) b) c  柯里化。
        // **词形运算符在这儿要停**(`or` / `and`):它们看着像个 primary —— 本来就是标识符
        // —— 但 `f a or g` 里那个 `or` 是中缀:`a` 当实参,`or` 归上面 `ParseClimb` 的循环。
        // (不停的话 `f or g` 就成了"把 `or` 和 `g` 当实参传给 f" —— 而这门语言里
        //  实参本来就吃运算符,不拦一手,运算符永远轮不到。)
        while (true)
        {
            // `xs[i]` —— **紧挨着**才算索引(`xs [1 2]` 仍是"把列表当实参")。
            // 空格在这门语言里本来就是"应用"的意思,拿它分开这两件事最省事,
            // 也不给列表字面量挖坑(`print [1 2]` / `f [1 2]` 一个字都不用改)。
            // `|>` —— **把左边交给右边**。右边是什么,决定了交法:
            //
            //     x.f () |> .g ()   ≡   (x.f ()).g ()    ← `.成员`:挂到左边那个值上
            //     x      |> f       ≡   f x              ← 别的:左边当**实参**喂给右边
            //     x      |> f 1     ≡   f 1 x            ← 右边自己的实参先喂,左边排**最后**
            //
            // `.成员` 那一支是它最早的样子,也是它非有不可的理由:`.成员` 比并列的调用绑得紧,
            // `x.f ().g ()` 会被读成 `x.f ((().g ()))` —— 零参调用后面接链的写法就全废了。
            //
            // `stopAtPipe` 是给"右边那半"用的:右边**不吃下一个 `|>`**。吃了的话
            // `a |> f |> g` 会成 `a |> (f |> g)`,而我们一路往右喂要的是 `g (f a)`。
            if (Match(TokenType.PipeInto))
            {
                if (stopAtPipe) { _pos--; break; }      // 把这一枚还回去,外层接着收
                BumpExprDepth();                       // `a |> f |> g`:一层套一层

                if (Check(TokenType.Dot) || Check(TokenType.QuestionDot))
                {
                    expr = ParseMemberChain(expr, allowCall, guarded);
                    continue;
                }

                // 后面总得跟点什么。`1 |>` 那种尾巴上多出来的 `|>` 不该被静默吃掉。
                // **运算符节也算**(`5 |> +.2` ≡ `(+.2) 5`)—— 那个开头是个 `+`,
                // `StartsPrimary` 不认它,所以得单独放行。
                if (!StartsPrimary() && !IsOperatorToken(Peek().Type))
                    throw ParseError("'|>' 后面得跟点什么（`a |> .f ()` 是挂成员，`a |> g` 是把 a 喂给 g）");

                // 右边整串先收完(`f` / `f 1` / `xs.At 0` / `+.2` / 一个 lambda),
                // 再把左边当**最后一个**实参喂进去 —— 柯里化的写法得是 `f 1 x`,不是 `f x 1`。
                //
                // 右边的**一格**就是这个形状;`|` 那条链也拿它当一格(见下)。
                Expression Clause()
                {
                    var one = ParseMemberChain(ParsePrimary(), allowCall);
                    return ParsePostfixRest(one, allowCall, guarded: false, stopAtPipe: true);
                }

                var left = expr;
                var fn = Clause();

                // **`|` 比 `|>` 松**:`v |> A | B` 是 `v |> (A | B)`,不是 `(v |> A) | B`。
                // 理由:`|` 那串本来就是**一整条函数**(分派表的写法),而 `|>` 是"把值喂进去" ——
                // 值该喂给**整条**,不是喂给第一格。所以右边收完接着把 `|` 链收进来。
                // (左结合,和 `|` 自己一个规矩;`stopAtPipe` 那层保护不受影响 —— 那只管 `|>`。)
                while (Match(TokenType.Pipe))
                {
                    BumpExprDepth();
                    var bar = Previous();
                    fn = new BinaryExpr(fn, "|", Clause()) { Line = bar.Line, Column = bar.Column };
                }

                expr = new CallExpr(fn, left) { Line = left.Line, Column = left.Column };
                continue;
            }

            // **块字面量不在这儿收** —— 它比运算符松,归上面那层 `ParseBlockArgs`
            // (`x > 0 { A } { B }` 要挂到整条 `x > 0` 上,不是挂到 `0` 上)。
            // 不退出去的话,循环回到这儿会把它当"紧挨着那个操作数"的并列实参吃掉。
            if (!allowCall || !StartsPrimary() || IsInfixWordOperator(Peek())
                || (!allowBlock && Check(TokenType.LeftBrace))) break;

            // **实参吃到运算符为止**:`print 1 + 2` ≡ `print (1 + 2)`。
            // 并列的应用比运算符**松** —— 调用"抓住"它右边的一整条算式,而不是先算完调用再拿结果去算。
            // (`allowCall: false` 那一档正好是"运算符链、但不吃并列的实参":所以 `f a b` 还是两个参数,
            //  柯里化不受影响;`f 1 + 2 * 3` 是 `f (1 + 2*3)`。)
            //
            // **唯一的例外是 `<|`** —— 它由 `ParsePipe` 那一层管,而实参只走到 `ParseAssignment`。
            // 所以实参**吃到运算符为止,但吃不掉 `<|`**:
            //     add 1 <| 7     ≡  (add 1) 7      —— 拿**整串调用**的结果当函数
            //     f <| a + b     ≡  f (a + b)      —— 右边整条算式当一个实参
            // 这条不可少:`<|` 的用处之一就是"喂给一个柯里化了一半的调用",
            // 而实参若把它吞进去,`add 1 <| 7` 就成了 `add (1 <| 7)` —— 报的还是
            // 「'<|' 左边必须是函数」,指着一个根本不该当函数用的 `1`,查半天都找不到北。
            // (括号开头的实参照旧:`f (1) + 2` 是 `f ((1) + 2)`。运算符作用在调用**结果**上时
            //  是**调用**那一层的事,自己加括号:`xs.Count () == 0` 要写 `(xs.Count ()) == 0`。)
            // 循环调用。两种形状(**体在第几个实参不一样**):
            //
            //     while <条件> <体>        —— 裸名字,体是第 **2** 个实参
            //     <序列>.Foreach <体>      —— **成员调用**(`MemberAccess`),体是第 **1** 个
            //
            // 一进来就把上下文压上 —— 体里写的 `break` / `continue` 才知道往哪跳。
            //
            // `.Foreach` 写成成员调用是 2026-10-07 的事:从前的 `foreach 序列 体` 是**库函数**,
            // 每个元素要走一遍 `GetEnumerator` → `MoveNext` → `Current`;挂成方法之后容器
            // 那条路归引擎(`SeqMethod`,每元素快 5 倍)。解析器**按名字认循环**这条没变,
            // 只是名字从裸的 `foreach` 挪到成员 `.Foreach` 上。
            var bodyAt = expr switch
            {
                IdentifierExpr { Name: "while" } => 2,
                MemberAccess { Member: "Foreach" } => 1,
                _ => 0,
            };
            if (moreControlFlow && loop is null && bodyAt != 0)
            {
                loop = new LoopCtx { Label = _pendingLabel, Brk = _pendingBrk ?? "", BodyAt = bodyAt };
                // 只清"待认领"那个标记(免得**下一个**循环又把它认走);名字留着 ——
                // 里面的 `break 标签` 要拿它跟自己的出口比,才知道标签被用过了
                if (_pendingLabel is not null) _pendingLabel = null;
                _loops.Add(loop);
            }

            // 这一格实参是不是**空参数表**那个 `()` 写下来的(`f () > 1`)—— 判据在 token 上:
            // `(` 紧跟着 `)`。`f (() > 1)` 那种括号组开头是 `(` `(`,不算。
            // 两种写法解析出来的子树一样,所以这个"来路"得当场记下(见 `CallExpr.BareUnitArg`)。
            var bareUnit = Check(TokenType.LeftParen) && TypeAt(1) == TokenType.RightParen;

            var arg = ParseAssignment(allowCall: false);

            var at = Previous();

            // 体到手了:先生 `continue` 的出口,再建这次调用,最后按需包 `break`
            if (loop is not null && ++loop.BodySeen == loop.BodyAt)
            {
                if (loop.ContUsed) arg = WrapLoopBody(arg, loop.Cont, at);
                expr = new CallExpr(expr, arg)
                {
                    Line = expr.Line,
                    Column = expr.Column,
                };
                // 标签认领过的循环**不包 break** —— 出口留给标签那一层(那样 `break 标签`
                // 跳的是标签,不是循环,标签也能贴在不带循环的语句上)
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

    private Expression ParseMemberChain(Expression e, bool allowCall = true, bool guarded = false)
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

                // 已经在一层守卫里了:当普通 `.` 使(守卫只包一层)。
                // 各包各的会变成 `Some (Some (…))` —— `a?.b?.c` 要的是"一条链一个守卫"。
                if (guarded)
                {
                    BumpExprDepth();
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
