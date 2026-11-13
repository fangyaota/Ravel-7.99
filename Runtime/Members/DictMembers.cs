namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal sealed class DictMembers : SequenceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(DictMembers), ObjectMembers.Set);

    // `d[k]` 走这条；`d.Get k` 也落到这儿（lib/keys.rav 的 IDict）。
    [ClassMethod("At")]
    private static RuntimeValue At(RuntimeValue self, RuntimeValue arg)
    {
        var d = (DictVal)self;
        if (d.Entries.TryGetValue(arg, out var v)) return v;
        throw new RuntimeException($"键不存在: {arg}", ErrorKind.Key);
    }

    [ClassMethod("SysSet")]
    private static RuntimeValue SysSet(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.KeyArg(arg, "dict.SysSet 的键");
        return FunctionVal.From(v =>
        {
            ((DictVal)self).Entries[key] = v;
            return VoidVal.Instance;
        });
    }

    [ClassMethod("SysHas")]
    private static RuntimeValue SysHas(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((DictVal)self).Entries.ContainsKey(BuiltinClasses.KeyArg(arg, "dict.SysHas 的键")));

    [ClassMethod("Keys")]
    private static RuntimeValue Keys(RuntimeValue self) => new ListVal([.. ((DictVal)self).Entries.Keys]);

    [ClassMethod("Values")]
    private static RuntimeValue Values(RuntimeValue self) => new ListVal([.. ((DictVal)self).Entries.Values]);

    // 摊成 [[键 值] …],和 ToDict 配对
    [ClassMethod("Entries")]
    private static RuntimeValue Entries(RuntimeValue self)
    {
        var list = new List<RuntimeValue>();
        foreach (var (k, v) in ((DictVal)self).Entries) list.Add(new ListVal([k, v]));
        return new ListVal(list);
    }

    [ClassMethod("SysRemove")]
    private static RuntimeValue SysRemove(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((DictVal)self).Entries.Remove(BuiltinClasses.KeyArg(arg, "dict.SysRemove 的键")));

    [ClassMethod("HasValue")]
    private static RuntimeValue HasValue(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((DictVal)self).Entries.ContainsValue(arg));

    [ClassMethod("SysGetOr")]
    private static RuntimeValue SysGetOr(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.KeyArg(arg, "dict.SysGetOr 的键");
        var d = (DictVal)self;
        return d.Entries.TryGetValue(key, out var v)
            ? FunctionVal.From(_ => v)        // 有:替代值收下但不用
            : FunctionVal.From(fallback => fallback);
    }

    [ClassMethod("Clear")]
    private static RuntimeValue Clear(RuntimeValue self)
    {
        ((DictVal)self).Entries.Clear();
        return VoidVal.Instance;
    }

    // ── 运算符:两张表的合并 / 按键删 ──
    // `d + e` 合并,右边的键盖左边的(e 得是 dict —— 两个键值面才有得合);
    // `d - e` 按右边的键删,右边是 dict 就取它的**键**,别的容器就把元素当键看。
    // 两个都交回**新的 dict**,原 dict 不动。

    [ClassOp("+")]
    private static RuntimeValue MergeOp(RuntimeValue a, RuntimeValue b)
    {
        if (b is not DictVal other)
            throw new RuntimeException($"dict 的 '+' 右边得是 dict（合并两张表），得到 {b.Type}", ErrorKind.Type);

        var result = new DictVal(((DictVal)a).Entries);
        foreach (var (k, v) in other.Entries) result.Entries[k] = v;
        return result;
    }

    [ClassOp("-")]
    private static RuntimeValue DropKeysOp(RuntimeValue a, RuntimeValue b)
    {
        IEnumerable<RuntimeValue> keys = b is DictVal d
            ? d.Entries.Keys
            : BuiltinClasses.ElementsOf(b, "dict 的 '-'");

        var result = new DictVal(((DictVal)a).Entries);
        foreach (var k in keys) result.Entries.Remove(k);
        return result;
    }
}
