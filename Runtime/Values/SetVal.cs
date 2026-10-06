namespace Ravel.Runtime;

/// <summary>非原子值 —— 有**自己的成员表**,见 <see cref="ObjectVal"/>。
/// 形状和 <see cref="ListVal"/> 一样:`members` 喂基类,不占数据字段。</summary>
public record SetVal : ObjectVal
{
    public HashSet<RuntimeValue> Elements { get; init; }

    public SetVal(HashSet<RuntimeValue> elements, Scope? members = null)
        : base(BuiltinClasses.Set, members)
        => Elements = elements;

    public override string ToString() => ShowDepth.Guard(() => "{" + string.Join(" ", Elements) + "}");
}
