namespace Ravel.Runtime;

public record FloatVal(double Value) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.Float;
    public override string ToString() => Value.ToString("G");
}
