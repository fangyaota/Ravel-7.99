namespace Ravel;

public class Token(TokenType type, string lexeme, int line, int column, int length)
{
    public TokenType Type { get; } = type;
    public string Lexeme { get; } = lexeme;
    public int Line { get; } = line;
    public int Column { get; } = column;

    public int Length { get; } = length;

    public IReadOnlyList<StringPart>? Parts { get; init; }

    public override string ToString() => $"{Type}({Lexeme}) at {Line}:{Column}";
}

public record StringPart(string Text, bool IsExpr, int Line, int Column);
