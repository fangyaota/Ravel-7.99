namespace Ravel.Runtime;

/// <summary>按**键**排序的字典:键永远有序,`Keys ()` / `Values ()` 就是排好的,
/// 找第一个/最后一个也是 O(log n)。
///
/// 用 .NET 的 <c>SortedDictionary</c>(红黑树),比较器是 Ravel 自己的 `<`(见 `Less`)——
/// 于是**键得能互相比大小**:比不了当场报错,而不是给个乱序的表。相等也按 `<` 算,
/// 所以 `1` 与 `1.0` 在这儿是同一个键(和 `dict` 那边按类型分不一样,写进文档了)。</summary>
public record SortedDictVal : ObjectVal
{
    public SortedDictionary<RuntimeValue, RuntimeValue> Entries { get; init; }

    public SortedDictVal(SortedDictionary<RuntimeValue, RuntimeValue> entries, Scope? members = null)
        : base(BuiltinClasses.ClassOf("SortedDict"), members ?? new Scope())
        => Entries = entries;

    public override string ToString() => ShowDepth.Guard(() =>
    {
        var pairs = new List<string>();
        foreach (var kv in Entries) pairs.Add(kv.Key + ": " + kv.Value);
        return "SortedDict {" + string.Join(" ", pairs) + "}";
    });
}
