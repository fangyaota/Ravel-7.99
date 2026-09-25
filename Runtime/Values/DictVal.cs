namespace Ravel.Runtime;

public record DictVal(Dictionary<string, RuntimeValue> Entries) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Dict;
    public override string ToString() => ShowDepth.Guard(() =>
    {
        var pairs = new List<string>();
        foreach (var kv in Entries) pairs.Add(kv.Key + ": " + kv.Value);
        return "{" + string.Join(" ", pairs) + "}";
    });
}
