namespace Ravel.Runtime;

public record RealVal(double Value) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Real;
    /// <summary>NaN 与正负无穷**不走 `double.ToString`**:它的无穷是**文化相关**的
    /// (`∞` / `-∞`),而值的形式是数据,该是 ASCII(见 `tests/153`)。NaN 顺便也定死。</summary>
    public override string ToString() => Value switch
    {
        double.NaN => "NaN",
        double.PositiveInfinity => "Inf",
        double.NegativeInfinity => "-Inf",
        _ => Value.ToString("G"),
    };
}
