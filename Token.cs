namespace Ravel;

public class Token(TokenType type, string lexeme, int line, int column)
{
    public TokenType Type { get; } = type;
    public string Lexeme { get; } = lexeme;
    public int Line { get; } = line;
    public int Column { get; } = column;

    public override string ToString() => $"{Type}({Lexeme}) at {Line}:{Column}";
}
