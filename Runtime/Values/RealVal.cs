namespace Ravel.Runtime;

public record RealVal(double Value) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Real;
    public override string ToString() => Value switch
    {
        double.NaN => "NaN",
        double.PositiveInfinity => "Inf",
        double.NegativeInfinity => "-Inf",
        _ => Value.ToString("G"),
    };
}
