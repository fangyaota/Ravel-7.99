namespace Ravel;

using Ravel.Runtime;

public partial class Parser
{
    // ========================================
    //  前瞻判定
    // ========================================

    // 只看 token,不造节点;判错就在主函数里立刻回头
    private bool StartsParamGroup(int off)
        => TypeAt(off) is TokenType.LeftBracket or TokenType.LeftBrace;

    private bool StartsParam()
        => Check(TokenType.Identifier) || StartsParamGroup(0) || IsLiteralStart()
        || Check(TokenType.LeftParen);

    // 名字/字面量/模式括号/负号/..rest 都能起一格
    private bool StartsBindingAt(int off) => TypeAt(off) switch
    {
        TokenType.Identifier or TokenType.Number or TokenType.String
            or TokenType.LeftBracket or TokenType.LeftBrace or TokenType.LeftParen
            or TokenType.Minus or TokenType.DotDot => true,
        _ => false,
    };

    private List<string>? CollectModifiers()
    {
        var attrs = new List<string>();
        while (Check(TokenType.Identifier) && Attr.All.Contains(Peek().Lexeme))
        {
            var save = _pos;
            var word = Peek().Lexeme;
            _pos++;                       // 先吃,再看后面有没有绑定项
            SkipNewlines();
            if (!StartsBindingAt(0)) { _pos = save; break; }
            attrs.Add(word);
        }
        return attrs.Count > 0 ? attrs : null;
    }

    private bool LooksLikeLambdaParams()
    {
        var off = 0;
        while (true)
        {
            if (StartsParamGroup(off))
            {
                // 模式参数算一项,后面不能跟注解
                off = SkipBalanced(off);
                if (off < 0) return false;               // 括号没闭上
            }
            else if (TypeAt(off) == TokenType.Identifier)
            {
                off++;
                if (TypeAt(off) == TokenType.Colon)
                {
                    off = SkipTypeAnnotation(off + 1);
                    if (off < 0) return false;           // 类型读不动
                }
            }
            else return false;

            if (TypeAt(off) == TokenType.RightParen) return TypeAt(off + 1) == TokenType.Arrow;
            // 之后只可能是下一个参数
        }
    }

    private bool IsLambdaAfterParen()
    {
        var off = 0;
        var depth = 1;      // 从 1 起:否则分组会认成参数表
        while (true)
        {
            var t = TypeAt(off);
            if (t == TokenType.EndOfFile) return false;
            if (t == TokenType.LeftParen) depth++;
            else if (t == TokenType.RightParen && --depth == 0) return TypeAt(off + 1) == TokenType.Arrow;
            off++;
        }
    }

    private int SkipTypeAnnotation(int off)
    {
        if (TypeAt(off) == TokenType.LeftParen)
        {
            var depth = 0;
            while (true)
            {
                if (TypeAt(off) == TokenType.EndOfFile) return -1;      // 括号没闭上
                if (TypeAt(off) == TokenType.LeftParen) depth++;
                else if (TypeAt(off) == TokenType.RightParen && --depth == 0) return off + 1;
                off++;
            }
        }

        if (TypeAt(off) != TokenType.Identifier) return -1;
        off++;
        while (TypeAt(off) == TokenType.Dot && TypeAt(off + 1) == TokenType.Identifier) off += 2;
        return off;
    }

    private (bool Arrow, bool Newline) ScanBraceBody()
    {
        int depth = 1;
        bool arrow = false, newline = false, statement = false;
        for (int i = _pos; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.EndOfFile) break;
            switch (t.Type)
            {
                case TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace:
                    depth++;
                    break;
                case TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace:
                    if (--depth == 0) return (arrow && !statement, newline);
                    break;
                case TokenType.Newline:
                    newline = true;
                    // 词法器把 `;` 也发成 Newline(lexeme 不同):顶层有语句分隔,
                    // 这对比字典就是一段块 —— `{1 -> 1;}` 是"块,值是一对",不是字典。
                    if (depth == 1 && t.Lexeme == ";") statement = true;
                    break;
                case TokenType.DictArrow when depth == 1:
                    arrow = true;
                    break;
            }
        }

        return (arrow, newline);
    }

    private bool BarePatternLambdaAhead(int off = 0)
    {
        switch (TypeAt(off))
        {
            case TokenType.Number or TokenType.String or TokenType.Char:
                return TypeAt(off + 1) == TokenType.Arrow;
            case TokenType.Minus:                                  // 负数字面量
                return TypeAt(off + 1) == TokenType.Number && TypeAt(off + 2) == TokenType.Arrow;
            case TokenType.Identifier:
                return tokens[_pos + off].Lexeme is "true" or "false" && TypeAt(off + 1) == TokenType.Arrow;
            case TokenType.LeftBracket or TokenType.LeftBrace:
            {
                var end = SkipBalanced(off);
                return end >= 0 && TypeAt(end) == TokenType.Arrow;
            }
            default:
                return false;
        }
    }

    // 取最左名字:按树会取到被调者
    private string? FirstIdentName(int from, int to)
    {
        for (var i = from; i < to && i < tokens.Count; i++)
            if (tokens[i].Type == TokenType.Identifier)
                return tokens[i].Lexeme;
        return null;
    }

    private bool AnyNewlineBeforeClose(TokenType closing)
    {
        int depth = 1;
        for (int i = _pos; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Type == TokenType.Newline) return true;
            if (t.Type == TokenType.LeftBrace) depth++;
            if (t.Type == closing)
            {
                depth--;
                if (depth == 0) return false;
            }

            if (t.Type == TokenType.EndOfFile) return false;
        }

        return false;
    }

    private bool HasDotDotAhead()
    {
        var depth = 0;
        for (var i = _pos; i < tokens.Count; i++)
        {
            switch (tokens[i].Type)
            {
                case TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace:
                    depth++;
                    break;
                case TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace:
                    if (depth == 0) return false;      // 这一层到头,没 ..
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
}
