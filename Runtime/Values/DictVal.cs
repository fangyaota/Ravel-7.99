namespace Ravel.Runtime;

/// <summary>非原子值 —— 有**自己的成员表**,见 <see cref="ObjectVal"/>。
/// 形状和 <see cref="ListVal"/> 一样:`members` 喂基类,不占数据字段。
///
/// **键是任意值,但"当键用"的只有按值比的那些**(`IValue` 那一支,外加 `Bool` / `Void`):
/// 把关在入口(`BuiltinClasses` 的 `KeyArg`),所以表里装的键永远同步比得了 ——
/// 这里要的是 .NET 的比较器,**够不着**用户写的 Ravel 函数(那得推帧)。
/// 对象要当键,先过 `lib/keys.rav` 那层把它规范成一个值类型(`Keys.Set` / `Keyed`)。
/// 相等的口径就是值自己的(`1` 与 `"1"` 是两个键,`1` 与 `1.0` 也是)。</summary>
public record DictVal : ObjectVal
{
    public Dictionary<RuntimeValue, RuntimeValue> Entries { get; init; }

    public DictVal(Dictionary<RuntimeValue, RuntimeValue> entries, Scope? members = null)
        : base(BuiltinClasses.Dict, members)
        => Entries = entries;

    public override string ToString() => ShowDepth.Guard(() =>
    {
        var pairs = new List<string>();
        foreach (var kv in Entries) pairs.Add(kv.Key + ": " + kv.Value);
        return "{" + string.Join(" ", pairs) + "}";
    });
}
