namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal sealed class DictMembers : SequenceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(DictMembers), ObjectMembers.Set);

    [ClassMethod("SysGet")]
    private static RuntimeValue SysGet(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.KeyArg(arg, "dict.SysGet 的键");
        var d = (DictVal)self;
        if (d.Entries.TryGetValue(key, out var v)) return v;
        throw new RuntimeException($"键不存在: {key}", ErrorKind.Key);
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
}
