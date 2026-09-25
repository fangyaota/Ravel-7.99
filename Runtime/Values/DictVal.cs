namespace Ravel.Runtime;

/// <summary>非原子值 —— 有**自己的成员表**,见 <see cref="ObjectVal"/>。
/// 形状和 <see cref="ListVal"/> 一样:`members` 喂基类,不占数据字段。</summary>
public record DictVal : ObjectVal
{
    public Dictionary<string, RuntimeValue> Entries { get; init; }

    public DictVal(Dictionary<string, RuntimeValue> entries, Scope? members = null)
        : base(BuiltinClasses.Dict, members ?? new Scope())
        => Entries = entries;

    public override string ToString() => ShowDepth.Guard(() =>
    {
        var pairs = new List<string>();
        foreach (var kv in Entries) pairs.Add(kv.Key + ": " + kv.Value);
        return "{" + string.Join(" ", pairs) + "}";
    });
}
