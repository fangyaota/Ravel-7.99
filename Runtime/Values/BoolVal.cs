namespace Ravel.Runtime;

public record BoolVal(bool Value) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.Bool;
    public override string ToString() => Value.ToString().ToLower();
}
