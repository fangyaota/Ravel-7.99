namespace Ravel;

using System.Linq;
using Ravel.Runtime;

public partial class Parser
{
    // ========================================
    //  语句
    // ========================================

    // 注意 init 不在此列:构造器是名字叫 init 的变量(类体里写 `init := ...`),不是修饰符
    private static bool IsMod(string kw) => Attr.All.Contains(kw);

    private bool IsSectionStart()
        => IsOperatorToken(Peek().Type)
           && _pos + 1 < tokens.Count && tokens[_pos + 1].Type == TokenType.Dot;

    private static bool IsOperatorToken(TokenType t) => t switch
    {
        // `:` 在内:它是**类型判断**(`x: int`),要能当节首(`:.int`)、当成员名(`2.:`)。
        // 但它**不在** `OperatorSymbols.All` 里 —— 和 `!` / `&&` / `||` 一样归"求值器特判、
        // 不支持自定义"那一档(见 `ParseOperatorDefinition` 开头那道拦截)。
        TokenType.Colon
            or TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.StarStar or TokenType.Slash or TokenType.Percent
            or TokenType.EqualEqual or TokenType.NotEqual
            or TokenType.Less or TokenType.Greater or TokenType.LessEqual or TokenType.GreaterEqual
            or TokenType.Subtype or TokenType.Supertype
            or TokenType.ShiftLeft or TokenType.ShiftRight or TokenType.RotateLeft or TokenType.RotateRight
            or TokenType.And or TokenType.Pipe or TokenType.Caret => true,
        _ => false,
    };

    private static bool IsDefinitionOp(TokenType t)
        => t is TokenType.ColonEqual or TokenType.ColonColonEqual or TokenType.ColonColon or TokenType.Colon;

    private static readonly HashSet<string> ReservedWords =
        [ObjectVal.ThisTypeMember, ObjectVal.BlockMember, Attr.Core];

    private int _doDepth;

    private sealed class LoopCtx
    {
        public string? Label;
        public string Brk = "";
        public string Cont = "";
        public bool BrkUsed;
        public bool ContUsed;
        public int BodySeen;          // 走到第几个实参了
        public int BodyAt = 2;        // **体是第几个实参** —— `while 条件 体` 是 2,
                                      // `序列.Foreach 体` 是 1(见 `ParsePostfixRest` 里认循环那段)
    }

    private readonly List<LoopCtx> _loops = [];
    private int _loopCount;

    private string? _pendingLabel;

    private string? _pendingBrk;

    private string? _pendingLabelName;

    private bool _pendingBrkUsed;

    private readonly List<bool> _returnUsed = [];

    private Statement ParseBreakContinue()
    {
        var kw = Peek();
        _pos++;
        string? label = null;
        if (Check(TokenType.Identifier) && !CheckNext(TokenType.Equal))
        {
            label = Peek().Lexeme;
            _pos++;
        }

        var ctx = label is null
            ? (_loops.Count > 0 ? _loops[^1] : null)
            : _loops.FindLast(c => c.Label == label);

        if (ctx is null)
            throw new SyntaxException(
                label is null
                    ? "'break' / 'continue' 得写在循环里（`while` / `.Foreach`，或者带标签的语句）"
                    : $"找不到标签 '{label}' 标的那个循环",
                new SourceSpot(source, kw.Line, kw.Column));

        var isBreak = kw.Lexeme == "break";
        var name = isBreak ? ctx.Brk : ctx.Cont;
        if (name.Length == 0)
        {
            name = (isBreak ? "__brk" : "__cont") + (++_loopCount);
            if (isBreak) ctx.Brk = name; else ctx.Cont = name;
        }
        if (isBreak) ctx.BrkUsed = true; else ctx.ContUsed = true;
        if (label is not null && label == _pendingLabelName) _pendingBrkUsed = true;

        var fn = new IdentifierExpr(name) { Line = kw.Line, Column = kw.Column };
        var arg = new VoidLiteral { Line = kw.Line, Column = kw.Column };
        return new ExpressionStatement(new CallExpr(fn, arg) { Line = kw.Line, Column = kw.Column })
            { Line = kw.Line, Column = kw.Column };
    }

    private Expression WrapLoopBody(Expression body, string cont, Token at)
        => body is LambdaExpr lam
            ? lam with { Body = AsBlock(CallCC(cont, AsBlock(lam.Body, at), at), at) }
            : AsBlock(CallCC(cont, AsBlock(body, at), at), at);

    private Expression WrapLoopWhole(Expression loop, string brk, Token at)
        => CallCC(brk, AsBlock(loop, at), at);

    private Expression CallCC(string name, BlockExpr body, Token at)
    {
        var param = new Parameter(name, new IdentifierExpr("object") { Line = at.Line, Column = at.Column });
        var lam = new LambdaExpr(param, body) { Line = at.Line, Column = at.Column };
        return new CallExpr(new IdentifierExpr("callcc") { Line = at.Line, Column = at.Column }, lam)
            { Line = at.Line, Column = at.Column };
    }

    private Statement WrapLabelStmt(Statement inner, string brk, Token lbl)
    {
        var at = new SourceSpot(source, lbl.Line, lbl.Column);
        var param = new Parameter(brk, new IdentifierExpr("object") { Line = lbl.Line, Column = lbl.Column });
        var lam = new LambdaExpr(param, Block([inner], lbl)) { Line = lbl.Line, Column = lbl.Column };
        var call = new CallExpr(new IdentifierExpr("callcc") { Line = lbl.Line, Column = lbl.Column }, lam)
            { Line = lbl.Line, Column = lbl.Column };
        return new ExpressionStatement(call) { Line = lbl.Line, Column = lbl.Column };
    }

