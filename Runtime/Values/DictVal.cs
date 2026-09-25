namespace Ravel.Runtime;

public record DictVal(Dictionary<string, RuntimeValue> Entries) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.Dict;
    public override string ToString() => ShowDepth.Guard(() =>
    {
        var pairs = new List<string>();
        foreach (var kv in Entries) pairs.Add(kv.Key + ": " + kv.Value);
        return "{" + string.Join(" ", pairs) + "}";
    });
}
