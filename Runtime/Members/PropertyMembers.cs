namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

/// <summary>**`Property` 这个类给它的实例准备什么。**
///
/// 只有一条,但要说清楚为什么只有一条:property 的读和写**不是方法**
/// (它是 `Getter` / `Setter` 两格数据,读 `p` 就是调 getter)——
/// 所以 `Property` 这一层没有 `Get` / `Set`,那两条落在 `Object` 上
/// (`ObjectMembers`),对所有值一样。
///
/// `Attrs` 是唯一例外:它需要 `by` 那些标记(见 `MemberSet.Create` 里
/// `Attr.By` 那一段),而那些标记存在**变量**上(`PropertyVal.Var`)。
/// 裸的 property(没有挂在任何变量上,比如 `default` 造出来那个)没有变量
/// —— 给空表,不报错:问"它有哪些标记",答"没有"是句真话。</summary>
internal static class PropertyMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(PropertyMembers), ObjectMembers.Set);

    /// <summary>attrs 只有一份,在变量上(见 `PropertyVal.Var`);裸的 property 没挂变量 -> 空表。</summary>
    [ClassMethod("Attrs")]
    private static RuntimeValue Attrs(RuntimeValue self)
        => new ListVal([.. (((PropertyVal)self).Var?.Attrs ?? []).Select(a => new StringVal(a))]);
}