    private bool LooksLikeUseOfTheName()
        => IsDefinitionOp(NextType()) || CheckNext(TokenType.Equal)
           || CheckNext(TokenType.Dot) || CheckNext(TokenType.QuestionDot);

    private bool IsBindStart() => Check(TokenType.Identifier) && CheckNext(TokenType.BindArrow);

    private Statement ParseStatement()
    {
        // `x :< m`:do 块里的取值绑定。别处写就是语法错误 —— 它没有独立语义。
        if (IsBindStart())
        {
            if (_doDepth == 0) throw ParseError("':<' 只能写在 do { … } 里");
            var name = Consume(TokenType.Identifier, "需要变量名");
            Consume(TokenType.BindArrow, "需要 ':<'");
            _holeCount = 0;
            var monad = ParseExpression();
            SkipNewlines();
            if (HasHoles(monad)) monad = DesugarHoles(monad);
            SkipNewlines();
            return new BindStatement(name.Lexeme, monad) { Line = name.Line, Column = name.Column };
        }

        // 运算符定义:`+ := f`(定义) / `+ = f`(覆盖)。符号本身就是成员名。
        if (IsOperatorToken(Peek().Type) && _pos + 1 < tokens.Count)
        {
            var nt = tokens[_pos + 1].Type;
            if (nt is TokenType.ColonEqual or TokenType.Equal)
                return ParseOperatorDefinition();
        }

        // `++` / `--` 落在语句**开头**,只有两种来路:上一条没把它收走(`y := x++`),
        // 或者写了前缀(`++x`)。**两种都不认** —— 它只跟在变量/字段后面、自己单独成句。
        // 不专门拦的话会落进 `ParsePrimary`,报「需要表达式，但得到 '++'」,
        // 看不出是这两件事里的一件。
        if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
        {
            var inc = Peek();
            throw ParseError($"'{inc.Lexeme}' 只能跟在变量或字段后面、自己单独成句（`x{inc.Lexeme}`）——"
                           + "它是 `x += 1` 的简写，不交回值，所以写不进表达式里（前缀 `"
                           + inc.Lexeme + "x` 也不认）");
        }

        // 旧写法 operator+ add := ... 已废弃。不拦的话会被当表达式 `operator + add` 求值,
        // 报的却是「未定义的变量 'operator'」,看不出所以然。
        if (Check(TokenType.Identifier) && Peek().Lexeme == "operator" &&
            _pos + 1 < tokens.Count && IsOperatorToken(tokens[_pos + 1].Type))
            throw ParseError("运算符定义已改成直接用符号：`+ := f`（定义）或 `+ = f`（覆盖）");

        // 旧写法 `init ctor := ...` 同理:构造器现在只是名字叫 init 的变量,
        // 不拦下来就会去求值表达式 `init ctor`,报「未定义的变量 'init'」。
        if (Check(TokenType.Identifier) && Peek().Lexeme == ObjectVal.InitMember &&
            _pos + 1 < tokens.Count && tokens[_pos + 1].Type == TokenType.Identifier &&
            _pos + 2 < tokens.Count && tokens[_pos + 2].Type is TokenType.ColonEqual or TokenType.ColonColonEqual)
            throw ParseError("构造器不再用 init 修饰符，直接写 `init = () => { ... }`");

        // 修饰符 `override` / `new` 已删(它们只被记下来,全库没有一处读 —— 语言里既没有
        // 重载也没有重定义检查,写上去等于没写)。不拦的话 `override x := 1` 会被当表达式
        // `override x` 求值,报「未定义的变量 'override'」——看不出是这个修饰符没了。
        if (Check(TokenType.Identifier) && Peek().Lexeme is "override" or "new" &&
            _pos + 1 < tokens.Count && tokens[_pos + 1].Type == TokenType.Identifier)
            throw ParseError($"'{Peek().Lexeme}' 修饰符已删除（它只被记下来，没有任何地方读它）");

        // `forInstance` 同样已删:它当年是"类那一侧读不到"的凭据,而那个判据现在是**结构**的
        // —— 类对象有两张表,给实例的成员住在 `ClassVal.InstanceTable` 里,类那侧根本读不到。
        // 用户能写它的地方(变量定义)本来就没有实例表可落。同一条理由:不专门拦的话它会被
        // 当普通变量名,报「未定义的变量 'forInstance'」,看不出是这个修饰符没了。
        if (Check(TokenType.Identifier) && Peek().Lexeme is "forInstance" &&
            _pos + 1 < tokens.Count && tokens[_pos + 1].Type == TokenType.Identifier)
            throw ParseError("'forInstance' 修饰符已删除（类有「自己的」和「给实例的」两张表，住哪张表就是声明）");

        // 修饰符
        var attrs = new List<string>();
        while (Check(TokenType.Identifier))
        {
            var kw = Peek().Lexeme;
            if (!IsMod(kw)) break;
            // 下一个 token 是定义符 → 当前这个是**名字**,不是修饰符(`readonly := 1`
            // 定义的是名字叫 readonly 的变量)
            if (IsDefinitionOp(NextType())) break;
            _pos++;
            attrs.Add(kw);
        }

        // **解构定义**:`[x _ z ..rest] : T = e` / `{x y z} : U = e`。
        // 摆在修饰符那一段之后、`by` 那几条之前 —— `private [x y] : T = e` 也走这儿。
        if (LooksLikePatternDestructure())
            return ParseDestructure(attrs);

        if (attrs.Count > 0)
        {
            // `by a = X` / `by a.x = X`:换掉槽里的那份 property(见 SlotAssign)。
            // 只有 `by` 这一个修饰符有这个形态:别的修饰符后面跟 `=` 还是错的(`readonly x = 5`)——
            // `by` 特殊在它修饰的那个名字可以**是个属性槽**,而槽里的东西是可以换的。
            //
            // **必须 `attrs.Count == 1`**:SlotAssign 那条路不接别的修饰符,混着写会
            // 把它们**静默丢掉**(`readonly by r := property …` 里的 readonly 就是这么没的)。
            // 带别的修饰符时落回 `ParseDefinition`,那边会把 attrs 一个个装上。
            if (attrs.Count == 1 && attrs.Contains(Attr.By) && IsSlotAssignStart())
                return ParseSlotAssign();

            // 语句开头的 `by a.x` 取槽没地方落脚(那是**表达式**,见 SlotExpr)
            if (attrs.Contains(Attr.By) && Check(TokenType.Identifier) && CheckNext(TokenType.Dot))
                throw ParseError("语句开头只有换/建槽的写法（`by a.x = …` / `by a.x := …`）；取槽的 `by a.x` 是个表达式，得放在能用值的地方（比如 `p := by a.x`）");

            // `readonly x := 1`(修饰符 + 名字)或 `readonly := 1`(修饰符自己当名字)
            if ((Check(TokenType.Identifier) && IsDefinitionOp(NextType())) || IsDefinitionOp(Peek().Type))
                return ParseDefinition(attrs);
            throw ParseError("修饰符后需要 ':='（`by a = …` 那种换槽的写法只在 by 后面有）");
        }

        // `return v` —— **上下文关键字**:只在语句开头认,别处照旧是普通名字
        // (`return := 5` 还是定义,`x.return` 还是成员)。脱糖成对 `__return` 的一次调用;
        // 那枚 `__return` 由最近一层**用户写的** lambda 体包出来的 `callcc` 绑住
        // (见 `ParseUserLambdaBody`)。
        if (moreControlFlow && Check(TokenType.Identifier) && Peek().Lexeme == "return"
            && !LooksLikeUseOfTheName())
        {
            if (_returnUsed.Count == 0)
                throw ParseError("'return' 得写在函数体里 —— 顶层没有可返回的那个函数");

            var kw = Peek();
            _pos++;
            _holeCount = 0;
            Expression? value = null;
            if (!Check(TokenType.Newline) && !Check(TokenType.RightBrace) && !IsAtEnd())
                value = ParseExpression();
            SkipNewlines();
            if (value is not null && HasHoles(value)) value = DesugarHoles(value);
            _returnUsed[^1] = true;

            var fn = new IdentifierExpr("__return") { Line = kw.Line, Column = kw.Column };
            var arg = value ?? new VoidLiteral { Line = kw.Line, Column = kw.Column };
            var call = new CallExpr(fn, arg) { Line = kw.Line, Column = kw.Column };
            return new ExpressionStatement(call) { Line = kw.Line, Column = kw.Column };
        }

        // `break` / `continue` —— 和 `return` 一样是**上下文关键字**,也归同一个开关。
        if (moreControlFlow && Check(TokenType.Identifier)
            && Peek().Lexeme is "break" or "continue" && !LooksLikeUseOfTheName())
            return ParseBreakContinue();

        // `@outer <语句>` —— 给循环/语句起个名字,好让 `break outer` / `continue outer` 指着它跳。
        //
        // **前缀是 `@`**。从前这条写 `outer: <语句>`,和定义(`x: int = 5`)靠"往后找 `=`"分家;
        // `:` 改成类型判断之后分不了了(`outer: while …` 会读成判断),所以换个不撞任何东西的符号。
        // `@` 今天是词法错误,一分钱的兼容账都没有。
        //
        // 这条只在这个开关开着时才认,免得动到老代码。
        if (moreControlFlow && Check(TokenType.At))
        {
            _pos++;                          // 吃掉 '@'
            var lbl = Consume(TokenType.Identifier, "'@' 后面需要标签名");
            var outerLabel = _pendingLabel;
            var outerLabelName = _pendingLabelName;
            var outerBrk = _pendingBrk;
            var outerUsed = _pendingBrkUsed;
            _pendingLabel = lbl.Lexeme;
            _pendingLabelName = lbl.Lexeme;
            _pendingBrk = "__brk_" + lbl.Lexeme;
            _pendingBrkUsed = false;
            var inner = ParseStatement();
            var used = _pendingBrkUsed;
            _pendingLabel = outerLabel;
            _pendingLabelName = outerLabelName;
            _pendingBrk = outerBrk;
            _pendingBrkUsed = outerUsed;
            if (!used)
                throw new SyntaxException(
                    $"标签 '{lbl.Lexeme}' 没人用（写 `break {lbl.Lexeme}` 或 `continue {lbl.Lexeme}` 才指得上它）",
                    new SourceSpot(source, lbl.Line, lbl.Column));
            return WrapLabelStmt(inner, "__brk_" + lbl.Lexeme, lbl);
        }

        // 普通定义：IDENT := expr 或 IDENT : type = expr
        //
        // **`:` 这一支要看一眼前头再定**。`:` 现在是**类型判断**(`x: int`),而它同时还是
        // 定义的注解符 —— 两种读法的分岔只有一个:**注解后面跟不跟 `=`**。
        // 跟了就是定义(名字 + 注解 + 值),没跟就是一个光秃秃的判断,照表达式语句走。
        // (从前这里不用看:那会儿 `x: int` 单独写就是「类型注解后需要 '='」,没有第二种读法。)
        //
        // `:=` / `::=` / `::` 三条不在此列 —— 它们只可能是定义。
        if (Check(TokenType.Identifier) && NextType() == TokenType.Colon)
        {
            // `SkipTypeAnnotation` 收的是**相对 `_pos`** 的偏移(`TypeAt` 就是 `_pos + off`),
            // 注解从名字后面第 2 个 token 开始。
            var afterType = SkipTypeAnnotation(2);
            if (afterType >= 0 && TypeAt(afterType) == TokenType.Equal)
                return ParseDefinition();
        }
        else if (Check(TokenType.Identifier) && IsDefinitionOp(NextType()))
        {
            return ParseDefinition();
        }
        // IDENT = expr → 赋值
        if (Check(TokenType.Identifier) && CheckNext(TokenType.Equal))
            return ParseAssignment();

        // 独立的表达式语句
        _holeCount = 0;
        var startTok = _pos;
        var expr = ParseExpression();

        // **约束式定义**:`a == 1 = e` / `a |> IsPrime = e` —— `=` 左边不是左值(名字/成员),
        // 那它是一条**条件**:把右边绑给条件里**最左边那个标识符**,并要求条件成立(不成 → 拒收)。
        //
        // 走的是**解构那条路**(一个只有一格的模式,那一格带条件)—— 检查只写在 `Lowering.Bind`
        // 一处。`=` 而不是 `:=`:约束本身就说明了这是个定义(见 myMetaClass 草稿那句)。
        if (expr is BinaryExpr { Op: "=", Left: not (IdentifierExpr or MemberAccess) } cd)
        {
            var nm = FirstIdentName(startTok, _pos)
                ?? throw ParseError("'=' 左边不是左值 —— 那它得是条**条件**，而条件里要有个名字");
            var pat = new NamePattern(nm) { Guard = cd.Left, Line = cd.Left.Line, Column = cd.Left.Column };
            return new Destructure([], pat, cd.Right) { Line = cd.Line, Column = cd.Column };
        }

        // 赋值那几样(`:=` / `+=` …)的左边得是**左值**。`(f x) := …` / `(a + b) += 1` 这种
        // 求值期一定会报「赋值的左边得是个变量名或字段」,可树已经造出来了 —— 而打印器把它印回
        // `<表达式> := <表达式>`,那个形状在语句位置**读不出赋值**(先掉进模式那条路,报
        // 「模式括号没闭上」)。错在左边不是左值,指在这儿比等求值准。
        if (expr is BinaryExpr { Op: ":=" or "+=" or "-=" or "*=" or "**=" or "/=" or "%=" } bad
            && bad.Left is not (IdentifierExpr or MemberAccess))
            throw new SyntaxException(
                $"'{bad.Op}' 的左边得是变量或成员（和 '=' 一样）",
                new SourceSpot(source, bad.Line, bad.Column));

        // `x++` / `x--`。**紧贴着**刚算完的表达式看,不跨换行 —— `x` 一行、`++` 一行
        // 那是两条语句,不是自增(第二条到语句开头会被上面那条拦下来)。
        if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
            return ParseIncDec(expr);

        SkipNewlines();
        if (HasHoles(expr)) expr = DesugarHoles(expr);
        return new ExpressionStatement(expr) { Line = expr.Line, Column = expr.Column };
    }

