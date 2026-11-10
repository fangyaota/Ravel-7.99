namespace Ravel.Runtime;

[RavelModule("Structures")]
[RavelClass("SortedDict")]
internal static class SortedDictClass
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg)
    {
        var d = Structure.NewSortedDict();
        if (arg is DictVal src)
            foreach (var (k, v) in src.Entries) d[PluginKit.Key(k, "SortedDict 的键")] = v;
        else if (arg is SortedDictVal sorted)
            foreach (var (k, v) in sorted.Entries) d[k] = v;

        return new SortedDictVal(d);
    }

    [ClassMethod("Set")]
    public static RuntimeValue Set(RuntimeValue self, RuntimeValue k) => FunctionVal.From(v =>
    {
        Val(self).Entries[PluginKit.Key(k, "SortedDict.Set 的键")] = v;
        return VoidVal.Instance;
    });

    [ClassMethod("Get")]
    public static RuntimeValue Get(RuntimeValue self, RuntimeValue k)
    {
        var key = PluginKit.Key(k, "SortedDict.Get 的键");
        return Val(self).Entries.TryGetValue(key, out var v)
            ? v
            : throw PluginKit.Fail($"SortedDict.Get: 键不存在: {key}", ErrorKind.Key);
    }

    [ClassMethod("GetOr")]
    public static RuntimeValue GetOr(RuntimeValue self, RuntimeValue k) => FunctionVal.From(dflt =>
    {
        var key = PluginKit.Key(k, "SortedDict.GetOr 的键");
        return Val(self).Entries.TryGetValue(key, out var v) ? v : dflt;
    });

    [ClassMethod("Has")]
    public static RuntimeValue Has(RuntimeValue self, RuntimeValue k)
        => new BoolVal(Val(self).Entries.ContainsKey(PluginKit.Key(k, "SortedDict.Has 的键")));

    [ClassMethod("Remove")]
    public static RuntimeValue Remove(RuntimeValue self, RuntimeValue k)
        => new BoolVal(Val(self).Entries.Remove(PluginKit.Key(k, "SortedDict.Remove 的键")));

    [ClassMethod("Keys")]
    public static RuntimeValue Keys(RuntimeValue self) => new ListVal([.. Val(self).Entries.Keys]);

    [ClassMethod("Values")]
    public static RuntimeValue Values(RuntimeValue self) => new ListVal([.. Val(self).Entries.Values]);

    [ClassMethod("Count")]
    public static RuntimeValue Count(RuntimeValue self) => IntVal.Of(Val(self).Entries.Count);

    [ClassMethod("IsEmpty")]
    public static RuntimeValue IsEmpty(RuntimeValue self) => new BoolVal(Val(self).Entries.Count == 0);

    [ClassMethod("Clear")]
    public static RuntimeValue Clear(RuntimeValue self)
    {
        Val(self).Entries.Clear();
        return VoidVal.Instance;
    }

    private static SortedDictVal Val(RuntimeValue self) => (SortedDictVal)self;
}
