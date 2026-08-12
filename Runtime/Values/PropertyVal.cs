namespace Ravel.Runtime;

/// <summary>Property = getter + setter 对</summary>
public record PropertyVal(FunctionVal Getter, FunctionVal Setter, List<string>? Attrs = null) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.Property;
    public override string ToString() => "<property>";
}
