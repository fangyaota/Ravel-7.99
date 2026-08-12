namespace Ravel;

public class Lexer
{
    private readonly string _source;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    // Ravel 无关键字 — while/if 等是普通标识符，在运行时作为内置函数处理
    private static readonly Dictionary<string, TokenType> Keywords = [];

    public Lexer(string source)
    {
        _source = source;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (_pos < _source.Length)
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
                while (_pos < _source.Length && _source[_pos] == '\n')
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
                while (_pos < _source.Length && _source[_pos] != '\n')
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

            if (single is TokenType tt)
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

            throw new Exception($"未预期的字符 '{c}'，位置 {_line}:{_col}");
        }

        tokens.Add(new Token(TokenType.EndOfFile, "", _line, _col));
        return tokens;
    }

    // ==================== helpers ====================

    private bool TryMatch(string s, TokenType type, List<Token> tokens)
    {
        if (_pos + s.Length > _source.Length) return false;
        for (int i = 0; i < s.Length; i++)
            if (_source[_pos + i] != s[i])
                return false;

        int line = _line, col = _col;
        for (int i = 0; i < s.Length; i++) Advance();
        tokens.Add(new Token(type, s, line, col));
        return true;
    }

    private char Peek() => _source[_pos];

    private void Advance()
    {
        _pos++;
        _col++;
    }

    private Token ReadNumber()
    {
        int start = _pos, line = _line, col = _col;
        while (_pos < _source.Length && char.IsDigit(_source[_pos]))
            _pos++;
        if (_pos < _source.Length && _source[_pos] == '.' && _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1]))
        {
            _pos++;
            while (_pos < _source.Length && char.IsDigit(_source[_pos]))
                _pos++;
        }
        string num = _source[start.._pos];
        _col += (_pos - start);
        return new Token(TokenType.Number, num, line, col);
    }

    private Token ReadString()
    {
        int line = _line, col = _col;
        Advance(); // skip "
        int start = _pos;
        while (_pos < _source.Length && _source[_pos] != '"')
        {
            if (_source[_pos] == '\n') { _line++; _col = 1; }
            _pos++;
        }
        string str = _source[start.._pos];
        if (_pos < _source.Length) Advance(); // skip closing "
        _col += (_pos - start) + 2;
        return new Token(TokenType.String, str, line, col);
    }

    private Token ReadIdentifier()
    {
        int start = _pos, line = _line, col = _col;
        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_'))
            _pos++;
        string word = _source[start.._pos];
        if (word == "operator" && _pos < _source.Length)
        {
            char nc = _source[_pos];
            if (nc == '+' || nc == '-' || nc == '*' || nc == '/' || nc == '%')
            {
                _pos++; _col++; word += nc;
            }
            else if (nc == '=' && _pos + 1 < _source.Length && _source[_pos + 1] == '=')
            {
                _pos += 2; _col += 2; word += "==";
            }
            else if (nc == '!' && _pos + 1 < _source.Length && _source[_pos + 1] == '=')
            {
                _pos += 2; _col += 2; word += "!=";
            }
            else if (nc == '<' && _pos + 1 < _source.Length && _source[_pos + 1] == '=')
            {
                _pos += 2; _col += 2; word += "<=";
            }
            else if (nc == '>' && _pos + 1 < _source.Length && _source[_pos + 1] == '=')
            {
                _pos += 2; _col += 2; word += ">=";
            }
            else if (nc == '<' || nc == '>')
            {
                _pos++; _col++; word += nc;
            }
            else if (nc == '&' || nc == '|' || nc == '^')
            {
                _pos++; _col++; word += nc;
            }
        }
        // operator+ → 合并为一个标识符
        if (word == "operator" && _pos < _source.Length)
        {
            char nc = _source[_pos];
            if (nc == '+' || nc == '-' || nc == '*' || nc == '/' || nc == '%')
            {
                Advance(); word += nc;
            }
            else if (nc == '=' && _pos + 1 < _source.Length && _source[_pos + 1] == '=')
            {
                Advance(); Advance(); word += "==";
            }
            else if (nc == '!' && _pos + 1 < _source.Length && _source[_pos + 1] == '=')
            {
                Advance(); Advance(); word += "!=";
            }
            else if (nc == '<' && _pos + 1 < _source.Length && _source[_pos + 1] == '=')
            {
                Advance(); Advance(); word += "<=";
            }
            else if (nc == '>' && _pos + 1 < _source.Length && _source[_pos + 1] == '=')
            {
                Advance(); Advance(); word += ">=";
            }
            else if (nc == '<' || nc == '>')
            {
                Advance(); word += nc;
            }
            else if (nc == '&' || nc == '|' || nc == '^')
            {
                Advance(); word += nc;
            }
        }
        _col += (_pos - start);

        TokenType type = Keywords.TryGetValue(word, out var kw) ? kw : TokenType.Identifier;
        return new Token(type, word, line, col);
    }
}