    private Statement ParseIncDec(Expression target)
    {
        var op = Peek();
        _pos++;

        if (target is not (IdentifierExpr or MemberAccess))
            throw new SyntaxException(
                $"'{op.Lexeme}' 只能跟在变量或字段后面，而且得自己单独成句"
                + $"（`x{op.Lexeme}` / `a.b{op.Lexeme}`）—— 它是 `x += 1` 的简写，不交回值",
                new SourceSpot(source, op.Line, op.Column));

        var inc = new UnaryExpr(op.Lexeme, target) { Line = target.Line, Column = target.Column };

        // **后面得收尾**。这条不是洁癖:`a--b` 会**安安静静**读成 `a--` 和 `b` 两条语句
        // (Ravel 靠换行分句,所以同一条语句里再冒出个 `b` 本来就说不通)——
        // 而写它的人十有八九是想要 `a - (-b)`。静默地做成另一件事,比报错难查得多。
        // `;` 在词法那一步就变成换行了,所以这儿只管换行/收尾两种。
        if (!Check(TokenType.Newline) && !Check(TokenType.RightBrace) && !IsAtEnd())
            throw new SyntaxException(
                $"'{op.Lexeme}' 后面得收尾 —— `a{op.Lexeme}b` 会被读成 `a{op.Lexeme}` 和 `b` 两条语句"
                + (op.Type == TokenType.MinusMinus
                    ? "（连着写两个负号的话，中间那个空格不能省：`a - -b`）"
                    : ""),
                new SourceSpot(source, Peek().Line, Peek().Column));

        SkipNewlines();
        return new ExpressionStatement(inc) { Line = inc.Line, Column = inc.Column };
    }

