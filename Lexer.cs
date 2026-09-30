namespace Ravel;

using Ravel.Runtime;

/// <summary>词法分析。file 只用来给报错标位置(见 SyntaxException),不参与切词。</summary>
public class Lexer(string source, string? file = null)
{
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    /// <summary>还没闭合的 `(` / `[` / `{` —— 只为了下面那条:**圆括号/方括号里的换行不算数**。
    ///
    /// 规则就一句:"**没闭合的 `(` / `[` 里,换行不当语句结束**"。于是表和长参数列表能写好看:
    ///
    ///     match v [
    ///         [is.int    {"整数";}]
    ///         [is.string {"字符串";}]
    ///     ] {"别的";}
    ///
    /// 花括号**不在这条规则里**(它是块,靠换行分语句 —— `{ x }` 是集合、`{ a: 1 }` 是字典、
    /// 多行才是块,那条判据要看得见换行)。所以只有最里层是圆/方括号时才把换行吞掉;
    /// 进了花括号照旧发。</summary>
    private readonly List<TokenType> _groups = [];

    /// <summary>这个换行/分号当前算不算数:在圆括号或方括号里就不算(见 <see cref="_groups"/>)。
    /// 花括号里算 —— 块靠它分语句。</summary>
    private bool NewlineCounts()
        => _groups.Count == 0 || _groups[^1] == TokenType.LeftBrace;

    /// <summary>括号进出栈 —— 只记 `(` / `[` / `{` 三种,给 <see cref="NewlineCounts"/> 用。</summary>
    private void TrackGroup(TokenType t)
    {
        if (t is TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace)
            _groups.Add(t);
        else if (t is TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace
                 && _groups.Count > 0)
            _groups.RemoveAt(_groups.Count - 1);   // 对不上的闭合(本来就是语法错)就当没这层
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_pos < source.Length)
        {
            char c = Peek();

            // 空白（空格、tab 等，但不包括换行）
            if (char.IsWhiteSpace(c) && c != '\n')
            {
                Advance();
                continue;
            }

            // 换行 → Newline token(连着几个换行合成一个,所以它的跨度是那几个字符)
            if (c == '\n')
            {
                int startLine = _line;
                while (_pos < source.Length && source[_pos] == '\n')
                {
                    Advance();
                    _line++;
                    _col = 1;
                }

                if (NewlineCounts())
                    tokens.Add(new Token(TokenType.Newline, "\\n", _line - 1, 1, _line - startLine));
                continue;
            }

            // 分号 → 视作换行
            if (c == ';')
            {
                int line = _line, col = _col;
                Advance();
                if (NewlineCounts())
                    tokens.Add(new Token(TokenType.Newline, ";", line, col, 1));
                continue;
            }

            // 注释 # 到行尾
            if (c == '#')
            {
                while (_pos < source.Length && source[_pos] != '\n')
                    _pos++;
                _col++;
                continue;
            }

            // ========== 多字符运算符 ==========

            // `:<` / `:>` 得排在 `:` 前面(单字符那批在下面)
            if (TryMatch(":<", TokenType.BindArrow, tokens)) continue;
            if (TryMatch(":>", TokenType.Supertype, tokens)) continue;
            if (TryMatch("<:", TokenType.Subtype, tokens)) continue;
            if (TryMatch(":=", TokenType.ColonEqual, tokens)) continue;
            if (TryMatch("::=", TokenType.ColonColonEqual, tokens)) continue;
            if (TryMatch("::", TokenType.ColonColon, tokens)) continue;
            if (TryMatch("=>", TokenType.Arrow, tokens)) continue;
            if (TryMatch("==", TokenType.EqualEqual, tokens)) continue;
            if (TryMatch("!=", TokenType.NotEqual, tokens)) continue;
            if (TryMatch("<=", TokenType.LessEqual, tokens)) continue;
            if (TryMatch("<|", TokenType.PipeLeft, tokens)) continue;
            if (TryMatch(">=", TokenType.GreaterEqual, tokens)) continue;
            if (TryMatch("&&", TokenType.AndAnd, tokens)) continue;
            if (TryMatch("||", TokenType.OrOr, tokens)) continue;
            if (TryMatch("+=", TokenType.PlusEqual, tokens)) continue;
            if (TryMatch("-=", TokenType.MinusEqual, tokens)) continue;
            if (TryMatch("*=", TokenType.StarEqual, tokens)) continue;
            if (TryMatch("/=", TokenType.SlashEqual, tokens)) continue;
            if (TryMatch("%=", TokenType.PercentEqual, tokens)) continue;

            // ========== 单字符 ==========

            TokenType? single = c switch
            {
                ':' => TokenType.Colon,
                '=' => TokenType.Equal,
                '<' => TokenType.Less,
                '>' => TokenType.Greater,
                '+' => TokenType.Plus,
                '-' => TokenType.Minus,
                '*' => TokenType.Star,
                '/' => TokenType.Slash,
                '%' => TokenType.Percent,
                '&' => TokenType.And,
                '^' => TokenType.Caret,
                '!' => TokenType.Bang,
                '(' => TokenType.LeftParen,
                ')' => TokenType.RightParen,
                '[' => TokenType.LeftBracket,
                ']' => TokenType.RightBracket,
                '{' => TokenType.LeftBrace,
                '}' => TokenType.RightBrace,
                ',' => TokenType.Comma,
                '.' => TokenType.Dot,
                '|' => TokenType.Pipe,
                '@' => TokenType.At,
                '$' => TokenType.Dollar,
                _ => null,
            };

            if (single is { } tt)
            {
                int line = _line, col = _col;
                Advance();
                tokens.Add(new Token(tt, c.ToString(), line, col, 1));
                TrackGroup(tt);
                continue;
            }

            // 数字
            if (char.IsDigit(c))
            {
                tokens.Add(ReadNumber());
                continue;
            }

            // 字符串
            if (c == '"')
            {
                tokens.Add(ReadString());
                continue;
            }

            // 字符
            if (c == '\'')
            {
                tokens.Add(ReadChar());
                continue;
            }

            // 标识符 / 关键字
            if (char.IsLetter(c) || c == '_')
            {
                tokens.Add(ReadIdentifier());
                continue;
            }

            throw new SyntaxException($"未预期的字符 '{c}'", new SourceSpot(file, _line, _col));
        }

