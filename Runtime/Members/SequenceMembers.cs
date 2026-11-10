namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

/// <summary>**三种容器(`list` / `set` / `dict`)共用的那一批序列方法 —— 只写一遍。**
///
/// 这是 `BuiltinClasses.Sequences.cs` 里 `RegisterSequenceMethods(type, elems)` 的接手者:
/// 从前那个方法被调用三次、每次把**同一批**方法往一张表上挂一遍,只换一个"怎么取元素"的
/// 函数。现在换成 C# 的**继承**来说同一件事 —— `ListMembers` / `SetMembers` / `DictMembers`
/// 各继承这个类一次,`MemberSet.Scan` 沿 C# 基类往上扫,于是那批 `[ClassMethod]`
/// 三种容器都有,**实现只有这一份**。
///
/// 为什么是 `abstract class` 而不是 `static class`:静态类不能被继承,三种容器就没法
/// 各拿一份 `Set`。这个类自己**从来没有实例** —— 它只当"方法放在哪儿"的落脚处。
///
/// 取元素只有一条规矩(`Items`):**按枚举顺序**给。字典给的是**值**(按键找用
/// `Has` / `Keys ()`),因为字典在"序列"这个身份下就是一串值 —— 和从前
/// `RegisterSequenceMethods(Dict, s => ((DictVal)s).Entries.Values)` 一模一样。
///
/// **借容器自己那一份,不拷**:`Items` 交回的就是容器里那个 `List` / `HashSet` /
/// `Values` 本身,所以 `Count` 是 O(1)(`Enumerable.Count` 认出 `ICollection`)、
/// `First` 也只取一个。要拷由用户明说(`ToList` / `ToSet`)。</summary>
internal abstract class SequenceMembers
{
    /// <summary>**按枚举顺序取出元素** —— 三种容器各自的那一份,不拷。
    /// 别的类型走到这儿说明"序列方法挂在不该有的类上了",当场报类型错(不是静默空序列)。</summary>
    private static IEnumerable<RuntimeValue> Items(RuntimeValue s) => s switch
    {
        ListVal l => l.Elements,
        SetVal st => st.Elements,
        DictVal d => d.Entries.Values,
        _ => throw new RuntimeException($"序列方法只对 list / set / dict 有意义，得到 {s.Type}", ErrorKind.Type),
    };

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

    // ── 查 ──

    [ClassMethod("Contains")]
    private static RuntimeValue Contains(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(Items(self).Contains(arg));

    // ── 换形 ──

    [ClassMethod("ToList")]
    private static RuntimeValue ToList(RuntimeValue self) => new ListVal([.. Items(self)]);

    [ClassMethod("ToSet")]
    private static RuntimeValue ToSet(RuntimeValue self) => new SetVal([.. Items(self)]);

    /// <summary>去重 —— 判等走值自己的相等(和 `set` 里那套一致),顺序按**第一次出现**。</summary>
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

    /// <summary>**惰性翻,不就地翻** —— "借来的"那张表是用户的(`Items` 交回的就是容器里那一份),
    /// 就地 `Reverse()` 会把用户的容器改了;这里只交回一个新表。</summary>
    [ClassMethod("Reverse")]
    private static RuntimeValue Reverse(RuntimeValue self) => new ListVal([.. Enumerable.Reverse(Items(self))]);

    [ClassMethod("Take")]
    private static RuntimeValue Take(RuntimeValue self, RuntimeValue arg)
        => new ListVal([.. Enumerable.Take(Items(self), BuiltinClasses.IntArg(arg, "Take"))]);

    [ClassMethod("Skip")]
    private static RuntimeValue Skip(RuntimeValue self, RuntimeValue arg)
        => new ListVal([.. Enumerable.Skip(Items(self), BuiltinClasses.IntArg(arg, "Skip"))]);

    /// <summary>接上另一串(**交回新的**,原容器不动 —— 和 `Map` / `Where` 那批一个规矩)。
    /// 另一头可以是任何"取得出元素"的东西(含插件容器,见 `BuiltinClasses.ElementsOf`)。</summary>
    [ClassMethod("Concat")]
    private static RuntimeValue Concat(RuntimeValue self, RuntimeValue arg)
        => new ListVal([.. Items(self), .. BuiltinClasses.ElementsOf(arg, "Concat")]);

    /// <summary>把元素**一个个打印出来**接成串;分隔符**只收 string**(不是 `TextArg` ——
    /// 这条从前的口径就是"要一个字符串分隔符",别在这里悄悄放宽)。
    /// 元素自己怎么打印由它的 `ToString` 说了算(所以 `[1 2].Join ","` 是 `"1,2"`)。</summary>
    [ClassMethod("Join")]
    private static RuntimeValue Join(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not StringVal sep) throw new RuntimeException("Join 需要 string 分隔符", ErrorKind.Argument);
        return new StringVal(string.Join(sep.Value, Items(self).Select(x => x.ToString())));
    }

    // ── 算 ──

    /// <summary>求和 —— 走的是 `+` 那条算子(见 `ApplyOp`),所以小数、字符串拼接
    /// 这些"加得起来的东西"都一样能求和,不必另立一套。
    /// **空集合给 `0`**(C# 的 `Sum()` 也是这样),不是报错 —— 从 `0` 起手、逐个 `+` 上来。</summary>
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

    /// <summary>头一个 / 末一个元素。**空集合报的是同一句话** ——
    /// 从前走 `Nth(xs, 0/-1)` 那条:`Nth` 要按下标取,而借来的表不该要求 `IList`
    /// (`HashSet` / `Dictionary.Values` 都不是)。措辞一个字没改。</summary>
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

    /// <summary>`a op b` —— 求和用;运算符**不在这里实现**,只按名字去接收者的成员域里找那一条
    /// (运算符都在 `Operators.cs` 注册,`+` 挂的是 `BuiltinMethodVal`)。找不着就说清楚是哪两个类型。</summary>
    private static RuntimeValue ApplyOp(string op, RuntimeValue a, RuntimeValue b)
        => a.MemberScope.LookupField(op)?.Value is BuiltinMethodVal m
            ? m.Impl(a, b)
            : throw new RuntimeException($"'{op}' 不支持 {a.Type}（{a.Type} 与 {b.Type} 之间）", ErrorKind.Type);

    /// <summary>最小 / 最大 —— 比不了的时候由 `Less` 当场说人话(见 `BuiltinClasses.Less`)。
    /// 空集合报错:没有元素就没有极值,给个 `()` 只会让错晚一步。</summary>
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
