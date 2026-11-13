namespace Ravel.Runtime;

public record DictVal : ObjectVal
{
    public Dictionary<RuntimeValue, RuntimeValue> Entries { get; init; }

    public DictVal(Dictionary<RuntimeValue, RuntimeValue> entries, Scope? members = null)
        : base(BuiltinClasses.Dict, members)
        => Entries = new Dictionary<RuntimeValue, RuntimeValue>(entries, SameComparer.Instance);

    public override string ToString() => ShowDepth.Guard(() =>
    {
        // 空的印 `{->}` —— 和源码里那个写法对齐:`{}` 现在是空集合,印成 `{}` 会骗人。
        if (Entries.Count == 0) return "{->}";
        var pairs = new List<string>();
        foreach (var kv in Entries) pairs.Add(kv.Key + ": " + kv.Value);
        return "{" + string.Join(" ", pairs) + "}";
    });
}
