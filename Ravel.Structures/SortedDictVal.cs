namespace Ravel.Runtime;

public record SortedDictVal : ObjectVal
{
    public SortedDictionary<RuntimeValue, RuntimeValue> Entries { get; init; }

    public SortedDictVal(SortedDictionary<RuntimeValue, RuntimeValue> entries, Scope? members = null)
        : base(PluginKit.ClassOf("SortedDict"), members ?? new Scope())
        => Entries = entries;

    public override string ToString() => PluginKit.Guard(() =>
    {
        var pairs = new List<string>();
        foreach (var kv in Entries) pairs.Add(kv.Key + ": " + kv.Value);
        return "SortedDict {" + string.Join(" ", pairs) + "}";
    });
}