    private bool LooksLikePatternDestructure()
    {
        if (Peek().Type is not (TokenType.LeftBracket or TokenType.LeftBrace or TokenType.LeftParen))
            return false;

        // `(` 开头的:括号只是分组,里面是模式 —— `(myPoint := {x y}) : Point = v`。
        var off = SkipBalanced(0);
        if (off < 0) return false;                          // 括号没闭上
        if (TypeAt(off) == TokenType.ColonEqual) return true;

        // `[a b] |> f := e` / `[a b] |> f = e` —— 模式后面挂了「整块」条件。
        // `|>` 后面那条谓词一直到行尾,中间那个 `:=` / `=` 就是"这是个定义"的证据
        // (`When` 是按 `ParseClimb(allowCall: false, 0)` 读的,**到赋值就停**,所以
        // 深度 0 上再出现一个 `=` 只可能是这一句自己的定义号)。
        if (TypeAt(off) == TokenType.PipeInto)
        {
            var depth = 0;
            for (var i = off + 1; ; i++)
            {
                var t = TypeAt(i);
                if (t is TokenType.EndOfFile or TokenType.Newline) return false;
                if (t is TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace) depth++;
                else if (t is TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace) depth--;
                else if (depth == 0 && t is TokenType.ColonEqual or TokenType.Equal) return true;
            }
        }

        if (TypeAt(off) != TokenType.Colon) return false;
        var after = SkipTypeAnnotation(off + 1);
        return after >= 0 && TypeAt(after) == TokenType.Equal;
    }

