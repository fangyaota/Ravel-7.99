namespace Ravel.Runtime;

/// <summary>**按值排序的集合**:枚举就是升序,`Min ()` / `Max ()` 是 O(log n)。
///
///     s := SortedSet ()
///     s.Add 3 / s.Add 1
///     s.Min ()      # 1
///
/// 里面的东西得能互相比大小(按 Ravel 的 `<`);比不了当场报错。
/// 相等也按 `<` 算 —— `1` 与 `1.0` 在集合里是同一个(和 `set` 那边按类型分不一样)。</summary>
[RavelModule("Structures")]
[RavelClass("SortedSet")]
internal static class SortedSetClass
{
    /// <summary>空集合;给一个容器就照它播种(`SortedSet [3 1 2]`,重复的进不去)</summary>
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg)
    {
        var set = Structure.NewSortedSet();
        foreach (var x in Structure.Seed(arg, "SortedSet")) set.Add(x);
        return new SortedSetVal(set);
    }

    [ClassMethod("Add")]
    public static RuntimeValue Add(RuntimeValue self, RuntimeValue v)
    {
        Val(self).Elements.Add(v);      // 已经有了就什么都不做(集合的脾气)
        return VoidVal.Instance;
    }

    [ClassMethod("Has")]
    public static RuntimeValue Has(RuntimeValue self, RuntimeValue v) => new BoolVal(Val(self).Elements.Contains(v));

    [ClassMethod("Remove")]
    public static RuntimeValue Remove(RuntimeValue self, RuntimeValue v) => new BoolVal(Val(self).Elements.Remove(v));

    [ClassMethod("Min")]
    public static RuntimeValue Min(RuntimeValue self)
        => Val(self).Elements.Count > 0
            ? Val(self).Elements.Min!
            : throw PluginKit.Fail("SortedSet.Min: 空集合没有最小", ErrorKind.Index);

    [ClassMethod("Max")]
    public static RuntimeValue Max(RuntimeValue self)
        => Val(self).Elements.Count > 0
            ? Val(self).Elements.Max!
            : throw PluginKit.Fail("SortedSet.Max: 空集合没有最大", ErrorKind.Index);

    [ClassMethod("Count")]
    public static RuntimeValue Count(RuntimeValue self) => new IntVal(Val(self).Elements.Count);

    [ClassMethod("IsEmpty")]
    public static RuntimeValue IsEmpty(RuntimeValue self) => new BoolVal(Val(self).Elements.Count == 0);

    /// <summary>升序的一串</summary>
    [ClassMethod("ToList")]
    public static RuntimeValue ToList(RuntimeValue self) => new ListVal([.. Val(self).Elements]);

    [ClassMethod("Clear")]
    public static RuntimeValue Clear(RuntimeValue self)
    {
        Val(self).Elements.Clear();
        return VoidVal.Instance;
    }

    private static SortedSetVal Val(RuntimeValue self) => (SortedSetVal)self;
}
