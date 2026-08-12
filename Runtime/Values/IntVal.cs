namespace Ravel.Runtime;

public record IntVal(int Value) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.Int;
    public override string ToString() => Value.ToString();
}
