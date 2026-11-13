namespace Ravel;

using System.Linq;
using Ravel.Runtime;

public partial class Parser
{
    // 语句

    // init 不是修饰符，是构造器名字
    private static bool IsMod(string kw) => Attr.All.Contains(kw);

    private bool IsSectionStart()
        => IsOperatorToken(Peek().Type)
           && _pos + 1 < tokens.Count && tokens[_pos + 1].Type == TokenType.Dot;

    // ':' 与 '===' 也在此列，但它们不支持自定义
    private static bool IsOperatorToken(TokenType t) => t switch
    {
        TokenType.Colon or TokenType.EqualEqualEqual or TokenType.NotEqualEqual
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
        public int BodySeen;      // 走到第几个实参了
        public int BodyAt = 2;    // 体是第几个实参（while 2、Foreach 1）
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

    // 只看 token 分派，各形自己成句
    private Statement ParseStatement()
    {
        if (TryParseDirectiveStatement() is { } directive) return directive;
        if (TryParseBindStatement() is { } bind) return bind;
        if (TryParseOperatorDefinitionStatement() is { } operatorDef) return operatorDef;

        RejectRetiredStatementStart();

        if (TryParseModifiedStatement() is { } modified) return modified;
        if (TryParseControlFlowStatement() is { } flow) return flow;
        if (TryParseDefinitionOrAssign() is { } decl) return decl;
        return ParseExpressionStatement();
    }

    // 开关语句：改掉开关，不产出代码
    private Statement? TryParseDirectiveStatement()
    {
        if (!Check(TokenType.Directive)) return null;
        var at = Peek();
        TakeDirective();
        SkipNewlines();
        return new ExpressionStatement(new LiteralExpr(VoidVal.Instance) { Line = at.Line, Column = at.Column })
        {
            Line = at.Line, Column = at.Column, Synthetic = true,
        };
    }

    // ':<' 只在 do 块里有意义
    private Statement? TryParseBindStatement()
    {
        if (!IsBindStart()) return null;
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

    // 运算符定义：`+ := f` / `+ = f`
    private Statement? TryParseOperatorDefinitionStatement()
    {
        if (!IsOperatorToken(Peek().Type) || _pos + 1 >= tokens.Count) return null;
        if (tokens[_pos + 1].Type is not (TokenType.ColonEqual or TokenType.Equal)) return null;
        return ParseOperatorDefinition();
    }

    // 已删写法；不拦会报成未定义变量
    private void RejectRetiredStatementStart()
    {
        RejectBareIncDec();
        RejectRetiredOperatorSyntax();
        RejectRetiredInitSyntax();
        RejectRetiredModifier();
    }

    // 单独起句只有写错这一种来路
    private void RejectBareIncDec()
    {
        if (!Check(TokenType.PlusPlus) && !Check(TokenType.MinusMinus)) return;
        var inc = Peek();
        throw ParseError($"'{inc.Lexeme}' 只能跟在变量或字段后面、自己单独成句（`x{inc.Lexeme}`）——"
                       + "它是 `x += 1` 的简写，不交回值，所以写不进表达式里（前缀 `"
                       + inc.Lexeme + "x` 也不认）");
    }

    private void RejectRetiredOperatorSyntax()
    {
        if (!Check(TokenType.Identifier) || Peek().Lexeme != "operator") return;
        if (_pos + 1 >= tokens.Count || !IsOperatorToken(tokens[_pos + 1].Type)) return;
        throw ParseError("运算符定义已改成直接用符号：`+ := f`（定义）或 `+ = f`（覆盖）");
    }

    private void RejectRetiredInitSyntax()
    {
        if (!Check(TokenType.Identifier) || Peek().Lexeme != ObjectVal.InitMember) return;
        if (_pos + 1 >= tokens.Count || tokens[_pos + 1].Type != TokenType.Identifier) return;
        if (_pos + 2 >= tokens.Count) return;
        if (tokens[_pos + 2].Type is not (TokenType.ColonEqual or TokenType.ColonColonEqual)) return;
        throw ParseError("构造器不再用 init 修饰符，直接写 `init = () => { ... }`");
    }

    private void RejectRetiredModifier()
    {
        if (!Check(TokenType.Identifier)) return;
        if (_pos + 1 >= tokens.Count || tokens[_pos + 1].Type != TokenType.Identifier) return;
        switch (Peek().Lexeme)
        {
            case "override" or "new":
                throw ParseError($"'{Peek().Lexeme}' 修饰符已删除（它只被记下来，没有任何地方读它）");
            case "forInstance":
                throw ParseError("'forInstance' 修饰符已删除（类有「自己的」和「给实例的」两张表，住哪张表就是声明）");
        }
    }

    private List<string> CollectStatementModifiers()
    {
        var attrs = new List<string>();
        while (Check(TokenType.Identifier))
        {
            var kw = Peek().Lexeme;
            if (!IsMod(kw)) break;
            // 下个是定义符 → 它是名字，不是修饰符
            if (IsDefinitionOp(NextType())) break;
            _pos++;
            attrs.Add(kw);
        }
        return attrs;
    }

    // 修饰符、解构、换槽、定义都在这一段
    private Statement? TryParseModifiedStatement()
    {
        var attrs = CollectStatementModifiers();

        if (LooksLikePatternDestructure())
            return ParseDestructure(attrs);

        if (attrs.Count == 0) return null;

        // `by a = X` 换槽；只有单独一个 by 走这条
        if (attrs.Count == 1 && attrs.Contains(Attr.By) && IsSlotAssignStart())
            return ParseSlotAssign();

        // 取槽是表达式，语句开头没地方落脚
        if (attrs.Contains(Attr.By) && Check(TokenType.Identifier) && CheckNext(TokenType.Dot))
            throw ParseError("语句开头只有换/建槽的写法（`by a.x = …` / `by a.x := …`）；取槽的 `by a.x` 是个表达式，得放在能用值的地方（比如 `p := by a.x`）");

        // `readonly x := 1` 或 `readonly := 1`
        if ((Check(TokenType.Identifier) && IsDefinitionOp(NextType())) || IsDefinitionOp(Peek().Type))
            return ParseDefinition(attrs);
        throw ParseError("修饰符后需要 ':='（`by a = …` 那种换槽的写法只在 by 后面有）");
    }

    private Statement? TryParseControlFlowStatement()
    {
        if (!_moreControlFlow) return null;
        if (TryParseReturnStatement() is { } ret) return ret;
        if (TryParseBreakContinueStatement() is { } loop) return loop;
        if (Check(TokenType.At)) return ParseLabeledStatement();
        return null;
    }

    // return 是上下文关键字，只在语句开头认
    private Statement? TryParseReturnStatement()
    {
        if (!Check(TokenType.Identifier) || Peek().Lexeme != "return") return null;
        if (LooksLikeUseOfTheName()) return null;
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

    private Statement? TryParseBreakContinueStatement()
    {
        if (!Check(TokenType.Identifier)) return null;
        if (Peek().Lexeme is not ("break" or "continue")) return null;
        if (LooksLikeUseOfTheName()) return null;
        return ParseBreakContinue();
    }

    // '@名字' 给语句起名；前缀换过，':' 已归类型判断
    private Statement ParseLabeledStatement()
    {
        _pos++;
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

    private Statement? TryParseDefinitionOrAssign()
    {
        if (Check(TokenType.Identifier) && NextType() == TokenType.Colon)
        {
            // ':' 也可能只是类型判断，后面跟 '=' 才是定义
            var afterType = SkipTypeAnnotation(2);
            if (afterType >= 0 && TypeAt(afterType) == TokenType.Equal)
                return ParseDefinition();
        }
        else if (Check(TokenType.Identifier) && IsDefinitionOp(NextType()))
        {
            return ParseDefinition();
        }

        if (Check(TokenType.Identifier) && CheckNext(TokenType.Equal))
            return ParseAssignment();

        return null;
    }

    private Statement ParseExpressionStatement()
    {
        _holeCount = 0;
        var startTok = _pos;
        var expr = ParseExpression();

        if (expr is BinaryExpr { Op: ":=", Left: not (IdentifierExpr or MemberAccess) } constraint)
            return ParseConstraintDefinition(constraint, startTok);

        RejectNonLValueAssign(expr);

        // 紧贴着刚算完的表达式看，不跨换行
        if (Check(TokenType.PlusPlus) || Check(TokenType.MinusMinus))
            return ParseIncDec(expr);

        SkipNewlines();
        if (HasHoles(expr)) expr = DesugarHoles(expr);
        return new ExpressionStatement(expr) { Line = expr.Line, Column = expr.Column };
    }

    // `a == 1 = e`：左边是条件，绑给条件里最左的名字
    private Statement ParseConstraintDefinition(BinaryExpr constraint, int startTok)
    {
        var nm = FirstIdentName(startTok, _pos)
            ?? throw ParseError("'=' 左边不是左值 —— 那它得是条**条件**，而条件里要有个名字");
        var pat = new NamePattern(nm)
            { Guard = constraint.Left, Line = constraint.Left.Line, Column = constraint.Left.Column };
        return new Destructure([], pat, constraint.Right)
            { Line = constraint.Line, Column = constraint.Column };
    }

    // 左边不是左值时，早报比等求值期报好
    private void RejectNonLValueAssign(Expression expr)
    {
        if (expr is BinaryExpr { Op: "=" or ":=" or "+=" or "-=" or "*=" or "**=" or "/=" or "%=" } bad
            && bad.Left is not (IdentifierExpr or MemberAccess or IndexExpr))
            throw new SyntaxException(
                $"'{bad.Op}' 的左边得是变量、成员或下标",
                new SourceSpot(source, bad.Line, bad.Column));
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

        // 不报错的话 `a--b` 会被静默读成两条语句
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

        // '(' 只是分组，里面仍是模式
        var off = SkipBalanced(0);
        if (off < 0) return false;
        if (TypeAt(off) == TokenType.ColonEqual) return true;
        // 括号后跟 '=' 是解构赋值：名字必须已存在
        if (TypeAt(off) == TokenType.Equal) return true;

        // `[a b] |> f := e`：'|>' 后挂整块条件
        if (TypeAt(off) == TokenType.PipeInto)
        {
            var depth = 0;
            for (var i = off + 1; ; i++)
            {
                var t = TypeAt(i);
                if (t is TokenType.EndOfFile or TokenType.Newline) return false;
                if (t is TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace) depth++;
                else if (t is TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace) depth--;
                else if (depth == 0 && t is TokenType.ColonEqual) return true;
            }
        }

        if (TypeAt(off) != TokenType.Colon) return false;
        var after = SkipTypeAnnotation(off + 1);
        return after >= 0 && TypeAt(after) is TokenType.Equal or TokenType.ColonEqual;
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

    // by 在这儿是建槽：修饰符摊到每个名字
    private Statement ParseDestructure(List<string> attrs)
    {
        var at = Peek();

        var pattern = ParsePattern();
        RejectBareSub(pattern);

        var typed = pattern.Type is not null;
        var assign = false;
        if (Match(TokenType.ColonEqual))
        {
            // 定义
        }
        else if (Match(TokenType.Equal))
        {
            // 不带注解的 '=' 是解构赋值
            assign = !typed;
        }
        else
        {
            throw ParseError("解构需要 ':=' 或 '='（`[x y] := e` 是定义、`[x y] = e` 是赋值；`[x y] : T = e` 是带注解的定义）");
        }

        _holeCount = 0;
        var value = ParseExpression();
        SkipNewlines();
        if (HasHoles(value)) value = DesugarHoles(value);

        if (assign && attrs.Count > 0)
            throw new SyntaxException($"解构赋值里不能加修饰符 '{attrs[0]}' —— 修饰符只属于定义（要定义就写 ':='）",
                new SourceSpot(source, at.Line, at.Column));

        return new Destructure(attrs, pattern, value) { Line = at.Line, Column = at.Column, Assign = assign };
    }

    // 这些是这一格收尾或下一格开头
    private bool EndsThisPart()
        => Check(TokenType.Colon) || Check(TokenType.Identifier) || Check(TokenType.DotDot)
        || Check(TokenType.LeftBracket) || Check(TokenType.LeftBrace)
        || Check(TokenType.RightBracket) || Check(TokenType.RightBrace)
        || Check(TokenType.RightParen)             // 分组收尾
        || Check(TokenType.ColonEqual)             // `p := 子模式`
        || Check(TokenType.Equal)                  // `{新名 = 成员名}`
        || Check(TokenType.PipeInto)               // '|>' 归 ParsePattern
        || Check(TokenType.Number) || Check(TokenType.String)   // 字面量的下一格
        || Check(TokenType.LeftParen)              // `()` 是下一格的分组
        || Check(TokenType.Newline) || IsAtEnd();

    private bool IsLiteralStart()
        => Check(TokenType.Number) || Check(TokenType.String) || Check(TokenType.Char)
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
            TokenType.String => "\"" + raw + "\"",     // 词法存的是解码后的值
            TokenType.Char => "'" + raw + "'",
            _ => (neg ? "-" : "") + raw,
        };
        var domain = kind switch
        {
            TokenType.String => "string",
            TokenType.Char => "char",
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
        // 每一格也能写修饰符，和外层叠加
        var attrs = CollectModifiers();

        var p = ParsePatternBare();
        if (attrs is not null) p = p with { Attrs = attrs };
        if (Match(TokenType.Colon))
            p = p with { Type = ParseTypeAnnotation(), IgnoreType = _ignoreTypes };
        else if (_ignoreTypes)
            p = p with { IgnoreType = true };
        // `|>` 贴刚结束的那一格
        if (Match(TokenType.PipeInto))
            p = p with { When = ParseClimb(Postfix.Bare, 0) };

        // 值和拆法都要：约束本身说明这是定义
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

        if (Match(TokenType.LeftBracket)) return ParseListPattern(at);
        if (Match(TokenType.LeftBrace)) return ParseBracePattern(at);
        if (Match(TokenType.DotDot)) return ParseRestPattern(at);
        // 字面量那一格：要求等于它
        if (IsLiteralStart() || Check(TokenType.Minus)) return ParseLiteralPattern();
        if (Match(TokenType.LeftParen)) return ParseGroupPattern();

        return ParseNamePattern(at);
    }

    private Pattern ParseListPattern(Token at)
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
        // `[]` 是零格：要求它是空的
        EnsureRestIsLast(parts);
        return new ListPattern(parts) { Line = at.Line, Column = at.Column };
    }

    // 成员名和字典项不许混，两套取法
    private Pattern ParseBracePattern(Token at)
    {
        var names = new List<NamePattern>();
        var entries = new List<DictPatEntry>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            if (LooksLikeDictEntry())
            {
                if (names.Count > 0)
                    throw ParseError("对象成员（`{x y}`）和字典项（`{键 -> 模式}`）别混在一个 `{…}` 里 —— 底下是两套取法，分开写");
                entries.Add(ParseDictEntry(at));
            }
            else
            {
                if (entries.Count > 0)
                    throw ParseError("字典项（`{键 -> 模式}`）和对象成员（`{x y}`）别混在一个 `{…}` 里 —— 底下是两套取法，分开写");
                names.Add(ParseMemberEntry());
            }
            SkipNewlines();
        }
        Consume(TokenType.RightBrace, "模式末尾需要 '}'");
        if (entries.Count > 0) return new DictPattern(entries) { Line = at.Line, Column = at.Column };
        if (names.Count == 0) throw ParseError("模式里得写点什么（`{x y}` 或 `{键 -> 模式}`）");
        return new MemberPattern(names) { Line = at.Line, Column = at.Column };
    }

    private DictPatEntry ParseDictEntry(Token at)
    {
        var key = ParseDictKey();
        Consume(TokenType.DictArrow, "字典项的键后面需要 '->'");
        var sub = ParsePattern();
        RejectBareSub(sub);
        return new DictPatEntry(key, sub) { Line = at.Line, Column = at.Column };
    }

    private NamePattern ParseMemberEntry()
    {
        if (Check(TokenType.Identifier) && Peek().Lexeme == "_")
            throw ParseError("对象模式里没有 '_' 这一格 —— 不想要的成员别写出来就行");
        if (ParsePattern() is not NamePattern n)
            throw ParseError("对象模式里每一项是一个成员名（`{x y}`，能带类型 `{n: int}`），"
                           + "要么整项写成字典项（`{键 -> 模式}`）");

        if (n.Sub is null) return n;

        // `{新名 = 成员名}`：右边是成员名，不是子模式
        // 右边取成员不算声明，挂修饰符会静默丢掉
        if (n.Sub is not NamePattern { Sub: null, Member: null, Type: null, Guard: null, When: null } sel
            || sel.Attrs is not null)
            throw ParseError("对象模式里 `=` 后面写**成员名**（`{新名 = 成员名}`）—— "
                           + "要拆那一项的值,把整项写成列表/字典那边那种模式");
        return n with { Member = sel.Name, Sub = null };
    }

    private Pattern ParseRestPattern(Token at)
    {
        var n = Consume(TokenType.Identifier, "'..' 后面要跟个名字（`..rest` —— 名字自己起）");
        // '..' 后要真名字，'_' 是占位记号
        if (n.Lexeme == "_")
            throw ParseError("'..' 后面要一个真名字 —— `_` 是丢弃/占位的记号，当不了名字");
        return new RestPattern(n.Lexeme) { Line = at.Line, Column = at.Column };
    }

    // 括号只是分组，连套几层折成一层
    private Pattern ParseGroupPattern()
    {
        var open = Previous();
        var inner = ParsePattern();
        Consume(TokenType.RightParen, "模式括号没闭上");
        return inner is WholePattern ? inner : new WholePattern(inner) { Line = open.Line, Column = open.Column };
    }

    private Pattern ParseNamePattern(Token at)
    {
        var saveAt = _pos;
        var name = Consume(TokenType.Identifier, "模式里要写一个名字、`_`、`..名字`、还是一个字面量（`1` / \"看\" / `()` / `true`）");

        // 名字后紧跟 '[' 或 '.' 就是位置
        if (name.Lexeme != "_" && AdjacentToPrevious()
            && (Check(TokenType.LeftBracket) || Check(TokenType.Dot)))
            return ParsePlacePattern(at, saveAt);

        if (name.Lexeme == "_")
        {
            // '_' 什么也不绑，挂不了东西
            if (Check(TokenType.Colon) || Check(TokenType.PipeInto) || !EndsThisPart())
                throw ParseError("`_` 不绑东西，挂不了 `: 类型` 也挂不了 `|> 条件` —— 想验那一格就给它一个名字");
            return new SkipPattern { Line = at.Line, Column = at.Column };
        }

        if (EndsThisPart())
            return new NamePattern(name.Lexeme) { Line = at.Line, Column = at.Column };

        return ParseGuardedNamePattern(at, saveAt);
    }

    private Pattern ParsePlacePattern(Token at, int saveAt)
    {
        _pos = saveAt;
        var place = ParseClimb(Postfix.Bare, 0);
        if (place is IndexExpr or MemberAccess)
            return new PlacePattern(place) { Line = at.Line, Column = at.Column };
        throw ParseError("这一格要么是名字、字面量，要么是一个位置（`l[2]` / `a.b`）");
    }

    // 带条件的一格：取条件里最左的名字
    private Pattern ParseGuardedNamePattern(Token at, int saveAt)
    {
        _pos = saveAt;
        var from = _pos;
        var guard = ParseClimb(Postfix.Bare, 0);
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

    // 运算符符号本身就是成员名
    private string ParseMemberName()
    {
        if (Check(TokenType.Identifier) || IsOperatorToken(Peek().Type))
        {
            _pos++;
            return Previous().Lexeme;
        }

        throw ParseError("'.' 后需要成员名");
    }

    // ':' 不给定义，它是内建判断
    private Statement ParseOperatorDefinition()
    {
        var sym = Peek();
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
        if (Check(TokenType.LeftParen)) return ParsePrimary();   // (任意表达式)

        var t = Consume(TokenType.Identifier, "注解需要一个类型名，或者括号里的表达式");
        Expression e = new IdentifierExpr(t.Lexeme) { Line = t.Line, Column = t.Column };
        while (Match(TokenType.Dot))
        {
            var m = Consume(TokenType.Identifier, "'.' 后需要成员名");
            e = new MemberAccess(e, m.Lexeme) { Line = t.Line, Column = t.Column };
        }

        return e;
    }

    // 槽里填的是属性那一格，所以给一对空 getter/setter
    private static CallExpr EmptyProperty(Expression at)
        => new CallExpr(
               new IdentifierExpr("property") { Line = at.Line, Column = at.Column },
               new LiteralExpr(VoidVal.Instance) { Line = at.Line, Column = at.Column })
           { Line = at.Line, Column = at.Column };

    private Statement ParseDefinition(List<string>? attrs = null)
    {
        var (name, line, col) = ParseDefinedName(attrs);

        if (ReservedWords.Contains(name))
            throw ParseError($"'{name}' 是保留字");

        var (typeAnnotation, autoName) = ParseDefinitionSign();
        var value = ParseDefinitionValue(attrs, typeAnnotation);

        if (name == "_")
            return new ExpressionStatement(value) { Line = line, Column = col };

        return new VarDefinition(name, typeAnnotation, value, attrs, autoName)
        {
            IgnoreType = _ignoreTypes,
            Line = line,
            Column = col,
        };
    }

    private (string Name, int Line, int Column) ParseDefinedName(List<string>? attrs)
    {
        if (Check(TokenType.Identifier))
        {
            var n = Consume(TokenType.Identifier, "需要变量名");
            return (n.Lexeme, n.Line, n.Column);
        }
        // 修饰符本身当名字（`readonly := …`）
        if (attrs is { Count: > 0 })
            return (attrs[0], Previous().Line, Previous().Column);
        throw ParseError("需要变量名");
    }

    private (Expression? Type, bool AutoName) ParseDefinitionSign()
    {
        if (Match(TokenType.ColonColonEqual)) return (null, true);
        if (Match(TokenType.ColonColon))
        {
            var type = ParseTypeAnnotation();
            RequireAfterTypeAnnotation();
            return (type, true);
        }
        if (Match(TokenType.ColonEqual)) return (null, false);
        if (Match(TokenType.Colon))
        {
            var type = ParseTypeAnnotation();
            RequireAfterTypeAnnotation();
            return (type, false);
        }
        throw ParseError("变量定义需要 ':='、'::='、': type =' 或 ':: type ='");
    }

    private void RequireAfterTypeAnnotation()
    {
        if (!Match(TokenType.ColonEqual) && !Match(TokenType.Equal))
            throw ParseError("类型注解后需要 '=' 或 ':='");
    }

    private Expression ParseDefinitionValue(List<string>? attrs, Expression? typeAnnotation)
    {
        _holeCount = 0;
        var value = ParseExpression();
        SkipNewlines();
        // 定义处的 '_' 在 by 那格是 property ()
        if (value is HoleExpr && attrs?.Contains(Attr.By) == true)
            value = EmptyProperty(value);
        // 带注解的 `x: T = _` 交给 lowering
        var bare = value is HoleExpr && typeAnnotation != null && attrs?.Contains(Attr.By) != true;
        if (!bare && HasHoles(value)) value = DesugarHoles(value);
        SkipNewlines();
        return value;
    }

    private bool IsSlotAssignStart()
    {
        // 运算符也能当槽名
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
        // 运算符当成员名时不能走 ParsePrimary
        Expression path;
        if (IsOperatorToken(Peek().Type))
        {
            var sym = Peek();
            _pos++;
            path = new IdentifierExpr(sym.Lexeme) { Line = sym.Line, Column = sym.Column };
        }
        else
        {
            path = ParseMemberChain(ParsePrimary(), Postfix.Full);
        }
        var define = Match(TokenType.ColonEqual);
        if (!define) Consume(TokenType.Equal, "需要 '=' 或 ':='");
        _holeCount = 0;
        var value = ParseExpression();
        SkipNewlines();
        // 槽里填的是属性那一格
        if (value is HoleExpr) value = EmptyProperty(value);
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
