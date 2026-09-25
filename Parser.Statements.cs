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
    /// <summary>词形运算符:`is` / `isnot` 不是标点,只能按词认。
    /// **只在运算符位置认**(中缀、节首),所以它们同时还能当普通标识符/成员名用 ——
    /// `1.is` 要能走成员访问那条路。</summary>
    private bool IsWordOperator(Token t) => t.Type == TokenType.Identifier && t.Lexeme is "is" or "isnot";

    /// <summary>吃掉一个词形运算符(是的话)。</summary>
    private bool MatchWordOperator()
    {
        if (!IsWordOperator(Peek())) return false;
        _pos++;
        return true;
    }

    /// <summary>运算符节的开头:`+.2` / `is.int` —— 运算符(标点或词形)后面紧跟 `.`。
    /// 节是"左操作数留空"的写法,`ParsePrimary` 认它。</summary>
    private bool IsSectionStart()
        => (IsOperatorToken(Peek().Type) || IsWordOperator(Peek()))
           && _pos + 1 < tokens.Count && tokens[_pos + 1].Type == TokenType.Dot;

    /// <summary>这个位置上的词形运算符**是中缀**(`x is int`),不是节的开头(`is.int`)。
    /// 相邻调用的参数扫描要在这儿停 —— 否则 `1 is int` 会被吃成 `1(is, int)`。</summary>
    private bool IsInfixWordOperator() => IsWordOperator(Peek()) && !IsSectionStart();

    private static bool IsOperatorToken(TokenType t) => t switch
    {
        TokenType.Plus or TokenType.Minus or TokenType.Star or TokenType.Slash or TokenType.Percent
            or TokenType.EqualEqual or TokenType.NotEqual
            or TokenType.Less or TokenType.Greater or TokenType.LessEqual or TokenType.GreaterEqual
            or TokenType.And or TokenType.Pipe or TokenType.Caret => true,
        _ => false,
    };

    /// <summary>类机制内部词——禁止作为变量名（this/init 是类内可用变量，不禁）</summary>
    private static readonly HashSet<string> ReservedWords = ["thistype", "block", "core"];

    private Statement ParseStatement()
    {
        // 运算符定义:`+ := f`(定义) / `+ = f`(覆盖)。符号本身就是成员名。
        if (IsOperatorToken(Peek().Type) && _pos + 1 < tokens.Count)
        {
            var nt = tokens[_pos + 1].Type;
            if (nt is TokenType.ColonEqual or TokenType.Equal)
                return ParseOperatorDefinition();
        }

        // 旧写法 operator+ add := ... 已废弃。不拦的话会被当表达式 `operator + add` 求值,
        // 报的却是「未定义的变量 'operator'」,看不出所以然。
        if (Check(TokenType.Identifier) && Peek().Lexeme == "operator" &&
            _pos + 1 < tokens.Count && IsOperatorToken(tokens[_pos + 1].Type))
            throw ParseError("运算符定义已改成直接用符号：`+ := f`（定义）或 `+ = f`（覆盖）");

        // 旧写法 `init ctor := ...` 同理:构造器现在只是名字叫 init 的变量,
        // 不拦下来就会去求值表达式 `init ctor`,报「未定义的变量 'init'」。
        if (Check(TokenType.Identifier) && Peek().Lexeme == "init" &&
            _pos + 1 < tokens.Count && tokens[_pos + 1].Type == TokenType.Identifier &&
            _pos + 2 < tokens.Count && tokens[_pos + 2].Type is TokenType.ColonEqual or TokenType.ColonColonEqual)
            throw ParseError("构造器不再用 init 修饰符，直接写 `init := () => { ... }`");

        // 修饰符
        var attrs = new List<string>();
        while (true)
        {
            if (Check(TokenType.Identifier))
            {
                var kw = Peek().Lexeme;
                bool isMod = IsMod(kw);
                if (!isMod) break;
                // 下一个 token 是定义符 → 当前是名字，不是修饰符
                if (_pos + 1 < tokens.Count)
                {
                    var nt = tokens[_pos + 1].Type;
                    if (nt == TokenType.ColonEqual || nt == TokenType.ColonColonEqual || nt == TokenType.ColonColon ||
                        nt == TokenType.Colon)
                        break;
                }

                _pos++;
                attrs.Add(kw);
            }
            else break;
        }

        if (attrs.Count > 0)
        {
            if (Check(TokenType.Identifier) && (CheckNext(TokenType.ColonEqual) || CheckNext(TokenType.Colon) ||
                                                CheckNext(TokenType.ColonColonEqual) ||
                                                CheckNext(TokenType.ColonColon)))
                return ParseDefinition(attrs);
            if (Check(TokenType.ColonEqual) || Check(TokenType.Colon) || Check(TokenType.ColonColonEqual) ||
                Check(TokenType.ColonColon))
                return ParseDefinition(attrs);
            throw ParseError("修饰符后需要 ':='");
        }

        // 普通定义：IDENT := expr 或 IDENT : type = expr
        // 普通定义：IDENT := expr 或 IDENT : type = expr
        if (Check(TokenType.Identifier) &&
            (CheckNext(TokenType.ColonEqual) || CheckNext(TokenType.Colon) ||
             CheckNext(TokenType.ColonColonEqual) || CheckNext(TokenType.ColonColon)))
            return ParseDefinition();
        // IDENT = expr → 赋值
        if (Check(TokenType.Identifier) && CheckNext(TokenType.Equal))
            return ParseAssignment();

        // 独立的表达式语句
        _holeCount = 0;
        var expr = ParseExpression();
        SkipNewlines();
        if (HasHoles(expr)) expr = DesugarHoles(expr);
        return new ExpressionStatement(expr) { Line = expr.Line, Column = expr.Column };
    }

    /// <summary>`.` 后面那一段:普通成员名,或运算符符号(`2.+` / `"a".==`)</summary>
    private string ParseMemberName()
    {
        if (Check(TokenType.Identifier))
        {
            var t = Peek();
            _pos++;
            return t.Lexeme;
        }

        if (IsOperatorToken(Peek().Type))
        {
            var t = Peek();
            _pos++;
            return t.Lexeme;
        }

        throw ParseError("'.' 后需要成员名");
    }

    /// <summary>`+ := f` 定义运算符;`+ = f` 覆盖已有的(父类层定义的)那个。符号即成员名。</summary>
    private Statement ParseOperatorDefinition()
    {
        var sym = Peek();
        _pos++;
        if (Match(TokenType.ColonEqual))
            return new VarDefinition(sym.Lexeme, null, ParseExpression()) { Line = sym.Line, Column = sym.Column };

        Match(TokenType.Equal);
        return new Assignment(sym.Lexeme, ParseExpression()) { Line = sym.Line, Column = sym.Column };
    }

    /// <summary>name := expr  |  name: Type = expr</summary>
    private Statement ParseDefinition(List<string>? attrs = null)
    {
        string name;
        int line, col;
        if (attrs is { Count: > 0 })
        {
            if (Check(TokenType.Identifier))
            {
                var n = Consume(TokenType.Identifier, "需要变量名");
                name = n.Lexeme;
                line = n.Line;
                col = n.Column;
            }
            else
            {
                // 修饰符本身当名字用(如 `readonly := ...`)
                name = attrs[0];
                line = Previous().Line;
                col = Previous().Column;
            }
        }
        else
        {
            var nameToken = Consume(TokenType.Identifier, "需要变量名");
            name = nameToken.Lexeme;
            line = nameToken.Line;
            col = nameToken.Column;
        }

        string? typeAnnotation = null;
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
            var typeToken = Consume(TokenType.Identifier, "'::' 后需要类型名");
            typeAnnotation = typeToken.Lexeme;
            Consume(TokenType.Equal, "类型注解后需要 '='");
        }
        else if (Match(TokenType.ColonEqual))
        {
            // := → 类型推断
        }
        else if (Match(TokenType.Colon))
        {
            var typeToken = Consume(TokenType.Identifier, "':' 后需要类型名");
            typeAnnotation = typeToken.Lexeme;
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

        var result = new VarDefinition(name, typeAnnotation, value, attrs, autoName)
        {
            Line = line,
            Column = col,
        };
        if (autoName)
            return result; // 标记由 EvalStmt 处理
        return result;
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
