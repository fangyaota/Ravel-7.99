namespace Ravel;

using System.Linq;
using Ravel.Runtime;

/// <summary>递归下降解析器的分片,按职责拆文件(和 Runtime/Interpreter.*.cs 一个路子):
/// `Parser.cs` 入口 + token 辅助,`Parser.Statements.cs` 语句,
/// `Parser.Expressions.cs` 优先级链,`Parser.Atoms.cs` 基本单元与括号/块。</summary>
public partial class Parser
{
    // ========================================
    //  语句
    // ========================================

    // 注意 init 不在此列:构造器是名字叫 init 的变量(类体里写 `init := ...`),不是修饰符
    private static bool IsMod(string kw) => Attr.All.Contains(kw);

    /// <summary>能作为运算符定义的符号 token(`+ := f` / `a.+`)。一元 `!` 和短路 `&&`/`||` 不在内——
    /// 它们是求值器特判的,不支持自定义。</summary>
    /// <summary>运算符节的开头:`+.2` / `:.int` —— 运算符后面紧跟 `.`。
    /// 节是"左操作数留空"的写法,`ParsePrimary` 认它。
    ///
    /// (从前这儿还认**词形**运算符 `is` / `isnot` —— 它们是"看着像标识符、只在运算符位置认"
    ///  的。`is` 换成 `:` 之后那整套机器拆掉了,节也只剩标点这一路。)</summary>
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

    /// <summary>定义符:四种写法都以它收尾(`:=` / `::=` / `: 注解 =` / `:: 注解 =`)。
    /// 它紧跟在一个名字后面 = "这是一条定义,不是表达式" —— 这个判断散在四处,
    /// 四钟写法列四遍,加一种就得记得回来补齐。
    ///
    /// (不在这里的是运算符定义的 `+ = f`,`=` 单独认,见 ParseOperatorDefinition。)</summary>
    private static bool IsDefinitionOp(TokenType t)
        => t is TokenType.ColonEqual or TokenType.ColonColonEqual or TokenType.ColonColon or TokenType.Colon;

    /// <summary>类机制内部词——禁止作为变量名（this/init 是类内可用变量，不禁）</summary>
    private static readonly HashSet<string> ReservedWords =
        [ObjectVal.ThisTypeMember, ObjectVal.BlockMember, Attr.Core];

    /// <summary>正处在多少个 `do { … }` 的语句里。`:<` 只有在这层里才算绑定,
    /// 而**块会把它清零**(见 <see cref="ParseBlockStatements"/>)—— 块是新的语境,
    /// 外层 do 的 `:<` 不该漏进一个 lambda 的体里。</summary>
    private int _doDepth;

    /// <summary>一层循环(`while` / `foreach`,或带标签的语句)。`break` / `continue` 往
    /// **最近一层**跳;写了标签就往**那个标签**跳(于是能跳出好几层)。</summary>
    private sealed class LoopCtx
    {
        public string? Label;
        public string Brk = "";
        public string Cont = "";
        public bool BrkUsed;
        public bool ContUsed;
        public int BodySeen;          // 走到第几个实参了(第 2 个是体)
    }

    private readonly List<LoopCtx> _loops = [];
    private int _loopCount;

    /// <summary>`outer:` 刚读完,等着贴到下面那条语句上(见 ParseStatement 里那段)。</summary>
    private string? _pendingLabel;

    /// <summary>那个标签的 break 出口叫什么(`__brk_<标签>`)—— 循环认领它,`break 标签` 指它。</summary>
    private string? _pendingBrk;

    /// <summary>正在解析的那个标签叫**什么**。循环认领会清掉 `_pendingLabel`(免得下一个循环
    /// 又把它认走),可名字得留着 —— 里面的 `break 标签` 要拿它比,才知道这标签被用过了。</summary>
    private string? _pendingLabelName;

    /// <summary>这个标签被人 `break` 过吗(没人用过就报错 —— 顺手把 "`x: int` 想写定义却漏了 `=`" 那种笔误也逮住)。</summary>
    private bool _pendingBrkUsed;

    /// <summary>"这层 lambda 体里出现过 `return` 吗" —— 每进一个**用户写的** lambda 体压一层
    /// (见 <see cref="ParseUserLambdaBody"/>),出来按需把那层体包成 `callcc`。
    /// 空 = 不在函数体里(那儿写 `return` 当场报错)。</summary>
    private readonly List<bool> _returnUsed = [];



