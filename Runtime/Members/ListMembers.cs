namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal sealed class ListMembers : SequenceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ListMembers), ObjectMembers.Set);

    // dict 自己提供 `ContainsKey` / `ContainsValue`,不再有序列意义上的 `Contains`;list / set 仍要。
    [ClassMethod("Contains")]
    private static RuntimeValue Contains(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((ListVal)self).Elements.Contains(arg));

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
        if (arg is RangeVal)
            throw new RuntimeException(
                $"list.At 收一个下标（切一段用 list.Slice —— `xs[1..3]` 就走它），得到区间 {arg}",
                ErrorKind.Argument);

        var lst = Indexed(self, arg, "list.At");
        return lst.Elements[((IntVal)arg).Value];
    }

    [ClassMethod("Slice")]
    private static RuntimeValue Slice(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not RangeVal rng)
            throw new RuntimeException($"list.Slice 收一个区间（`xs[1..3]`），得到 {arg.Type}", ErrorKind.Argument);

        var lst = (ListVal)self;
        var (from, to, reverse) = StringMembers.SliceBounds(rng, lst.Elements.Count, "list.Slice", clamp: true);
        var part = lst.Elements.GetRange(from, to - from);
        if (reverse) part.Reverse();
        return new ListVal([.. part]);
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

    // ── 运算符:与 set 的 `Union` / `Except` 同一套意思,只是 list 有顺序、也留重复 ──
    // `xs + ys` 拼起来(`Concat` 的运算符写法);`xs - ys` 去掉 ys 里出现过的元素。
    // 两个都交回**新的 list**,原 list 不动。

    [ClassOp("+")]
    private static RuntimeValue ConcatOp(RuntimeValue a, RuntimeValue b)
        => new ListVal([.. ((ListVal)a).Elements, .. BuiltinClasses.ElementsOf(b, "list 的 '+'")]);

    [ClassOp("-")]
    private static RuntimeValue ExceptOp(RuntimeValue a, RuntimeValue b)
    {
        var drop = BuiltinClasses.ElementsOf(b, "list 的 '-'").ToHashSet(SameComparer.Instance);
        return new ListVal([.. ((ListVal)a).Elements.Where(x => !drop.Contains(x))]);
    }
}
