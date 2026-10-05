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

    /// <summary>扫过的**注释区间**(起始下标, 长度)—— 只有 `CommentStripper` 看它。
    ///
    /// 放这儿是因为"哪儿是注释"只有这台词法器说了算:字符串(`"a # b"`)、原始字符串
    /// (`"""…"""`,一个字符都不动)、字符字面量(`'#'`)里的 `#` 都不是注释。
    /// 外面再拿正则扫一遍,必然和这套分法分岔(从前 REPL 自己抄一遍括号扫描,
    /// 踩过同一个坑 —— 见 `lib/repl.rav` 里 `Balanced` 那段)。</summary>
    public List<(int Start, int Length)> Comments { get; } = [];

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
                int start = _pos;
                while (_pos < source.Length && source[_pos] != '\n')
                    _pos++;
                Comments.Add((start, _pos - start));
                _col++;
                continue;
            }

            // ========== 多字符运算符 ==========
            if (MatchMultiChar(tokens)) continue;

            // ========== 单字符 ==========

            TokenType? single = c switch
            {
                ':' => TokenType.Colon,
                '=' => TokenType.Equal,
                '<' => TokenType.Less,
                '>' => TokenType.Greater,
                '@' => TokenType.At,          // 标签
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

            // 原始字符串 `"""…"""`。**要排在普通字符串前面** ——
            // 不然开头那两下先被当成空串 `""` 吃掉,后面剩个孤零零的引号。
            if (c == '"' && _pos + 2 < source.Length && source[_pos + 1] == '"' && source[_pos + 2] == '"')
            {
                tokens.Add(ReadRawString());
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
            if (IsIdentStart(c))
            {
                tokens.Add(ReadIdentifier());
                continue;
            }


            // `$` 落到这儿 = 写到**字符串外面**来了。它的地盘只剩插值那一处,
            // 所以直说清楚,顺带把"想封右边该写什么"一并告诉他 —— 比"未预期的字符"有用。
            if (c == '$')
                throw new SyntaxException("'$' 只用在字符串里的插值 `${…}`（要把右边封成一个实参,用 '<|'）",
                    new SourceSpot(file, _line, _col));

            // 单个 `?` 落到这儿说明后面没跟 `.` / `?`。这套语言没有三目,
            // 直说它只在三条里出现,比"未预期的字符"有用。
            if (c == '?')
                throw new SyntaxException("'?' 只用在 '?.' / '??' / '??=' 里（没有三目运算符）",
                    new SourceSpot(file, _line, _col));

            throw new SyntaxException($"未预期的字符 '{c}'", new SourceSpot(file, _line, _col));
        }

        // **行首是二元运算符 → 那个换行不算数**(上一句自己接下去)。
        //
        // 为什么在这一层、而不是解析器里:换行一旦发出去,表达式就收了尾 —— 再想让
        // "上一句"接着长,得回退重读一整条语句(那正是这门语言最怕的那种翻倍)。
        // 在**发 token 之前**抽掉,下游一个字都不用改。
        //
        // 只管**真换行**:`;` 是**明写的**分隔符,它后面写运算符是另一回事(照旧报错)。
        // 括号里的换行本来就不算数(见 `NewlineCounts`),走不到这儿。
        //
        // 从后往前走:抽掉一格不会打乱还没看的那几格的编号。
        // **运算符定义不算**:`< := f` 那种,打头就是运算符,可那是它在**定义**
        // (见 `ParseOperatorDefinition` 的四种收尾:`:=` / `::=` / `: 注解 =` / `:: 注解 =`)。
        // 判据往后多看一枚 —— 运算符后面紧跟定义符/注解符的,就是那种,不能接上一行。
        for (var i = tokens.Count - 1; i > 0; i--)
            if (tokens[i - 1].Type == TokenType.Newline
                && tokens[i - 1].Lexeme != ";"
                && Parser.IsInfixOperator(tokens[i])
                && (i + 1 >= tokens.Count
                    || tokens[i + 1].Type is not (TokenType.ColonEqual or TokenType.ColonColonEqual
                                                or TokenType.Equal or TokenType.Colon)))
                tokens.RemoveAt(i - 1);

        tokens.Add(new Token(TokenType.EndOfFile, "", _line, _col, 0));
        return tokens;
    }

    // ==================== helpers ====================

    /// <summary>多字符运算符:**文本 → token**,按顺序试第一个命中的。
    ///
    /// **顺序有讲究**(三条注释都写在表里了):前缀相同的,长的得排在短的前面
    /// (`??=` 先于 `??`、`::=` 先于 `::`、`:<` 先于 `:` 那张单字符表)。
    /// 表驱动是因为这些条目本来就只是"一段文本 + 一个 token":从前它们是 25 行
    /// `if (TryMatch(…) continue;`,加一个得记得插在对的位置上、还得顺手改注释。
    ///
    /// 首字符对不上就直接跳过(所以二十来次比对只在真撞上时才发生)。</summary>
    private static readonly (string Text, TokenType Type)[] MultiCharOps =
    [
        // `:<` / `:>` 得排在 `:` 前面(单字符那批在另一个表里)
        (":<", TokenType.BindArrow),
        (":>", TokenType.Supertype),
        ("<:", TokenType.Subtype),
        (":=", TokenType.ColonEqual),
        ("::=", TokenType.ColonColonEqual),
        ("::", TokenType.ColonColon),
        ("=>", TokenType.Arrow),
        ("==", TokenType.EqualEqual),
        ("!=", TokenType.NotEqual),
        // 三个尖的排在两个尖的前面(`<<<` 别被读成 `<<` + `<`)
        ("<<<", TokenType.RotateLeft),
        (">>>", TokenType.RotateRight),
        ("<<", TokenType.ShiftLeft),
        (">>", TokenType.ShiftRight),
        ("<=", TokenType.LessEqual),
        ("<|", TokenType.PipeLeft),
        ("|>", TokenType.PipeInto),      // 和 `<|` 是一对:那个封右,这个封左
        (">=", TokenType.GreaterEqual),
        ("&&", TokenType.AndAnd),
        ("||", TokenType.OrOr),
        // `++` / `--` —— 语句级的糖(见 Parser.Statements 的 ParseIncDec)。
        // **代价写在文档里**:`a - -b` 不写空格就成了 `a--` `b`,所以连着写两个负号时
        // 中间那个空格不能省(C 也是一样)。
        // `->` 字典的键值分隔符。它和 `--` 不前缀冲突(`--` 后面跟的不是 `>`),
        // 但 **`-->` 会读成 `--` + `>`** —— C 也是这个脾气,连着写时留空格。
        ("->", TokenType.DictArrow),
        ("++", TokenType.PlusPlus),
        ("--", TokenType.MinusMinus),
        ("+=", TokenType.PlusEqual),
        ("-=", TokenType.MinusEqual),
        // `**=` 得排在 `**` 前面(前缀相同的,长的先试)—— 和 `??=` 先于 `??` 一个道理
        ("**=", TokenType.StarStarEqual),
        ("**", TokenType.StarStar),
        ("*=", TokenType.StarEqual),
        ("/=", TokenType.SlashEqual),
        ("%=", TokenType.PercentEqual),
        // `?` 这一族(空值那三条):`??=` 排在 `??` 前面,`?.` 自成一对;
        // 单独的 `?` 不是这套语言里的东西(没有三目),见单字符表后面那句
        ("??=", TokenType.CoalesceEqual),
        ("??", TokenType.Coalesce),
        ("?.", TokenType.QuestionDot),
        // 两个点连写 = **区间**的分隔符(`[1..3]` / `(3..5)`)—— 得排在单字符 `.` 前面。
        // 数字那边不受影响:`ReadNumber` 只在 `.` 后面跟数字时才当小数点,
        // 所以 `1..3` 读成 `1` + `..` + `3`,而 `1.5` 还是 float、`a.b` 还是取成员
        ("..", TokenType.DotDot),
    ];

    /// <summary>多字符运算符那一趟:吃掉了就返回 true(调用方 continue)。</summary>
    private bool MatchMultiChar(List<Token> tokens)
    {
        var c = Peek();
        foreach (var (text, type) in MultiCharOps)
            if (text[0] == c && TryMatch(text, type, tokens))
                return true;

        return false;
    }

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

    /// <summary>这个名字的字符**能开一个标识符**吗(标识符的第一个字符 —— 数字不算)。
    /// 认标识符开头那一处用。</summary>
    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    /// <summary>这个名字的字符**能当标识符的一部分**吗(`abc_1` 里每一个都算)。
    /// 数字后缀"后面是不是还粘着个名字"那一问用它 —— 所以 `2n` 算后缀、`2n1` 不算。</summary>
    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

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

        // 类型后缀:`2n` 大整数 / `3i` 普通整数 / `2.5f` 浮点(见 NumberLiteral.Suffix)。
        // **要吞得干净才算**:后面再跟标识符字符就不是后缀(所以 `0if`、`2not x` 照旧读成
        // "数字 + 标识符",一个字都没变);`2n` 后面跟 `)` `.` `[` 空格这些才算数。
        if (_pos < source.Length && source[_pos] is 'n' or 'i' or 'f' &&
            (_pos + 1 >= source.Length || !IsIdentChar(source[_pos + 1])))
            _pos++;

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

    /// <summary>`""" … """` —— **原始字符串:里面一个字符都不动**。
    ///
    /// 反斜杠不是转义、单个引号不是收尾、`#` 不是注释,只有**连着三个引号**才收。
    /// 代价是内容里写不了 `"""` 本身 —— 真要那三个字符就拿普通字符串拼。
    /// **也不插值**:要 `${…}` 请用普通字符串。
    ///
    /// 空白按 C# 那套:**紧跟开引号的那个换行不算内容**(前提是那一行剩下的都是空白),
    /// 再按收尾那三个引号所在行的缩进给每行剥掉同样多 —— 于是块能跟着代码正常缩进,
    /// 内容看起来就跟画的一样。
    ///
    /// 行尾**按 `\n` 归一**:源码里成对的 `\r\n` 在内容里就是一个 `\n`(单独一个 `\r` 是
    /// 内容,留着)。不归一的话,"画什么样是什么样"就成了"看检出时的行尾是什么样"——
    /// 同一份库在 LF / CRLF 两种检出下会给出不同的字符串(从前就是这个毛病:
    /// `lib/docsite.rav` 那份 CSS 每个行尾都多一个 `\r`)。
    ///
    ///     readonly Banner := () => {
    ///         """
    ///          ____
    ///         /\  _`\
    ///         """
    ///     }
    ///     # 收尾那行缩进 4 格 → 每行剥 4 格 → 内容是 ` ____\n/\  _`\`</summary>
    private Token ReadRawString()
    {
        int start = _pos, line = _line, col = _col;
        _pos += 3;
        _col += 3;

        // ① 开引号后面一路到行尾都是空白 → 那段空白连同那个换行都不算内容。
        //    (只有"开引号后面直接换行"那种写法会吃掉一个换行;`"""abc"""` 原封不动)
        //
        //    **`\r\n` 也算一个换行** —— 源码是 CRLF 检出的时候(Windows 上很常见),只认
        //    `\n` 会让这半个 `\r\n` 漏进内容:同一个库,检出时的行尾不同,字符串就不同。
        int probe = _pos;
        while (probe < source.Length && source[probe] is ' ' or '\t') probe++;
        if (probe < source.Length && source[probe] == '\n')
        {
            _pos = probe + 1;
            _line++;
            _col = 1;
        }
        else if (probe + 1 < source.Length && source[probe] == '\r' && source[probe + 1] == '\n')
        {
            _pos = probe + 2;
            _line++;
            _col = 1;
        }

        int contentStart = _pos;

        // ② 收尾:第一个 `"""`。**一个字符都不跳** —— `\` 和 `#` 在这中间都不是记号
        int close = -1;
        for (int i = _pos; i + 2 < source.Length; i++)
        {
            if (source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"')
            {
                close = i;
                break;
            }
        }

        // 走到源码末尾还没见着收尾 —— 和普通字符串那条"没有收尾的 \""一个口径:报错,别硬吞
        if (close < 0)
            throw new SyntaxException("原始字符串没有收尾的 '\"\"\"'", new SourceSpot(file, line, col));

        // ③ 收尾引号**那一行**、它前面的那段空白 = 缩进量。
        //    前面还有别的东西(单行写法)就不算独占一行,也就是没有缩进可剥。
        int lineStart = close == 0 ? 0 : source.LastIndexOf('\n', close - 1) + 1;
        string before = source[lineStart..close];
        bool ownLine = before.All(ch => ch is ' ' or '\t');

        // 内容里的 `\r\n` 一律按 `\n` 算,和上面①同一个理由:原始字符串"画什么样就是什么样"
        // 这条,不该被检出时的行尾搅掉。(只动成对的 `\r\n`;单独一个 `\r` 是内容,留着。)
        string body = source[contentStart..close].Replace("\r\n", "\n");
        string text = StripRawIndent(body, ownLine ? before : "", ownLine, line, col);

        // ④ 游标推到收尾引号之后 —— 从 contentStart 一格一格数过去,`_line` / `_col` 才准
        //    (报错位置、以及后面 token 的行列都吃它)
        _pos = contentStart;
        while (_pos < close + 3)
        {
            if (source[_pos] == '\n') { _line++; _col = 1; } else _col++;
            _pos++;
        }

        // 跨度照旧是**源码**跨度(含两边定界符) —— 行高亮从前靠它(C# 那版 REPL 的 `ReplView`,
        // 已删;`lib/repl.rav` 那半不经过 token,自己扫一遍)
        return new Token(TokenType.String, text, line, col, _pos - start);
    }

    /// <summary>原始字符串那一步的空白处理:收尾引号自己那一行不算内容,再给每行剥掉 `indent`。
    /// 某行剥不动、而它又不是一整个空行 → 当场报错。
    ///
    /// 那条报错是**护栏**,不是洁癖:漏排一行的缩进,结果是悄悄多出几个空格;
    /// 报出来才能立刻看见。(不报的话,一段对齐的文本里混进一行歪的,肉眼极难发现。)</summary>
    private string StripRawIndent(string raw, string indent, bool ownLine, int line, int col)
    {
        // 收尾引号独占一行时,它那一行(就是 `indent` 那段空白)不是内容
        if (ownLine)
        {
            int cut = raw.LastIndexOf('\n');
            raw = cut >= 0 ? raw[..cut] : "";
        }

        if (raw.Length == 0) return "";

        var rows = raw.Split('\n');
        if (indent.Length > 0)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].StartsWith(indent)) rows[i] = rows[i][indent.Length..];
                else if (rows[i].Trim().Length != 0)
                    throw new SyntaxException("原始字符串里这一行的缩进比收尾的 '\"\"\"' 浅",
                        new SourceSpot(file, line, col));
            }
        }

        return string.Join('\n', rows);
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
                // 原始字符串另算:里头的 `"` 不成对也收不住,只有 `"""` 能收
                if (_pos + 2 < source.Length && source[_pos + 1] == '"' && source[_pos + 2] == '"')
                {
                    _pos += 3; _col += 3;
                    while (_pos + 2 < source.Length &&
                           !(source[_pos] == '"' && source[_pos + 1] == '"' && source[_pos + 2] == '"'))
                    {
                        if (source[_pos] == '\n') { _line++; _col = 1; } else _col++;
                        _pos++;
                    }
                    if (_pos + 2 >= source.Length) break;   // 没收到尾:交给词法器去报
                    _pos += 3; _col += 3;
                    continue;
                }

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
        while (_pos < source.Length && IsIdentChar(source[_pos]))
            _pos++;
        string word = source[start.._pos];

        _col += (_pos - start);

        return new Token(TokenType.Identifier, word, line, col, _pos - start);
    }
}
