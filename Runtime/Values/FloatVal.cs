namespace Ravel.Runtime;

public record FloatVal(double Value) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Float;
    public override string ToString() => Value.ToString("G");
}
