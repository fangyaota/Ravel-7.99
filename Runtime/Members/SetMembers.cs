namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

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

    // ── 运算符:和上面两个方法同一个口径 ──
    // `s + t` 就是 `s.Union t`,`s - t` 就是 `s.Except t`:交回**新的 set**,原集合不动;
    // 右边同样可以是任何取得出元素的东西(list / set / dict / 插件容器)。

    [ClassOp("+")]
    private static RuntimeValue UnionOp(RuntimeValue a, RuntimeValue b) => Union(a, b);

    [ClassOp("-")]
    private static RuntimeValue ExceptOp(RuntimeValue a, RuntimeValue b) => Except(a, b);

    [ClassMethod("IsSubsetOf")]
    private static RuntimeValue IsSubsetOf(RuntimeValue self, RuntimeValue arg)
    {
        var other = BuiltinClasses.ElementsOf(arg, "IsSubsetOf").ToHashSet();
        return new BoolVal(((SetVal)self).Elements.All(other.Contains));
    }
}
