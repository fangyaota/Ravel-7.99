namespace Ravel.Runtime;

/// <summary>按值排序的集合(`SortedSet`),比较器同 <see cref="SortedDictVal"/> ——
/// 里面的东西得能互相比大小。`Min ()` / `Max ()` 是 O(log n),枚举就是升序。</summary>
public record SortedSetVal : ObjectVal
{
    public SortedSet<RuntimeValue> Elements { get; init; }

    public SortedSetVal(SortedSet<RuntimeValue> elements, Scope? members = null)
        : base(BuiltinClasses.ClassOf("SortedSet"), members ?? new Scope())
        => Elements = elements;

    public override string ToString() => ShowDepth.Guard(() => "SortedSet {" + string.Join(" ", Elements) + "}");
}
