namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

internal abstract class SequenceMembers
{
    // ── 高阶方法:值本身就是成员(`[ClassValue]`) ──
    //
    // 这批**不是** `BuiltinMethodVal`:`Map f` 要调用户函数,而原生闭包是同步的 C# 调用 ——
    // 调不了(`SeqMethod` 绑完交出去一个 `ControlFunction`,求值器推 `SeqOp` 帧、一个元素一步地走)。
    // 从前它们在 `BuiltinClasses.Sequences.cs` 的 `RegisterHigherOrderMethods` 里靠
    // `EngineMember` 往注册表写,现在字段就在这儿、名字挂在特性上。
    //
    // **字段声明必须排在 `Set` 之前**:`Set = MemberSet.Scan(...)` 是靠反射读这些字段建成员集的,
    // 还没初始化就读到 null 了(见 `MemberSet.Scan` 里那条检查)。

    [ClassValue("Foreach")]
    private static readonly RuntimeValue Foreach = new SeqMethod(SeqMode.Each, 3) { Name = "Foreach" };

    [ClassValue("Map")]
    private static readonly RuntimeValue Map = new SeqMethod(SeqMode.Map, 3) { Name = "Map" };

    [ClassValue("Where")]
    private static readonly RuntimeValue Where = new SeqMethod(SeqMode.Where, 3) { Name = "Where" };

    [ClassValue("All")]
    private static readonly RuntimeValue All = new SeqMethod(SeqMode.All, 3) { Name = "All" };

    [ClassValue("Any")]
    private static readonly RuntimeValue Any = new SeqMethod(SeqMode.Any, 3) { Name = "Any" };

    [ClassValue("Find")]
    private static readonly RuntimeValue Find = new SeqMethod(SeqMode.Find, 3) { Name = "Find" };

    [ClassValue("SortBy")]
    private static readonly RuntimeValue SortBy = new SeqMethod(SeqMode.SortBy, 3) { Name = "SortBy" };

    [ClassValue("Fold")]
    private static readonly RuntimeValue Fold = new SeqMethod(SeqMode.Fold, 4) { Name = "Fold" };

    // dict 当序列时枚举的是 `[键 值]` 两格(与 `ToDict` 收的东西对称)。
    protected static IEnumerable<RuntimeValue> Items(RuntimeValue s) => s switch
    {
        ListVal l => l.Elements,
        SetVal st => st.Elements,
        DictVal d => Pairs(d),
        // 一对就是"一串两格":`Foreach` / `Map` / `ToList` 在它身上照样成立。
        PairVal p => [p.Key, p.Value],
        _ => throw new RuntimeException($"序列方法只对 list / set / dict / pair 有意义，得到 {s.Type}", ErrorKind.Type),
    };

    protected static IEnumerable<RuntimeValue> Pairs(DictVal d)
    {
        foreach (var (k, v) in d.Entries) yield return new ListVal([k, v]);
    }

    // ── 个数 / 空 ──

    [ClassMethod("Count")]
    private static RuntimeValue Count(RuntimeValue self) => IntVal.Of(Items(self).Count());

    [ClassMethod("IsEmpty")]
    private static RuntimeValue IsEmpty(RuntimeValue self) => new BoolVal(!Items(self).Any());

    // ── 头尾 ──

    [ClassMethod("First")]
    private static RuntimeValue First(RuntimeValue self) => FirstOf(Items(self), "First");

    [ClassMethod("Last")]
    private static RuntimeValue Last(RuntimeValue self) => LastOf(Items(self), "Last");

    // ── 换形 ──

    [ClassMethod("ToList")]
    private static RuntimeValue ToList(RuntimeValue self) => new ListVal([.. Items(self)]);

    [ClassMethod("ToSet")]
    private static RuntimeValue ToSet(RuntimeValue self) => new SetVal([.. Items(self)]);

    [ClassMethod("Distinct")]
    private static RuntimeValue Distinct(RuntimeValue self)
    {
        var seen = new HashSet<RuntimeValue>();
        var kept = new List<RuntimeValue>();
        foreach (var x in Items(self))
            if (seen.Add(x))
                kept.Add(x);
        return new ListVal(kept);
    }

