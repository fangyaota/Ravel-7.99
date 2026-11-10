namespace Ravel.Runtime;

public record SortedSetVal : ObjectVal
{
    public SortedSet<RuntimeValue> Elements { get; init; }

    public SortedSetVal(SortedSet<RuntimeValue> elements, Scope? members = null)
        : base(PluginKit.ClassOf("SortedSet"), members ?? new Scope())
        => Elements = elements;

    public override string ToString() => PluginKit.Guard(() => "SortedSet {" + string.Join(" ", Elements) + "}");
}
