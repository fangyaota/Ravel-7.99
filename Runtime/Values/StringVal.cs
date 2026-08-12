namespace Ravel.Runtime;

public record StringVal(string Value) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.String;
    public override string ToString() => Value;
}
