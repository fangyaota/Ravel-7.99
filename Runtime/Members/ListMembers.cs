namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal sealed class ListMembers : SequenceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ListMembers), ObjectMembers.Set);

    private static ListVal Indexed(RuntimeValue s, RuntimeValue a, string what, bool allowEnd = false)
    {
        if (a is not IntVal i) throw new RuntimeException($"{what} 需要 int 参数", ErrorKind.Argument);
        var lst = (ListVal)s;
        var n = lst.Elements.Count;
        if (i.Value < 0 || i.Value > (allowEnd ? n : n - 1))
            throw new RuntimeException($"{what} 的索引 {i.Value} 越界 (列表长度 {n})", ErrorKind.Index);
        return lst;
    }

    private static RuntimeValue RemoveAtImpl(RuntimeValue s, RuntimeValue a, string what)
    {
        var lst = Indexed(s, a, what);
        var idx = ((IntVal)a).Value;
        var v = lst.Elements[idx];
        lst.Elements.RemoveAt(idx);
        return v;
    }

    [ClassMethod("At")]
    private static RuntimeValue At(RuntimeValue self, RuntimeValue arg)
    {
        // 区间就是切片:`xs[1..3]` 和 `xs.At [1..3]` 一条路。
        if (arg is RangeVal rng)
        {
            var lst0 = (ListVal)self;
            var (from, to, reverse) = StringMembers.SliceBounds(rng, lst0.Elements.Count, "list.At");
            var part = lst0.Elements.GetRange(from, to - from);
            if (reverse) part.Reverse();
            return new ListVal([.. part]);
        }

        var lst = Indexed(self, arg, "list.At");
        return lst.Elements[((IntVal)arg).Value];
    }

    [ClassMethod("Add")]
    private static RuntimeValue Add(RuntimeValue self, RuntimeValue arg)
    {
        ((ListVal)self).Elements.Add(arg);
        return VoidVal.Instance;
    }

    [ClassMethod("Remove")]
    private static RuntimeValue Remove(RuntimeValue self, RuntimeValue arg) => RemoveAtImpl(self, arg, "list.Remove");

    [ClassMethod("Insert")]
    private static RuntimeValue Insert(RuntimeValue self, RuntimeValue arg)
    {
        var lst = Indexed(self, arg, "list.Insert", allowEnd: true);
        var idx = ((IntVal)arg).Value;
        return FunctionVal.From(v =>
        {
            lst.Elements.Insert(idx, v);
            return VoidVal.Instance;
        });
    }

    [ClassMethod("Set")]
    private static RuntimeValue SetAt(RuntimeValue self, RuntimeValue arg)
    {
        var lst = Indexed(self, arg, "list.Set");
        var idx = ((IntVal)arg).Value;
        return FunctionVal.From(v =>
        {
            lst.Elements[idx] = v;
            return VoidVal.Instance;
        });
    }

    [ClassMethod("IndexOf")]
    private static RuntimeValue IndexOf(RuntimeValue self, RuntimeValue arg) => IntVal.Of(((ListVal)self).Elements.IndexOf(arg));

    [ClassMethod("RemoveAt")]
    private static RuntimeValue RemoveAt(RuntimeValue self, RuntimeValue arg) => RemoveAtImpl(self, arg, "list.RemoveAt");

    [ClassMethod("AddRange")]
    private static RuntimeValue AddRange(RuntimeValue self, RuntimeValue arg)
    {
        ((ListVal)self).Elements.AddRange(BuiltinClasses.ElementsOf(arg, "AddRange"));
        return VoidVal.Instance;
    }

    [ClassMethod("Clear")]
    private static RuntimeValue Clear(RuntimeValue self)
    {
        ((ListVal)self).Elements.Clear();
        return VoidVal.Instance;
    }

    [ClassMethod("Sort")]
    private static RuntimeValue Sort(RuntimeValue self) => BuiltinClasses.SortByKey(((ListVal)self).Elements, x => x);
}