    /// <summary>`break [标签]` / `continue [标签]`。脱糖成对 `__brk<n>` / `__cont<n>` 的一次
    /// 调用 —— 那两个名字由**循环那一层**包出来的 `callcc` 绑住(见 ParsePostfixRest 里那段)。</summary>
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
                    ? "'break' / 'continue' 得写在循环里（`while` / `foreach`，或者带标签的语句）"
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

    /// <summary>`continue` 的出口:把循环的**体**包一层 `callcc` —— "跳过这一轮"就是跳出
    /// 这个体。每轮都会新包一次,所以下一个 `continue` 照样管用。
    ///
    /// 体可能是**块**(`while` / `foreach` 的裸块),也可能是 **lambda**(`foreach xs (x) => {…}`)
    /// —— 后者要换的是 **lambda 的体**,不是整个 lambda,不然那个 `x` 就没了。</summary>
    private Expression WrapLoopBody(Expression body, string cont, Token at)
        => body is LambdaExpr lam
            ? lam with { Body = AsBlock(CallCC(cont, AsBlock(lam.Body, at), at), at) }
            : AsBlock(CallCC(cont, AsBlock(body, at), at), at);

    /// <summary>`break` 的出口(没标签的循环):把**整个循环调用**包一层 `callcc`。</summary>
    private Expression WrapLoopWhole(Expression loop, string brk, Token at)
        => CallCC(brk, AsBlock(loop, at), at);

    /// <summary>`callcc ((<名字>) => { <体> })` —— 这三个糖的公共形状。</summary>
    private Expression CallCC(string name, BlockExpr body, Token at)
    {
        var param = new Parameter(name, new IdentifierExpr("object") { Line = at.Line, Column = at.Column });
        var lam = new LambdaExpr(param, body) { Line = at.Line, Column = at.Column };
        return new CallExpr(new IdentifierExpr("callcc") { Line = at.Line, Column = at.Column }, lam)
            { Line = at.Line, Column = at.Column };
    }

    /// <summary>带标签的那条语句包一层 `callcc` —— `break 标签` 跳到这儿。
    /// (**循环那层不包 break**:标签认领过的循环把出口留给标签,自己只管 `continue` 那条。)</summary>
    private Statement WrapLabelStmt(Statement inner, string brk, Token lbl)
    {
        var at = new SourceSpot(source, lbl.Line, lbl.Column);
        var param = new Parameter(brk, new IdentifierExpr("object") { Line = lbl.Line, Column = lbl.Column });
        var lam = new LambdaExpr(param, Block([inner], lbl)) { Line = lbl.Line, Column = lbl.Column };
        var call = new CallExpr(new IdentifierExpr("callcc") { Line = lbl.Line, Column = lbl.Column }, lam)
            { Line = lbl.Line, Column = lbl.Column };
        return new ExpressionStatement(call) { Line = lbl.Line, Column = lbl.Column };
    }

    /// <summary>`return` 后面跟着的是"这名字"的用法而不是关键字吗 —— 判据就一条:
    /// 接下来那个 token 是不是定义/赋值/取成员。`return := 5` / `return = 5` / `x.return`
    /// 都照旧当普通名字走。</summary>
    private bool LooksLikeUseOfTheName()
        => IsDefinitionOp(NextType()) || CheckNext(TokenType.Equal)
           || CheckNext(TokenType.Dot) || CheckNext(TokenType.QuestionDot);

    /// <summary>这个位置是 `名字 :&lt;` 吗?只在**语句开头**问,所以别处出现 `:<`
    /// 只是个普通的语法错误("需要表达式，但得到 '&lt;'")。</summary>
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
        var expr = ParseExpression();

        // `x++` / `x--`。**紧贴着**刚算完的表达式看,不跨换行 —— `x` 一行、`++` 一行
        // 那是两条语句,不是自增(第二条到语句开头会被上面那条拦下来)。
        if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
            return ParseIncDec(expr);