    private int SkipBalanced(int off)
    {
        var depth = 0;
        while (true)
        {
            switch (TypeAt(off))
            {
                case TokenType.EndOfFile: return -1;
                case TokenType.LeftParen:
                case TokenType.LeftBracket:
                case TokenType.LeftBrace:
                    depth++;
                    break;
                case TokenType.RightParen:
                case TokenType.RightBracket:
                case TokenType.RightBrace:
                    if (--depth == 0) return off + 1;
                    break;
            }
            off++;
        }
    }

    private Statement ParseDestructure(List<string> attrs)
    {
        var at = Peek();

        // `by` 在这儿和别处一个意思:**那一格的值是一份 property,名字就成了槽**。
        // (从前这儿拦着"by 是换槽、解构拆的是一串新名字"—— 那句把 `by` 的两个形态
        //  `by a = X`(**换**槽)和 `by a := X` / `by a: int = X`(**建**槽)混成了一件事;
        //  解构走的正是建槽那条,和变量定义一个待遇。修饰符照旧摊到每个名字上。)
        //
        // 类型由 `ParsePattern` 自己吃(写在**模式那一格**上)—— 所以 `[x y] : list` 和
        // `[x: int y: string]` 是同一条路:`:` 挂到哪一格,就是哪一格的事。
        var pattern = ParsePattern();
        RejectBareSub(pattern);

        if (!Match(TokenType.ColonEqual) && !Match(TokenType.Equal))
            throw ParseError("解构定义需要 '=' 或 ':='（`[x y] : T = e` / `[x y] := e`）");

        _holeCount = 0;
        var value = ParseExpression();
        SkipNewlines();
        if (HasHoles(value)) value = DesugarHoles(value);

        return new Destructure(attrs, pattern, value) { Line = at.Line, Column = at.Column };
    }

    private bool EndsThisPart()
        => Check(TokenType.Colon) || Check(TokenType.Identifier) || Check(TokenType.DotDot)
        || Check(TokenType.LeftBracket) || Check(TokenType.LeftBrace)
        || Check(TokenType.RightBracket) || Check(TokenType.RightBrace)
        || Check(TokenType.RightParen)             // `(x)` 里那个收尾 —— 也是"这一格完了"
        || Check(TokenType.ColonEqual)             // `p := 子模式` —— 名字自己收尾,别当条件读
        || Check(TokenType.Equal)                  // `{新名 = 成员名}` / `p = 子模式` 同理
                                                   // (`==` 是**另一个** token,这边的条件写法不受影响)
        || Check(TokenType.PipeInto)               // `|>` 归 ParsePattern —— 别读进条件里
        || Check(TokenType.Number) || Check(TokenType.String)              // 字面量的下一格
        || (Check(TokenType.LeftParen) && TypeAt(1) == TokenType.RightParen)   // 只认 `()`,
                                                                               // `(f x)` 还是条件
        || Check(TokenType.Newline) || IsAtEnd();

    private bool IsLiteralStart()
        => Check(TokenType.Number) || Check(TokenType.String)
        || (Check(TokenType.LeftParen) && TypeAt(1) == TokenType.RightParen)   // `()`,不是 `(f x)`
        || (Check(TokenType.Identifier) && Peek().Lexeme is "true" or "false");

    private Pattern ParseLiteralPattern()
    {
        var at = Peek();
        var neg = Match(TokenType.Minus);
        var kind = Peek().Type;
        var raw = Peek().Lexeme;
        var lit = ParsePrimary(bareLambda: false);

        var text = kind switch
        {
            TokenType.LeftParen => "()",
            TokenType.String => "\"" + raw + "\"",     // 词法存的是**解码后**的值,引号自己补
            _ => (neg ? "-" : "") + raw,
        };
        var domain = kind switch
        {
            TokenType.String => "string",
            TokenType.LeftParen => "void",
            TokenType.Identifier => "bool",              // `true` / `false`
            _ => "INumber",
        };
        var value = neg ? new UnaryExpr("-", lit) { Line = at.Line, Column = at.Column } : lit;
        return new LiteralPattern(value, new IdentifierExpr(domain) { Line = at.Line, Column = at.Column }, text)
            { Line = at.Line, Column = at.Column };
    }

    private bool LooksLikeDictEntry()
    {
        var off = 0;
        switch (TypeAt(off))
        {
            case TokenType.String or TokenType.Number or TokenType.Identifier: off++; break;
            case TokenType.LeftParen when TypeAt(1) == TokenType.RightParen: off += 2; break;   // `()`
            case TokenType.Minus when TypeAt(1) == TokenType.Number: off += 2; break;           // `-1`
            default: return false;
        }
        while (TypeAt(off) == TokenType.Dot && TypeAt(off + 1) == TokenType.Identifier) off += 2;
        return TypeAt(off) == TokenType.DictArrow;
    }

