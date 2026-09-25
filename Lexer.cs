namespace Ravel;

using Ravel.Runtime;

/// <summary>词法分析。file 只用来给报错标位置(见 SyntaxException),不参与切词。</summary>
public class Lexer(string source, string? file = null)
{
    private int _pos;
    private int _line = 1;
    private int _col = 1;

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

            // 换行 → Newline token
            if (c == '\n')
            {
                while (_pos < source.Length && source[_pos] == '\n')
                {
                    Advance();
                    _line++;
                    _col = 1;
                }

                tokens.Add(new Token(TokenType.Newline, "\\n", _line - 1, 1));
                continue;
            }

            // 分号 → 视作换行
            if (c == ';')
            {
                int line = _line, col = _col;
                Advance();
                tokens.Add(new Token(TokenType.Newline, ";", line, col));
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
                _ => null,
            };

            if (single is { } tt)
            {
                int line = _line, col = _col;
                Advance();
                tokens.Add(new Token(tt, c.ToString(), line, col));
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

            // 标识符 / 关键字
            if (char.IsLetter(c) || c == '_')
            {
                tokens.Add(ReadIdentifier());
                continue;
            }

            throw new SyntaxException($"未预期的字符 '{c}'", new SourceSpot(file, _line, _col));
        }

        tokens.Add(new Token(TokenType.EndOfFile, "", _line, _col));
        return tokens;
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
        tokens.Add(new Token(type, s, line, col));
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
        return new Token(TokenType.Number, num, line, col);
    }

    private Token ReadString()
    {
        int line = _line, col = _col;
        Advance(); // skip "
        var sb = new System.Text.StringBuilder();
        while (_pos < source.Length && source[_pos] != '"')
        {
            char c = source[_pos];
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

        if (_pos < source.Length) Advance(); // skip closing "
        return new Token(TokenType.String, sb.ToString(), line, col);
    }

    private Token ReadIdentifier()
    {
        int start = _pos, line = _line, col = _col;
        while (_pos < source.Length && (char.IsLetterOrDigit(source[_pos]) || source[_pos] == '_'))
            _pos++;
        string word = source[start.._pos];

        _col += (_pos - start);

        return new Token(TokenType.Identifier, word, line, col);
    }
}
