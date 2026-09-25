namespace Ravel.Runtime;

public record SetVal(HashSet<RuntimeValue> Elements) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Set;
    public override string ToString() => ShowDepth.Guard(() => "{" + string.Join(" ", Elements) + "}");
}
