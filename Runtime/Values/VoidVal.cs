namespace Ravel.Runtime;

public record VoidVal : RuntimeValue
{
    public static readonly VoidVal Instance = new();
    public override RuntimeType Type => RuntimeType.Void;
    public override string ToString() => "()";
    private VoidVal() { }
}