    private void RejectBareSub(Pattern p)
    {
        if (p is NamePattern { Sub: NamePattern { Sub: null, Member: null, Type: null, Guard: null, When: null } })
            throw ParseError("`=` 右边得是**拆法**（`[a ..rest]` / `{x y}` / `{\"k\" -> v}` 那种，"
                           + "或者一个字面量）—— 光一个名字没有意义；要**改名**那是对象模式的写法"
                           + "（`{新名 := 成员名}`，带类型就 `{新名 : 类型 = 成员名}`）");
    }

    private Expression ParseDictKey()
    {
        var at = Peek();
        Expression key;
        if (Match(TokenType.Minus))
            key = new UnaryExpr("-", ParsePrimary(bareLambda: false)) { Line = at.Line, Column = at.Column };
        else
            key = ParsePrimary(bareLambda: false);
        while (Match(TokenType.Dot))
            key = new MemberAccess(key, ParseMemberName()) { Line = at.Line, Column = at.Column };
        return key;
    }

    private Pattern ParsePattern()
    {
        _patternDepth++;
        try { return ParsePatternHere(); }
        finally { _patternDepth--; }
    }

    private int _patternDepth;

    private Pattern ParsePatternHere()
    {
        // **每一格前面也能写修饰符**(`[private x]` / `{"k" -> readonly v}`)—— 和参数那一圈
        // 同一个收集器、同一条判据(见 `CollectModifiers`)。它说的是**这一格拆出来的那个名字**,
        // 和外层写的那份**叠起来**(见 `Pattern.Attrs`)。
        //
        // 最外那一层通常是**空的**:语句那边挂在 `Destructure.Attrs` 上、参数那边挂在
        // `Parameter.Attrs` 上(它们要先收一步,好判"是不是解构"),轮到这儿已经没有了。
        var attrs = CollectModifiers();

        var p = ParsePatternBare();
        if (attrs is not null) p = p with { Attrs = attrs };
        if (Match(TokenType.Colon))
            p = p with { Type = ParseTypeAnnotation() };
        // `|>` 贴的是**刚结束的那一格**(不管它是名字、还是整个 `[…]` / `{…}`):
        // 把那一格的值喂给右边那个函数,不成立就拒收。于是每一层、每一格都能写。
        if (Match(TokenType.PipeInto))
            p = p with { When = ParseClimb(allowCall: false, 0) };

        // `名字 [: 类型] [|> 条件] (:= | =) 子模式` —— **值和它的拆法都要**:
        //   (myPoint := {x y})                 ← 光杆名字那支用 `:=`
        //   (myPoint |> IsSomeWhere = {x y})   ← 前面带了限制(类型 / 条件)那支用 `=`
        //   [first : list = [a ..rest] _ ..other]
        // 两种号的分工和语句级那条一个规矩 —— **约束本身就说明了这是个定义**。
        if (_patternDepth > 1 && p is NamePattern np
            && (Check(TokenType.ColonEqual) || Check(TokenType.Equal)))
        {
            _pos++;
            p = np with { Sub = ParsePattern() };
        }
        return p;
    }

    private Pattern ParsePatternBare()
    {
        var at = Peek();

        if (Match(TokenType.LeftBracket))
        {
            var parts = new List<Pattern>();
            while (!Check(TokenType.RightBracket) && !IsAtEnd())
            {
                var part = ParsePattern();
                RejectBareSub(part);
                parts.Add(part);
                SkipNewlines();
            }
            Consume(TokenType.RightBracket, "列表模式末尾需要 ']'");
            // `[]` 是**零格**:要求它是空的(`None` / 空 list / 空串)。
            // —— `Some` / `None` 分派就靠它收尾(`[v]` 认有值的那半,`[]` 认空的那半)。
            EnsureRestIsLast(parts);
            return new ListPattern(parts) { Line = at.Line, Column = at.Column };
        }

        if (Match(TokenType.LeftBrace))
        {
            // `{…}` 里两种项,**不许混**(一个按成员名、一个按键,底下是两套取法):
            //   成员名:  `{x y}`         —— `Fields ()` + 成员访问
            //   字典项:  `{"a" -> v}`    —— `Has` / `Get`
            var names = new List<NamePattern>();
            var entries = new List<DictPatEntry>();
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                if (LooksLikeDictEntry())
                {
                    if (names.Count > 0)
                        throw ParseError("对象成员（`{x y}`）和字典项（`{键 -> 模式}`）别混在一个 `{…}` 里 —— 底下是两套取法，分开写");
                    var key = ParseDictKey();
                    Consume(TokenType.DictArrow, "字典项的键后面需要 '->'");
                    var sub = ParsePattern();
                    RejectBareSub(sub);
                    entries.Add(new DictPatEntry(key, sub) { Line = at.Line, Column = at.Column });
                }
                else
                {
                    if (entries.Count > 0)
                        throw ParseError("字典项（`{键 -> 模式}`）和对象成员（`{x y}`）别混在一个 `{…}` 里 —— 底下是两套取法，分开写");
                    if (Check(TokenType.Identifier) && Peek().Lexeme == "_")
                        throw ParseError("对象模式里没有 '_' 这一格 —— 不想要的成员别写出来就行");
                    if (ParsePattern() is not NamePattern n)
                        throw ParseError("对象模式里每一项是一个成员名（`{x y}`，能带类型 `{n: int}`），"
                                       + "要么整项写成字典项（`{键 -> 模式}`）");

                    // `{新名 : 类型 = 成员名}` —— `=` 右边是**成员名**,不是子模式。
                    // 上面那道口子已经把 `= x` 读成 `Sub` 了,这儿搬过来:取成员 `x`、绑成
                    // `新名`。`{x}` 不带 `=`,就是同名(≡ `{x = x}`)。
                    if (n.Sub is not null)
                    {
                        // `sel.Attrs` 也算:右边是**取哪个成员**的引用,不是声明 —— 挂修饰符没处落,
                        // 放过去就是**静默丢掉**那个词(这条判断不比配 `Attrs` 的话就漏了)。
                        if (n.Sub is not NamePattern { Sub: null, Member: null, Type: null, Guard: null, When: null } sel
                            || sel.Attrs is not null)
                            throw ParseError("对象模式里 `=` 后面写**成员名**（`{新名 = 成员名}`）—— "
                                           + "要拆那一项的值,把整项写成列表/字典那边那种模式");
                        n = n with { Member = sel.Name, Sub = null };
                    }
                    names.Add(n);
                }
                SkipNewlines();
            }
            Consume(TokenType.RightBrace, "模式末尾需要 '}'");
            if (entries.Count > 0) return new DictPattern(entries) { Line = at.Line, Column = at.Column };
            if (names.Count == 0) throw ParseError("模式里得写点什么（`{x y}` 或 `{键 -> 模式}`）");
            return new MemberPattern(names) { Line = at.Line, Column = at.Column };
        }

