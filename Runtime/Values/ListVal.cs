namespace Ravel.Runtime;

public record ListVal(List<RuntimeValue> Elements) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.List;
    public override string ToString() => ShowDepth.Guard(() => "[" + string.Join(" ", Elements) + "]");
}