        tokens.Add(new Token(TokenType.EndOfFile, "", _line, _col, 0));
        return tokens;
    }

    /// <summary>扫一遍源码,回答「括号合上了吗、末尾在不在字符串里」。REPL 用它判断
    /// 这一行能不能交给求值器(见 `NeoInteractor.IsBalanced`)。
    ///
    /// **规则必须和词法器一致** —— 从前 REPL 自己抄了一份,不认 `\` 转义,于是
    /// `"a\"b"` 会把字符串状态判反、后面整行的括号都跟着数错。
    ///
    /// 深度只做加减不做校验(`)` 也能让深度回到 0):这样多一个右括号照样会交给解析器去
    /// 报错,而不是卡在"还没写完"上。</summary>
    public static (int Depth, bool InString) ScanState(string src)
    {
        int depth = 0;
        bool inString = false;
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];
            if (inString)
            {
                if (c == '\\') i++;                 // 转义:下一个字符不参与判断
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '#')                            // 注释到行尾
            {
                while (i < src.Length && src[i] != '\n') i++;
                continue;
            }

            if (c == '"') inString = true;
            else if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
        }

        return (depth, inString);
    }

    // ==================== helpers ====================

    private bool TryMatch(string s, TokenType type, List<Token> tokens)
    {
        if (_pos + s.Length > source.Length) return false;
        for (int i = 0; i < s.Length; i++)
            if (source[_pos + i] != s[i])
                return false;

        int line = _line, col = _col;
        for (int i = 0; i < s.Length; i++) Advance();
        tokens.Add(new Token(type, s, line, col, s.Length));
        return true;
    }

    private char Peek() => source[_pos];

    private void Advance()
    {
        _pos++;
        _col++;
    }

    private Token ReadNumber()
    {
        int start = _pos, line = _line, col = _col;
        while (_pos < source.Length && char.IsDigit(source[_pos]))
            _pos++;
        if (_pos < source.Length && source[_pos] == '.' && _pos + 1 < source.Length && char.IsDigit(source[_pos + 1]))
        {
            _pos++;
            while (_pos < source.Length && char.IsDigit(source[_pos]))
                _pos++;
        }

        string num = source[start.._pos];
        _col += (_pos - start);
        return new Token(TokenType.Number, num, line, col, _pos - start);
    }

    private Token ReadString()
    {
        int start = _pos, line = _line, col = _col;
        Advance(); // skip "
        var sb = new System.Text.StringBuilder();
        List<StringPart>? parts = null;                 // 有插值才建
        int litLine = _line, litCol = _col;             // 当前**字面量段**的起点

        void FlushLiteral()
        {
            if (parts == null) return;
            parts.Add(new StringPart(sb.ToString(), IsExpr: false, litLine, litCol));
            sb.Clear();
        }

        while (_pos < source.Length && source[_pos] != '"')
        {
            char c = source[_pos];

            // 插值:`${表达式}` —— **只有这一种写法**(要任意表达式,索性只有一种更好记)。
            // `$` 后面不是 `{` 就是**字面量**(`"$5"` / `"$name"` 都不用转义);
            // 真要 `${` 本身,写 `\${`。
            if (c == '$' && _pos + 1 < source.Length && source[_pos + 1] == '{')
            {
                parts ??= [];
                FlushLiteral();
                _pos += 2;
                _col += 2;

                int exprLine = _line, exprCol = _col;
                var text = ReadBracedInterpolation(line, col);
                parts.Add(new StringPart(text, IsExpr: true, exprLine, exprCol));
                litLine = _line;
                litCol = _col;
                continue;
            }

            if (c == '\\' && _pos + 1 < source.Length)
            {
                // 转义。以前只找下一个引号,于是字符串里根本放不进 `"`——
                // `"say \"hi\""` 会被当成两个串,再报「未预期的字符」。
                _pos++;
                _col++;
                var esc = source[_pos];
                sb.Append(esc switch
                {
                    'n' => "\n",
                    't' => "\t",
                    'r' => "\r",
                    '\\' => "\\",
                    '"' => "\"",
                    '$' => "$",          // 插值记号本身
                    // 不认识的转义原样保留,免得把已有文本里的反斜杠吃掉
                    _ => "\\" + esc,
                });
            }
            else
            {
                if (c == '\n') { _line++; _col = 1; }
                else _col++;
                sb.Append(c);
            }

            _pos++;
        }

        // 走到源码末尾还没见着收尾的引号 —— **报错**,别把剩下的全吞成一个字符串。
        // 从前是静默接受:`print "abc` 会把换行和后面的代码一起吞进去、打印出 abc,
        // 少打一个引号的人看不出自己错了。
        if (_pos >= source.Length)
            throw new SyntaxException("字符串没有收尾的 '\"'", new SourceSpot(file, line, col));

        Advance(); // skip closing "
        FlushLiteral();                                  // 收尾那段字面量(没有插值时不做事)
        // 跨度是**源码**跨度(含两边引号),不是解码后内容的长度 —— 两者差着转义
        return new Token(TokenType.String, sb.ToString(), line, col, _pos - start) { Parts = parts };
    }

    /// <summary>`${ … }` 里那一段源码:扫到**配对的** `}`,路上跳过一个字符串字面量
    /// (不然 `"${f "x"}"` 会被里面的引号提前截断)和嵌套的括号。</summary>
    private string ReadBracedInterpolation(int strLine, int strCol)
    {
        int start = _pos, depth = 0;
        while (_pos < source.Length)
        {
            char c = source[_pos];
            if (c == '"')                                // 跳过一个字符串字面量
            {
                _pos++; _col++;
                while (_pos < source.Length && source[_pos] != '"')
                {
                    if (source[_pos] == '\\') { _pos++; _col++; }
                    _pos++; _col++;
                }
                if (_pos >= source.Length) break;
                _pos++; _col++;
                continue;
            }

            if (c == '{') depth++;
            else if (c == '}' && depth == 0)
            {
                var text = source[start.._pos];
                _pos++; _col++;
                return text;
            }
            else if (c == '}') depth--;

            if (c == '\n') { _line++; _col = 1; } else _col++;
            _pos++;
        }

        throw new SyntaxException("插值没有收尾的 '}'", new SourceSpot(file, strLine, strCol));
    }

    /// <summary>`'a'` —— 一个**字符**字面量。转义和字符串那套一样,外加 `\'`。
    ///
    /// 长度不是 1 就当场报错:`'ab'` 是写错了,不是"两个字符的 char"。
    /// (UTF-16 码元口径:emoji 那种代理对**一个 `'…'` 装不下**,得用字符串 —— 和
    /// `"😀".Length` 是 2 一个道理,见 `CharVal`。)</summary>
    private Token ReadChar()
    {
        int start = _pos, line = _line, col = _col;
        Advance(); // skip '

        if (_pos >= source.Length || source[_pos] == '\n')
            throw new SyntaxException("字符没有收尾的 \"'\"", new SourceSpot(file, line, col));
        if (source[_pos] == '\'')
            throw new SyntaxException("字符不能是空的（空字符串写 \"\"）", new SourceSpot(file, line, col));

        char value;
        if (source[_pos] == '\\' && _pos + 1 < source.Length)
        {
            _pos++;
            _col++;
            var esc = source[_pos];
            if (esc is not ('n' or 't' or 'r' or '\\' or '\'' or '"' or '0'))
                throw new SyntaxException($"不认识的字符转义 '\\{esc}'（要反斜杠本身请写 '\\\\'）",
                    new SourceSpot(file, line, col));
            value = esc switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '\'' => '\'',
                '"' => '"',
                _ => '\0',
            };
        }
        else
        {
            value = source[_pos];
        }

        Advance();
        if (_pos >= source.Length || source[_pos] != '\'')
            throw new SyntaxException("字符字面量只能有一个字符（两个以上请用字符串）",
                new SourceSpot(file, line, col));
        Advance(); // skip closing '
        return new Token(TokenType.Char, value.ToString(), line, col, _pos - start);
    }

    private Token ReadIdentifier()
    {
        int start = _pos, line = _line, col = _col;
        while (_pos < source.Length && (char.IsLetterOrDigit(source[_pos]) || source[_pos] == '_'))
            _pos++;
        string word = source[start.._pos];

        _col += (_pos - start);

        return new Token(TokenType.Identifier, word, line, col, _pos - start);
    }
}