        if (Match(TokenType.DotDot))
        {
            var n = Consume(TokenType.Identifier, "'..' 后面要跟个名字（`..rest` —— 名字自己起）");
            // `_` 是丢弃/占位的记号,不是名字 —— 真放过去就悄悄绑出一个叫 `_` 的变量,
            // 后面 `print _` 又当洞消糖,出一堆看不懂的错。
            if (n.Lexeme == "_")
                throw ParseError("'..' 后面要一个真名字 —— `_` 是丢弃/占位的记号，当不了名字");
            return new RestPattern(n.Lexeme) { Line = at.Line, Column = at.Column };
        }

        // **字面量那一格**:`[1 v]` / `["ok" v]` / `[() x]` / `[true x]` —— 要求这一格等于它。
        // 开头的 `-` 只可能是负号(`[-1 v]`):一格的**开头**放不出二元运算符,没歧义。
        // (`[x -1]` 仍旧读成条件 `x - 1` —— 那儿 `-` 在**中间**,是二元,和从前一样。)
        if (IsLiteralStart() || Check(TokenType.Minus))
            return ParseLiteralPattern();

        // `( 模式 )` —— 括号只是**分组**,里面还是同一格模式(`(((x:(int))))` 也认)。
        // **`()` 走不到这儿**:那是"单位那个值"的字面量,上面那条先接走了。
        if (Match(TokenType.LeftParen))
        {
            var open = Previous();
            var inner = ParsePattern();
            Consume(TokenType.RightParen, "模式括号没闭上");
            // **连套几层折叠成一层** —— "无限制的 `()` 可以拆":`(((x)))` 和 `(x)` 是一回事。
            // 不折的话 N 层括号就是 N 个节点,而它们一个意思(见 `WholePattern` 的抬头)。
            return inner is WholePattern ? inner : new WholePattern(inner) { Line = open.Line, Column = open.Column };
        }

        var saveAt = _pos;
        var name = Consume(TokenType.Identifier, "模式里要写一个名字、`_`、`..名字`、还是一个字面量（`1` / `\"看\"` / `()` / `true`）");
        if (name.Lexeme == "_")
        {
            // `_` 什么都不绑,所以它**挂不了任何东西**:`: 类型` 和 `|> 条件` 都是"对着
            // 这个名字说事"。不拦的话 `[_ == 1]` 会一路走到下一格的名字那儿报
            // 「模式里要写一个名字」—— 指错了地方。
            if (Check(TokenType.Colon) || Check(TokenType.PipeInto) || !EndsThisPart())
                throw ParseError("`_` 不绑东西，挂不了 `: 类型` 也挂不了 `|> 条件` —— 想验那一格就给它一个名字");
            return new SkipPattern { Line = at.Line, Column = at.Column };
        }

        // **带条件的一格**:`u == 1` —— 名字取条件里最左边那个标识符(和参数守卫一个规矩)。
        // 后面跟的不是"这一格的收尾/下一格的开始",就当条件读。读的时候**不吃并列的实参**
        // (`allowCall: false`)—— 不然 `[u == 1 v]` 那一格会把后面的 `v` 一起吞了。
        if (EndsThisPart())
            return new NamePattern(name.Lexeme) { Line = at.Line, Column = at.Column };

