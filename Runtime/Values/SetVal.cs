namespace Ravel.Runtime;

public record SetVal : ObjectVal
{
    public HashSet<RuntimeValue> Elements { get; init; }

    public SetVal(HashSet<RuntimeValue> elements, Scope? members = null)
        : base(BuiltinClasses.Set, members)
        => Elements = new HashSet<RuntimeValue>(elements, SameComparer.Instance);

    public override string ToString() => ShowDepth.Guard(() => "{" + string.Join(" ", Elements) + "}");
}