        SkipNewlines();
        if (HasHoles(expr)) expr = DesugarHoles(expr);
        return new ExpressionStatement(expr) { Line = expr.Line, Column = expr.Column };
    }

    /// <summary>`x++` / `x--` —— **只在语句位置认**(上面那条分支),所以它是这条语言的
    /// 语法糖里最薄的一枚。解析器只造一枚 `UnaryExpr(op, target)`,**折成 `x += 1` /
    /// `x -= 1`** 是 <see cref="Lowering"/> 那趟的事。
    ///
    /// **它不交回值**,这是故意的: `y := x++` 是语法错误。C 里 `y = x++` 拿旧值、
    /// `y = ++x` 拿新值 —— "看符号写在哪边猜拿到哪个值"是那门语言最经典的一个坑,
    /// 而这两样在这儿一个都没有:当语句用的时候,`x++` 和 `x += 1` 一模一样。
    /// (要值就明写:`y := x; x += 1`。)
    ///
    /// 目标只能是**变量或字段**(和 `+=` 同一类),别的形状在这儿就报 —— 等到求值期
    /// 才报「复合赋值目标必须是变量」的话,插入符指着的是那个 `+=`,看不出是 `++` 用错了地方。
    /// (同理,"后面得收尾"那条也是**语法**规则,留在这儿。)</summary>
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

    /// <summary>语句开头这一格是不是**解构定义**:`[…]` / `{…}` 配对之后跟着 `:=`,
    /// 或者 `: 类型 =`。
    ///
    /// **必须是只看 token 的前瞻,不能"先试着解析一遍、不成再退"** —— 那就是
    /// `((((…))))` 那类指数(见 `Parser.Atoms` 里那条注释):`[` / `{` 每一层都会翻一倍。
    /// 这儿只配对括号 + 看一眼后面跟什么,一层的代价是 O(那一格的长度)。
    ///
    /// 判据里**要求有个 `=`**(`: T` 后面那个也算)是故意的:`[1 2] : List` 是一条
    /// 普通的类型判断语句,不能被当成"空注解的解构"。注解可省,但省了就得写 `:=`。</summary>
    private bool LooksLikePatternDestructure()
    {
        if (Peek().Type is not (TokenType.LeftBracket or TokenType.LeftBrace)) return false;

        var off = SkipBalanced(0);
        if (off < 0) return false;                          // 括号没闭上
        if (TypeAt(off) == TokenType.ColonEqual) return true;

        if (TypeAt(off) != TokenType.Colon) return false;
        var after = SkipTypeAnnotation(off + 1);
        return after >= 0 && TypeAt(after) == TokenType.Equal;
    }

    /// <summary>从 off 那一格的 `[` / `{` 跳到配对的收尾符后面那一格;配不上给 -1。
    /// 三种括号**一起数** —— `[{a b}]` 这样的嵌套也要配对得对。</summary>
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

    /// <summary>`[x _ z ..rest] : T = e` / `{x y z} : U = e`。
    ///
    /// 解析器只把这个形状读出来(一枚 <see cref="Destructure"/>)—— **怎么拆是
    /// `Lowering.Destructure` 的事**(和 `do` / `??` 一个规矩:造意思的活儿不留在这一层)。</summary>
    private Statement ParseDestructure(List<string> attrs)
    {
        var at = Peek();

        // `by` 是"换掉一份槽",解构拆出来的是一串**新名字** —— 两件事凑不到一起,
        // 混着写会在别处报一句看不懂的。这儿说清。
        if (attrs.Contains(Attr.By))
            throw ParseError("解构定义不能用 'by' —— 它拆出来的是一串新名字，不是槽");

        var pattern = ParsePattern();

        Expression? type = null;
        if (Match(TokenType.Colon)) type = ParseTypeAnnotation();

        if (!Match(TokenType.ColonEqual) && !Match(TokenType.Equal))
            throw ParseError("解构定义需要 '=' 或 ':='（`[x y] : T = e` / `[x y] := e`）");

        _holeCount = 0;
        var value = ParseExpression();
        SkipNewlines();
        if (HasHoles(value)) value = DesugarHoles(value);

        return new Destructure(attrs, pattern, type, value) { Line = at.Line, Column = at.Column };
    }

    /// <summary>读一格模式。四种写法:名字 / `_` / `..名字` / 嵌套的 `[…]` `{…}`。</summary>
    private Pattern ParsePattern()
    {
        var at = Peek();

        if (Match(TokenType.LeftBracket))
        {
            var parts = new List<Pattern>();
            while (!Check(TokenType.RightBracket) && !IsAtEnd())
            {
                parts.Add(ParsePattern());
                SkipNewlines();
            }
            Consume(TokenType.RightBracket, "列表模式末尾需要 ']'");
            if (parts.Count == 0) throw ParseError("列表模式里得有一格（`[]` 没东西可解）");
            EnsureRestIsLast(parts);
            return new ListPattern(parts) { Line = at.Line, Column = at.Column };
        }

        if (Match(TokenType.LeftBrace))
        {
            var names = new List<string>();
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                var n = Consume(TokenType.Identifier, "对象模式里要写成员名（`{x y}`）");
                if (n.Lexeme == "_") throw ParseError("对象模式里没有 '_' 这一格 —— 不想要的成员别写出来就行");
                names.Add(n.Lexeme);
                SkipNewlines();
            }
            Consume(TokenType.RightBrace, "对象模式末尾需要 '}'");
            if (names.Count == 0) throw ParseError("对象模式里得写成员名（`{x y}`）");
            return new MemberPattern(names) { Line = at.Line, Column = at.Column };
        }

        if (Match(TokenType.DotDot))
        {
            var n = Consume(TokenType.Identifier, "'..' 后面要跟个名字（`..rest`）");
            return new RestPattern(n.Lexeme) { Line = at.Line, Column = at.Column };
        }

        var name = Consume(TokenType.Identifier, "模式里要写一个名字、`_`、还是 `..名字`");
        if (name.Lexeme == "_") return new SkipPattern { Line = at.Line, Column = at.Column };
        return new NamePattern(name.Lexeme) { Line = at.Line, Column = at.Column };
    }

    /// <summary>`..rest` 只能写最后一项 —— 它把后面都吃了,写在中间后面的格子永远取不到
    /// (静默地永远不跑,正是这门语言最恨的那种)。</summary>
    private void EnsureRestIsLast(List<Pattern> parts)
    {
        for (var i = 0; i < parts.Count - 1; i++)
            if (parts[i] is RestPattern)
                throw new SyntaxException("'..rest' 只能写在模式的最后一项 —— 它把后面都吃了",
                    new SourceSpot(source, parts[i].Line, parts[i].Column));
    }

    /// <summary>`.` 后面那一段:普通成员名,或运算符符号(`2.+` / `"a".==`)</summary>
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

    /// <summary>`+ := f` 定义运算符;`+ = f` 覆盖已有的(父类层定义的)那个。符号即成员名。</summary>
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

    /// <summary>类型标注:**一个名字**(可带 `.` 成员访问),或**括号里的任意表达式**。
    ///
    /// 括号是必需的,不是可选:不带括号的 `f ()` 会和"下一个参数/字段"的写法撞上 ——
    /// `(x: int y: int)` 里那个 `int y` 会被解析成一次调用。有了括号就能写"算出来的类型":
    ///
    ///     a: (CallMeToGetARandomType ()) = 0
    ///     a: (if { flag; } { int; } { float; }) = 0
    ///
    /// 注解是一个**表达式**,求值发生在定义处(变量)或 lambda 创建处(参数)。</summary>
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

    /// <summary>name := expr  |  name: Type = expr</summary>
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

    /// <summary>这个位置是 `by 名字[.成员] = …` / `… := …` 吗?
    ///
    /// 只看 token、不动 AST(所以不存 `_pos` 再还回去那套):`by` 后面要么 `:`(注解,走定义)
    /// 要么这个形状(换/建槽),看 `名字` 后面那个 token 就分得清。</summary>
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

    /// <summary>`by a = 表达式` / `by a.x = 表达式`(`:=` 那种是**在那个对象上建槽**)——
    /// 换掉槽里的那份 property:不走旧属性的 setter,也不查类型约束
    /// (`by a: int = …` 那个约束管的是"写进属性的值",而这里换的是属性本身)。
    /// 和 `by a` / `by a.x`(取槽)对称,左边都是同一个"槽路径"。
    ///
    /// 和 `a = v`(过 setter)、`by a := property …`(定义槽)是三件不同的事。</summary>
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
