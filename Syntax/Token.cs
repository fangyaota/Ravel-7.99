namespace Ravel;

public class Token(TokenType type, string lexeme, int line, int column, int length)
{
    public TokenType Type { get; } = type;
    public string Lexeme { get; } = lexeme;
    public int Line { get; } = line;
    public int Column { get; } = column;

    public int Length { get; } = length;

    public IReadOnlyList<StringPart>? Parts { get; init; }

    // 同一个 token 换个位置/跨度 —— f-string 前缀那个 `f` 在引号**前面**,
    // 报错时要指到那儿,不是引号那一列。
    public Token At(int line, int column, int length)
        => new(Type, Lexeme, line, column, length) { Parts = Parts };

    public override string ToString() => $"{Type}({Lexeme}) at {Line}:{Column}";
}

public record StringPart(string Text, bool IsExpr, int Line, int Column);
