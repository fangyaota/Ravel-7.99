namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

/// <summary>**`Set` 这个类给它的实例准备什么。**
///
/// 集合**自己有**的:增删,以及集合代数(`Union` / `Intersect` / `Except` / `IsSubsetOf`)。
/// 序列那一批(`Count` / `Contains` / `ToList` / `Distinct` …)从 `SequenceMembers` 继承 ——
/// 集合和列表、字典共用同一份实现,不必各写一遍。
///
/// 集合代数**交回新的**,原集合不动:和 `Map`/`Where`/`Sort` 一个规矩
/// (要就地改的那几条是 `Add`/`Remove`/`Clear`,它们交回 `()`)。
///
/// 从前这些挤在 `RegisterSetMethods` 里;搬过来之后"集合有哪些成员"就是这个类有哪几个
/// `[ClassMethod]`。</summary>
internal sealed class SetMembers : SequenceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(SetMembers), ObjectMembers.Set);

    [ClassMethod("Add")]
    private static RuntimeValue Add(RuntimeValue self, RuntimeValue arg)
    {
        ((SetVal)self).Elements.Add(arg);
        return VoidVal.Instance;
    }

    [ClassMethod("Remove")]
    private static RuntimeValue Remove(RuntimeValue self, RuntimeValue arg)
    {
        ((SetVal)self).Elements.Remove(arg);
        return VoidVal.Instance;
    }

    [ClassMethod("Clear")]
    private static RuntimeValue Clear(RuntimeValue self)
    {
        ((SetVal)self).Elements.Clear();
        return VoidVal.Instance;
    }

    // ── 集合代数(C# 的 HashSet 同名方法):交回**新的 set**,原集合不动 ──
    // 另一头可以是任何"取得出元素"的东西(list / set / dict / 插件容器,见 `ElementsOf`)

    [ClassMethod("Union")]
    private static RuntimeValue Union(RuntimeValue self, RuntimeValue arg)
        => new SetVal([.. ((SetVal)self).Elements, .. BuiltinClasses.ElementsOf(arg, "Union")]);

    [ClassMethod("Intersect")]
    private static RuntimeValue Intersect(RuntimeValue self, RuntimeValue arg)
    {
        var other = BuiltinClasses.ElementsOf(arg, "Intersect").ToHashSet();
        return new SetVal([.. ((SetVal)self).Elements.Where(other.Contains)]);
    }

    [ClassMethod("Except")]
    private static RuntimeValue Except(RuntimeValue self, RuntimeValue arg)
    {
        var other = BuiltinClasses.ElementsOf(arg, "Except").ToHashSet();
        return new SetVal([.. ((SetVal)self).Elements.Where(x => !other.Contains(x))]);
    }

    [ClassMethod("IsSubsetOf")]
    private static RuntimeValue IsSubsetOf(RuntimeValue self, RuntimeValue arg)
    {
        var other = BuiltinClasses.ElementsOf(arg, "IsSubsetOf").ToHashSet();
        return new BoolVal(((SetVal)self).Elements.All(other.Contains));
    }
}