    [ClassMethod("Reverse")]
    private static RuntimeValue Reverse(RuntimeValue self) => new ListVal([.. Enumerable.Reverse(Items(self))]);

    [ClassMethod("Take")]
    private static RuntimeValue Take(RuntimeValue self, RuntimeValue arg)
        => new ListVal([.. Enumerable.Take(Items(self), BuiltinClasses.IntArg(arg, "Take"))]);

    [ClassMethod("Skip")]
    private static RuntimeValue Skip(RuntimeValue self, RuntimeValue arg)
        => new ListVal([.. Enumerable.Skip(Items(self), BuiltinClasses.IntArg(arg, "Skip"))]);

    [ClassMethod("Concat")]
    private static RuntimeValue Concat(RuntimeValue self, RuntimeValue arg)
        => new ListVal([.. Items(self), .. BuiltinClasses.ElementsOf(arg, "Concat")]);

    [ClassMethod("Join")]
    private static RuntimeValue Join(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not StringVal sep) throw new RuntimeException("Join 需要 string 分隔符", ErrorKind.Argument);
        return new StringVal(string.Join(sep.Value, Items(self).Select(x => x.ToString())));
    }

    // ── 算 ──

    [ClassMethod("Sum")]
    private static RuntimeValue Sum(RuntimeValue self)
    {
        RuntimeValue acc = IntVal.Of(0);          // 空集合求和给 0(C# 的 Sum() 也是这样)
        foreach (var x in Items(self)) acc = ApplyOp("+", acc, x);
        return acc;
    }

    [ClassMethod("Min")]
    private static RuntimeValue Min(RuntimeValue self) => Extreme(Items(self), "Min", keepLess: true);

    [ClassMethod("Max")]
    private static RuntimeValue Max(RuntimeValue self) => Extreme(Items(self), "Max", keepLess: false);

    // ── 本地小工具 ──
    // 这几样原件在 `BuiltinClasses.Sequences.cs` 里(都是 `private`),新文件够不着;
    // list / set / dict 已经不走那条路了,所以在这儿留一份同义的实现,措辞也照抄 ——
    // 将来 Sequences.cs 里那份可以删(不在本次范围)。

    private static RuntimeValue FirstOf(IEnumerable<RuntimeValue> xs, string what)
    {
        foreach (var x in xs) return x;
        throw new RuntimeException($"{what}: 元素不够（一共 0 个）", ErrorKind.Index);
    }

    private static RuntimeValue LastOf(IEnumerable<RuntimeValue> xs, string what)
    {
        if (xs is IReadOnlyList<RuntimeValue> list)          // List —— 直取末一个,O(1)
        {
            if (list.Count == 0) throw new RuntimeException($"{what}: 元素不够（一共 0 个）", ErrorKind.Index);
            return list[^1];
        }

        var found = false;
        RuntimeValue last = VoidVal.Instance;
        foreach (var x in xs) { last = x; found = true; }
        if (!found) throw new RuntimeException($"{what}: 元素不够（一共 0 个）", ErrorKind.Index);
        return last;
    }

    private static RuntimeValue ApplyOp(string op, RuntimeValue a, RuntimeValue b)
        => a.MemberScope.LookupField(op)?.Value is BuiltinMethodVal m
            ? m.Impl(a, b)
            : throw new RuntimeException($"'{op}' 不支持 {a.Type}（{a.Type} 与 {b.Type} 之间）", ErrorKind.Type);

    private static RuntimeValue Extreme(IEnumerable<RuntimeValue> xs, string what, bool keepLess)
    {
        using var e = xs.GetEnumerator();
        if (!e.MoveNext()) throw new RuntimeException($"{what}: 空集合没有{what}", ErrorKind.Value);
        var best = e.Current;
        while (e.MoveNext())
        {
            var x = e.Current;
            // 比的方向跟着"留小的"还是"留大的"走 —— 报错里那两个类型的先后顺序就是它。
            if (keepLess ? BuiltinClasses.Less(x, best) : BuiltinClasses.Less(best, x)) best = x;
        }

        return best;
    }
}
