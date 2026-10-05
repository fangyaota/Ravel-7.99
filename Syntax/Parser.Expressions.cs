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
        // 「不空就一个字符都不碰」(成员那条连 setter 都不该调)得靠"读、写分开"才做得到,
        // 那是 `Lowering` 折 `NilFill` 时的事。这儿只管形状:`??=` 的左边得是变量或成员
        // (和 `=` 一样),不是就当场报 —— 插入符指着 `??=`,比指着整条语句准。
        if (Match(TokenType.CoalesceEqual))
        {
            var op = Previous();
            if (left is not (IdentifierExpr or MemberAccess))
                throw ParseError("'??=' 的左边得是变量或成员（就像 '=' 那样）");
            var right = ParseAssignment(allowCall);
            return new BinaryExpr(left, op.Lexeme, right) { Line = left.Line, Column = left.Column };
        }

        if (Match(TokenType.Equal) || Match(TokenType.ColonEqual) || Match(TokenType.PlusEqual) ||
            Match(TokenType.MinusEqual) || Match(TokenType.StarEqual) || Match(TokenType.StarStarEqual) ||
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
    /// 解析器只造 `BinaryExpr(a, "??", b)`;**折成 `NilOr` 两个 thunk** 是
    /// <see cref="Lowering"/> 那趟的事(右边惰性的那些语义在 `lib/predefined.rav`)。</summary>
    private Expression ParseNullCoalesce(bool allowCall = true)
    {
        var left = ParseClimb(allowCall, 0);
        if (!Match(TokenType.Coalesce)) return left;

        var op = Previous();
        var right = ParseNullCoalesce(allowCall);          // 右结合:`a ?? b ?? c` 是 `a ?? (b ?? c)`
        return new BinaryExpr(left, op.Lexeme, right) { Line = left.Line, Column = left.Column };
    }

    /// <summary>把一条表达式包成"要用才跑"的块 `() => { expr; }`。参数名是 `_`(void),
    /// 和 `() => …` 的 AST 写法一致(见 `ParseParen`)。
    ///
    /// 只剩 `?.` 在用了 —— `??` / `??=` 那两处已经搬到 `Lowering` 去折。</summary>
    private LambdaExpr Thunk(Expression e, Token at)
        => new(new Parameter("_", new IdentifierExpr("void") { Line = at.Line, Column = at.Column }), AsBlock(e, at))
            { Line = at.Line, Column = at.Column, Sugar = true };

    private static IdentifierExpr Ident(string name, Token at) => new(name) { Line = at.Line, Column = at.Column };

    private static CallExpr Call(Expression fn, Expression arg, Token at)
        => new(fn, arg) { Line = at.Line, Column = at.Column };

    /// <summary>纯二元那八层共用的优先级表:`(左结合度, 右结合度)`。
    ///
    /// 左结合写 `(2k, 2k+1)` —— 右结合度比左结合度大一,于是"同级的右边"该不该继续
    /// 吃下一个同级的运算符,由这一对数说了算:右边那一趟的 `minBp` 正好把同级挡在外面。
    ///
    /// **只有纯二元的在表里。** 四个例外各有各的道理,留在自己那层:
    ///
    ///   * `**` **比一元还紧**(见 `ParsePower`)—— 优先级表表达不了"比前缀运算符紧";
    ///   * `??` 是**惰性**的,要脱糖成 `NilOr` 的两个 thunk,不是"求两个值";
    ///   * `=` 那一族是赋值,右值那边要的是"又一个赋值",不是运算符链;
    ///   * `<|` **比这一整条链都松**(见 `ParsePipe`)—— 它连赋值都包得住,
    ///     表里放不下"比最松的还松"。(`|>` 不是运算符,它住在调用那一段。)
    ///
    /// **词形运算符**(`or` / `and`)是**标识符**,进不了按 token 类型查的表 —— 单独一张
    /// (见 <see cref="WordOpBp"/>)。</summary>
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

    /// <summary>**词形运算符**:看着是标识符,但在中缀位置是运算符 —— 现在只有 `or` / `and`
    /// (谓词级的与 / 或)。
    ///
    /// 它们比不了 `<:` 那种"生来就是标点"的:进不了按 TokenType 查的 <see cref="BinOpBp"/>,
    /// 得按**词**认,而且只有两个词、只在**中缀位置**认。代价说清楚:
    /// **在实参那个位置它们不再是名字** —— `f or g` 合成谓词,想传一个叫 `or` 的值得写 `f (or)`
    /// (括号里它照旧是普通标识符)。定义位置(`or := 42`)、参数位置、成员位置(`o.or`)、
    /// 字典键一概不受影响。语料里没有把 `or` / `and` 当裸实参传的地方。
    ///
    /// 优先级**照着 `||` / `&&`**:`or` 那一档、`and` 高两档 —— 于是 `a or b and c` 是
    /// `a or (b and c)`,和布尔那边读法一致。
    ///
    /// 折成谓词那一步**不在这儿** —— 解析器只造 `BinaryExpr(l, "or", r)`,换形状是
    /// <see cref="Lowering"/> 那趟的事。</summary>
    private static readonly Dictionary<string, (int Lbp, int Rbp)> WordOpBp = new()
    {
        ["or"] = (2, 3),
        ["and"] = (8, 9),
    };

    /// <summary>这一枚是不是**中缀位置**的词形运算符(`or` / `and`)。</summary>
    private static bool IsInfixWordOperator(Token t)
        => t.Type == TokenType.Identifier && WordOpBp.ContainsKey(t.Lexeme);

    /// <summary>这一枚在**中缀位置**是个二元运算符吗 —— 标点的(见 <see cref="BinOpBp"/>)、
    /// 词形的(`or` / `and`),再加两个走别的路的(`??` 在 `ParseNullCoalesce`、
    /// `|>` 在 `ParsePostfixRest`)。
    ///
    /// **词法器要用它**:一行要是**以二元运算符打头**,那个换行就不算数,于是"接着上一行写"
    /// 成立(见 `Lexer.Tokenize` 末尾那一趟)。表在哪儿、判据就在哪儿 —— 别在两处各列一份。</summary>
    internal static bool IsInfixOperator(Token t)
        => BinOpBp.ContainsKey(t.Type) || IsInfixWordOperator(t)
        || t.Type is TokenType.Coalesce or TokenType.PipeInto;

    /// <summary>优先级爬升 —— 替掉原来"一个运算符一个函数"的那八层。
    ///
    /// 那八层(`ParseLogic` / `ParsePipeOp` / `ParseAndBit` / `ParseAnd` / `ParseComparison` /
    /// `ParseShift` / `ParseTerm` / `ParseFactor`)除了"认哪个运算符"之外**一模一样** ——
    /// 加一个运算符得新开一个函数,还得在链上找准位置。现在加一个运算符就是**表里添一行**。
    ///
    /// 顺带把调用栈也削平了:从前一层括号要穿过八层才下到 `ParsePower`,现在只穿过一层 ——
    /// 而这几层本来就是整个解析器里递归最深的一段。
    ///
    /// `allowCall` 照旧往下传 —— 实参那一趟(`allowCall: false`)爬的是**同一条链**,只是
    /// 不吃并列的调用。这正是生成器做不到的那一件(规则不能参数化,只能把整条链抄两遍)。</summary>
    private Expression ParseClimb(bool allowCall, int minBp)
    {
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
            var op = t.Lexeme;
            var right = ParseClimb(allowCall, rbp);
            left = new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>乘方 `**` —— **右结合**,而且**比一元还紧**:
    ///
    ///     -2 ** 2     ≡  -(2 ** 2)       = -4     ← 要 (-2)² 得自己括
    ///     2 ** 3 ** 2 ≡  2 ** (3 ** 2)   = 512
    ///     2 ** -1     ≡  2 ** (-1)       = 0.5    ← 指数里的负号照样是一元的
    ///
    /// 它是这门口子里**唯一**比一元紧的二元运算符(`-2 * 3` 仍然是 `(-2) * 3`)。
    /// 照着数学写法定的:写 `-2 ** 2` 的人要的是 -4 的多。
    ///
    /// 实现上不是靠优先级表——是让 `ParseCall` 里那个一元分支的**操作数走这一层**
    /// (而不是它自己),于是"先把整个乘方收完、再套负号"。</summary>
    private Expression ParsePower(bool allowCall = true)
    {
        var left = ParseCall(allowCall);

        if (Match(TokenType.StarStar))
        {
            var op = Previous().Lexeme;
            var right = ParsePower(allowCall);      // 往本层递归 = 右结合
            return new BinaryExpr(left, op, right) { Line = left.Line, Column = left.Column };
        }

        return left;
    }

    /// <summary>一元 ! -  .成员访问  和函数调用</summary>
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
            var op = Previous();
            return new UnaryExpr(op.Lexeme, ParsePower(allowCall)) { Line = op.Line, Column = op.Column };
        }

        // .成员访问 — 在空格调用之前处理
        var expr = ParseMemberChain(ParsePrimary(), allowCall);
        return ParsePostfixRest(expr, allowCall);
    }

    /// <summary>主表达式后面那一串**并列的调用/实参**(`f a b` / `|>`)。
    ///
    /// 单拎出来是因为 `?.`:`a?.b c` 里**整条链**(成员访问 + 后面的调用)都归那枚守卫
    /// 闭包管 —— 它在自己的 lambda 体内得再走一趟这段(见 <see cref="GuardedChain"/>)。
    /// `guarded: true` = 已经在一层守卫里了,再撞上 `?.` 就当普通 `.` 使(守卫只包一层)。</summary>
    private Expression ParsePostfixRest(Expression expr, bool allowCall, bool guarded = false,
                                        bool stopAtPipe = false)
    {
        if (!allowCall) return expr;

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
                    var bar = Previous();
                    fn = new BinaryExpr(fn, "|", Clause()) { Line = bar.Line, Column = bar.Column };
                }

                expr = new CallExpr(fn, left) { Line = left.Line, Column = left.Column };
                continue;
            }

            if (!StartsPrimary() || IsInfixWordOperator(Peek())) break;

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
            // 循环调用(`while <条件> <体>` / `foreach <序列> <体>`,**体是第 2 个实参**)。
            // 一进来就把上下文压上 —— 体里写的 `break` / `continue` 才知道往哪跳。
            if (moreControlFlow && loop is null
                && expr is IdentifierExpr { Name: "while" or "foreach" })
            {
                loop = new LoopCtx { Label = _pendingLabel, Brk = _pendingBrk ?? "" };
                // 只清"待认领"那个标记(免得**下一个**循环又把它认走);名字留着 ——
                // 里面的 `break 标签` 要拿它跟自己的出口比,才知道标签被用过了
                if (_pendingLabel is not null) _pendingLabel = null;
                _loops.Add(loop);
            }

            var arg = ParseAssignment(allowCall: false);

            var at = Previous();

            // 体到手了:先生 `continue` 的出口,再建这次调用,最后按需包 `break`
            if (loop is not null && ++loop.BodySeen == 2)
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
