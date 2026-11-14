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
        throw new RuntimeException(Messages.T("runtime.dict.key_not_found", arg), ErrorKind.Key);
    }

    [ClassMethod("Set")]
    private static RuntimeValue Assign(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.KeyArg(arg, "dict.Set 的键");
        return FunctionVal.From(v =>
        {
            ((DictVal)self).Entries[key] = v;
            return VoidVal.Instance;
        });
    }

    [ClassMethod("ContainsKey")]
    private static RuntimeValue HasKey(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((DictVal)self).Entries.ContainsKey(BuiltinClasses.KeyArg(arg, "dict.ContainsKey 的键")));

    [ClassMethod("Keys")]
    private static RuntimeValue Keys(RuntimeValue self) => new ListVal([.. ((DictVal)self).Entries.Keys]);

    [ClassMethod("Values")]
    private static RuntimeValue Values(RuntimeValue self) => new ListVal([.. ((DictVal)self).Entries.Values]);

    // 摊成 [[键 值] …],和 ToDict 配对(序列口径也是它,见 `SequenceMembers.Pairs`)
    [ClassMethod("Entries")]
    private static RuntimeValue Entries(RuntimeValue self) => new ListVal([.. Pairs((DictVal)self)]);

    [ClassMethod("Remove")]
    private static RuntimeValue RemoveKey(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((DictVal)self).Entries.Remove(BuiltinClasses.KeyArg(arg, "dict.Remove 的键")));

    // `d.Contains (Pair k v)` —— 表里有没有"这一对"。
    // 从前是 `IEnumerable` 默认的那条(拿整条序列比元素),dict 上恒为 false,等于没有;
    // 现在单列一条,参数就要一个 `Pair`(要查值就用 `ContainsValue`)。
    [ClassMethod("Contains")]
    private static RuntimeValue ContainsPair(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not PairVal pair)
            throw new RuntimeException(Messages.T("runtime.dict.not_a_pair", arg.Type), ErrorKind.Argument);
        var d = (DictVal)self;
        return new BoolVal(d.Entries.TryGetValue(pair.Key, out var v)
                           && BuiltinClasses.StrictSame(v, pair.Value));
    }

    [ClassMethod("ContainsValue")]
    private static RuntimeValue HasValue(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((DictVal)self).Entries.ContainsValue(arg));

    [ClassMethod("GetOr")]
    private static RuntimeValue GetOrElse(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.KeyArg(arg, "dict.GetOr 的键");
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
            throw new RuntimeException(Messages.T("runtime.dict.merge_needs_dict", b.Type), ErrorKind.Type);

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
