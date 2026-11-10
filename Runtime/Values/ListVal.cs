namespace Ravel.Runtime;

public record ListVal : ObjectVal
{
    public List<RuntimeValue> Elements { get; init; }

    public ListVal(List<RuntimeValue> elements, Scope? members = null)
        : base(BuiltinClasses.List, members)
        => Elements = elements;

    public override string ToString() => ShowDepth.Guard(() => "[" + string.Join(" ", Elements) + "]");
}
