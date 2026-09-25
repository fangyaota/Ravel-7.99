namespace Ravel.Runtime;

/// <summary>Property = getter + setter 对</summary>
public record PropertyVal(FunctionVal Getter, FunctionVal Setter, List<string>? Attrs = null) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Property;
    public override string ToString() => "<property>";
}
