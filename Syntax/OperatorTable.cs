namespace Ravel;

public sealed record OpInfo(
    string Symbol,
    TokenType? Token,   // null = 词形运算符
    int Lbp,
    int Rbp,
    int PrintPrec,
    bool RightAssoc);

public static class Operators
{
    // 一元前缀那一档(打印器判括号也用)
    public const int UnaryPrec = 17;

    private static readonly Dictionary<TokenType, OpInfo> ByToken = new();
    private static readonly Dictionary<string, OpInfo> ByWord = new();
    private static readonly Dictionary<string, OpInfo> BySymbol = new();

    static Operators()
    {
        Add("||", 2, TokenType.OrOr);
        Add("|", 4, TokenType.Pipe);
        Add("&", 6, TokenType.And);
        Add("^", 6, TokenType.Caret);
        Add("&&", 8, TokenType.AndAnd);
        Add("and", 8);                       // 与 `&&` 同档
        Add("or", 2);                        // 与 `||` 同档
        Add(":", 10, TokenType.Colon);
        // `->` 造一对(见 `pair`):右结合,所以 `a -> b -> c` 是 `a -> (b -> c)`。
        // 档位取在比较/判定(10)与移位(12)之间:它是**造一个值**,不是谓词 ——
        // 落在 10 那档的话 `1 -> 2 : pair` 会被读成 `1 -> (2 : pair)`(右边那格被判类型),
        // 11 才得到想要的 `(1 -> 2) : pair`;同时 `1 + 2 -> 3` 仍是 `3 -> 3`(`+` 14 更紧)。
        // 字典字面量里那个 `->` 不是它 —— 那里是条目分隔符,键按 `Postfix.DictKey` 解析。
        Add("->", 11, TokenType.DictArrow, right: true);
        Add("==", 10, TokenType.EqualEqual);
        // `===` 与 `==` 同档(能串):比的是**同一性**——值类型按值、对象按引用,不做跨族数值归一
        Add("===", 10, TokenType.EqualEqualEqual);
        Add("!==", 10, TokenType.NotEqualEqual);
        Add("!=", 10, TokenType.NotEqual);
        Add("<", 10, TokenType.Less);
        Add(">", 10, TokenType.Greater);
        Add("<=", 10, TokenType.LessEqual);
        Add(">=", 10, TokenType.GreaterEqual);
        Add("<:", 10, TokenType.Subtype);
        Add(":>", 10, TokenType.Supertype);
        Add("<<", 12, TokenType.ShiftLeft);
        Add(">>", 12, TokenType.ShiftRight);
        Add("<<<", 12, TokenType.RotateLeft);
        Add(">>>", 12, TokenType.RotateRight);
        Add("+", 14, TokenType.Plus);
        Add("-", 14, TokenType.Minus);
        Add("*", 16, TokenType.Star);
        Add("/", 16, TokenType.Slash);
        Add("%", 16, TokenType.Percent);
        Add("**", 18, TokenType.StarStar, right: true);
    }

    private static void Add(string symbol, int lbp, TokenType? token = null, bool right = false)
    {
        var op = new OpInfo(symbol, token, lbp, right ? lbp - 1 : lbp + 1, lbp, right);
        BySymbol[symbol] = op;
        if (token is { } t) ByToken[t] = op;
        else ByWord[symbol] = op;
    }

    public static bool TrySymbol(string symbol, out OpInfo op)
        => BySymbol.TryGetValue(symbol, out op!);

    public static bool TryToken(TokenType t, out OpInfo op)
        => ByToken.TryGetValue(t, out op!);

    public static bool TryWord(string lexeme, out OpInfo op)
        => ByWord.TryGetValue(lexeme, out op!);

    // 打印器判括号用;不认识的符号按最低档
    public static int PrintPrec(string symbol)
        => BySymbol.TryGetValue(symbol, out var op) ? op.PrintPrec : 0;

    public static bool IsCompare(string symbol)
        => symbol is "<" or ">" or "<=" or ">=" or "==" or "!=" or "===" or "!==" or ":" or "<:" or ":>";

    public static bool IsInfixToken(TokenType t) => ByToken.ContainsKey(t);

    public static bool IsPowerLike(TokenType t)
        => ByToken.TryGetValue(t, out var op) && op.RightAssoc && op.PrintPrec > UnaryPrec;
}