        _pos = saveAt;
        var from = _pos;
        var guard = ParseClimb(allowCall: false, 0);
        if (FirstIdentName(from, _pos) is not { } nm)
            throw ParseError("这一格的条件里得有个名字 —— 它**最左边**那个标识符就是");
        return new NamePattern(nm) { Guard = guard, Line = at.Line, Column = at.Column };
    }

    private void EnsureRestIsLast(List<Pattern> parts)
    {
        for (var i = 0; i < parts.Count - 1; i++)
            if (parts[i] is RestPattern)
                throw new SyntaxException("'..名字' 只能写在模式的最后一项 —— 它把后面都吃了",
                    new SourceSpot(source, parts[i].Line, parts[i].Column));
    }

    private string ParseMemberName()
    {
        // 两段是一件事:**吃掉这个 token、把它的文本当成员名**
        // (运算符符号本身就是成员名:`2.+` / `"a".==`)。
        if (Check(TokenType.Identifier) || IsOperatorToken(Peek().Type))
        {
            _pos++;
            return Previous().Lexeme;
        }

        throw ParseError("'.' 后需要成员名");
    }

    private Statement ParseOperatorDefinition()
    {
        var sym = Peek();
        // `:` 在 `IsOperatorToken` 里(要当节首 `:.int`、当成员名 `2.:`),但它**不给定义** ——
        // 和 `!` / `&&` / `||` 一样是求值器特判的。不拦的话 `: := f` 会建出一个没人查的成员。
        if (sym.Type == TokenType.Colon)
            throw ParseError("':' 不支持自定义 —— 它是内建的类型判断（`x: T`），和 `!` / `&&` / `||` 一样");
        _pos++;
        if (Match(TokenType.ColonEqual))
            return new VarDefinition(sym.Lexeme, null, ParseExpression()) { Line = sym.Line, Column = sym.Column };

        Match(TokenType.Equal);
        return new Assignment(sym.Lexeme, ParseExpression()) { Line = sym.Line, Column = sym.Column };
    }

    private Expression ParseTypeAnnotation()
    {
        if (Check(TokenType.LeftParen)) return ParsePrimary();   // (任意表达式) —— ParsePrimary 自己吃掉右括号

        var t = Consume(TokenType.Identifier, "注解需要一个类型名，或者括号里的表达式");
        Expression e = new IdentifierExpr(t.Lexeme) { Line = t.Line, Column = t.Column };
        while (Match(TokenType.Dot))
        {
            var m = Consume(TokenType.Identifier, "'.' 后需要成员名");
            e = new MemberAccess(e, m.Lexeme) { Line = t.Line, Column = t.Column };
        }

        return e;
    }

    private Statement ParseDefinition(List<string>? attrs = null)
    {
        string name;
        int line, col;
        if (Check(TokenType.Identifier))
        {
            var n = Consume(TokenType.Identifier, "需要变量名");
            name = n.Lexeme;
            line = n.Line;
            col = n.Column;
        }
        else if (attrs is { Count: > 0 })
        {
            // 修饰符本身当名字用(如 `readonly := ...`):位置取刚吃掉的那个修饰符
            name = attrs[0];
            line = Previous().Line;
            col = Previous().Column;
        }
        else
        {
            throw ParseError("需要变量名");
        }

        Expression? typeAnnotation = null;
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
            typeAnnotation = ParseTypeAnnotation();
            Consume(TokenType.Equal, "类型注解后需要 '='");
        }
        else if (Match(TokenType.ColonEqual))
        {
            // := → 类型推断
        }
        else if (Match(TokenType.Colon))
        {
            typeAnnotation = ParseTypeAnnotation();
            Consume(TokenType.Equal, "类型注解后需要 '='");
        }
        else
        {
            throw ParseError("变量定义需要 ':='、'::='、': type =' 或 ':: type ='");
        }

        _holeCount = 0;
        var value = ParseExpression();
        SkipNewlines();
        // 定义处的 `_` 是"填空"：`x: T = _` 就是 `x: T = T ()`；
        // `by a: T = _` 填的是**属性**那一格，所以是 `property ()`（一对什么都不做的 getter/setter）。
        if (value is HoleExpr)
        {
            var arg = new LiteralExpr(VoidVal.Instance) { Line = value.Line, Column = value.Column };
            Expression? box = null;
            if (attrs?.Contains("by") == true)
                box = new CallExpr(new IdentifierExpr("property") { Line = value.Line, Column = value.Column }, arg)
                    { Line = value.Line, Column = value.Column };
            else if (typeAnnotation != null)
                box = new CallExpr(typeAnnotation, arg) { Line = value.Line, Column = value.Column };
            if (box != null) value = box;
        }
        if (HasHoles(value)) value = DesugarHoles(value);
        SkipNewlines();

        if (name == "_")
            return new ExpressionStatement(value) { Line = line, Column = col };

        return new VarDefinition(name, typeAnnotation, value, attrs, autoName)
        {
            Line = line,
            Column = col,
        };
    }

    private bool IsSlotAssignStart()
    {
        // 运算符也能当槽名(`by + := property …`:接口里声明它、实现里换掉它)。符号后面
        // 直接跟定义符/赋值符,没有 `.` 那一串 —— 槽路径本来就短。
        if (IsOperatorToken(Peek().Type) && _pos + 1 < tokens.Count)
            return tokens[_pos + 1].Type is TokenType.Equal or TokenType.ColonEqual;

        if (!Check(TokenType.Identifier)) return false;
        var i = _pos + 1;
        while (i + 1 < tokens.Count && tokens[i].Type == TokenType.Dot && tokens[i + 1].Type == TokenType.Identifier)
            i += 2;
        return i < tokens.Count && tokens[i].Type is TokenType.Equal or TokenType.ColonEqual;
    }

    private Statement ParseSlotAssign()
    {
        // 运算符符号本身就是成员名,所以它也能当槽路径(`by + = …` / `by + := …`)。
        // 这条不走 ParsePrimary —— 那会把 `+` 当成运算符节的开头(`+.2` 那种)。
        Expression path;
        if (IsOperatorToken(Peek().Type))
        {
            var sym = Peek();
            _pos++;
            path = new IdentifierExpr(sym.Lexeme) { Line = sym.Line, Column = sym.Column };
        }
        else
        {
            path = ParseMemberChain(ParsePrimary());
        }
        var define = Match(TokenType.ColonEqual);
        if (!define) Consume(TokenType.Equal, "需要 '=' 或 ':='");
        _holeCount = 0;
        var value = ParseExpression();
        SkipNewlines();
        // `by a := _` / `by a = _` —— 槽里填的是**属性**那一格，所以是 `property ()`。
        if (value is HoleExpr)
            value = new CallExpr(new IdentifierExpr("property") { Line = value.Line, Column = value.Column },
                new LiteralExpr(VoidVal.Instance) { Line = value.Line, Column = value.Column })
                { Line = value.Line, Column = value.Column };
        if (HasHoles(value)) value = DesugarHoles(value);
        return new SlotAssign(path, value, define) { Line = path.Line, Column = path.Column };
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

}
