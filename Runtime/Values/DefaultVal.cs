namespace Ravel.Runtime;

public record DefaultVal : RuntimeValue
{
    public static readonly DefaultVal Instance = new();
    public override RuntimeType Type => RuntimeType.Every;
    public override string ToString() => "default";
    private DefaultVal() { }
}
