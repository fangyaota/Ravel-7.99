namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`Range` 这个类给它的实例准备什么。**
///
/// 区间那几条。都只跟那**一对边界 + 开闭**打交道,所以都是 O(1)
/// (只有 `ToList ()` 是 O(n) —— 那是用户自己要铺成表的)。
///
/// 它同时也是一个 `IEnumerable`(impl 在 lib/iterator.rav,枚举走生成器的光标、惰性),
/// 所以 `Map` / `Where` / `Fold` / `Take` 那整套是白拿的;这里只把**该更快或者
/// 该更直白**的几条先摆出来(`Count` / `Contains` / `ToList` 与接口那份语义一致,
/// 只是不必走一遍迭代)。
///
/// 从前这一个类的方法挤在 `RegisterRangeMethods` 里 —— 现在"区间有哪些成员"就是
/// 这个类有哪几个 `[ClassMethod]`,和 `String` / `List` 那几份摆在一起看。</summary>
internal static class RangeMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(RangeMembers), ObjectMembers.Set);

    // 端点:写出来那两个数(**任意数值** —— 开区间的那端不在元素里,要元素用 First / Last)
    [ClassMethod("Start")]
    private static RuntimeValue Start(RuntimeValue self) => ((RangeVal)self).Start;

    [ClassMethod("End")]
    private static RuntimeValue End(RuntimeValue self) => ((RangeVal)self).End;

    /// <summary>个数:装得下给 int、装不下给 bigint。</summary>
    [ClassMethod("Count")]
    private static RuntimeValue Count(RuntimeValue self)
    {
        var n = ((RangeVal)self).CountValue();
        return n >= int.MinValue && n <= int.MaxValue ? IntVal.Of((int)n) : new BigIntVal(n);
    }

    [ClassMethod("IsEmpty")]
    private static RuntimeValue IsEmpty(RuntimeValue self) => new BoolVal(((RangeVal)self).IsEmpty());

    /// <summary>两头都定得下来吗(`[1..]` / `[..1]` 不是)—— `Count` / `Last` / `ToList` 答不出来
    /// 就是因为这个,所以给它一个问法,别让人靠"试试看会不会报错"去猜。</summary>
    [ClassMethod("IsBounded")]
    private static RuntimeValue IsBounded(RuntimeValue self) => new BoolVal(((RangeVal)self).IsBounded());

    /// <summary>整条数轴(`[..]`):谁都在里头,可没有起手那一头 —— 比"无界"还少一样东西。</summary>
    [ClassMethod("IsWhole")]
    private static RuntimeValue IsWhole(RuntimeValue self) => new BoolVal(((RangeVal)self).IsWhole());

    /// <summary>往哪个方向数:1 正着、-1 倒着。无界也答得出来(方向由无穷在哪一头定,见 RangeVal)。</summary>
    [ClassMethod("Step")]
    private static RuntimeValue Step(RuntimeValue self) => IntVal.Of(((RangeVal)self).Step());

    [ClassMethod("Contains")]
    private static RuntimeValue Contains(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((RangeVal)self).ContainsValue(arg));

    /// <summary>和 `Contains` 分开的两个问法:`Contains` 是"里头有没有这个**元素**"(和别的容器
    /// 一个意思,元素是那些整数),`Covers` 是"这个值**落不落在这段里**"(按端点比,
    /// 小数也算数)。Ruby 也是这么分的(`include?` / `cover?`)。</summary>
    [ClassMethod("Covers")]
    private static RuntimeValue Covers(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((RangeVal)self).CoversValue(arg));

    /// <summary>铺成表 —— 只有这一条是 O(n)。空区间给空表(不是报错),**无限长的报错**(铺不完)。</summary>
    [ClassMethod("ToList")]
    private static RuntimeValue ToList(RuntimeValue self)
    {
        var r = (RangeVal)self;
        if (!r.IsBounded())
            throw new RuntimeException(
                r.IsWhole()
                    ? $"整条数轴铺不成表（{r} —— 它谁都在里头，但没有起手那一头）"
                    : $"无限长的区间铺不成表（{r}）—— 用 Take 取前几个",
                ErrorKind.Value);
        var (first, last, up) = r.Walk();
        var out_ = new List<RuntimeValue>();
        for (var i = first; up ? i <= last : i >= last; i += up ? 1 : -1) out_.Add(r.Element(i));
        return new ListVal(out_);
    }

    /// <summary>`Text ()` 就是那个记号本身(和 `print` 一致)。</summary>
    [ClassMethod("Text")]
    private static RuntimeValue Text(RuntimeValue self) => new StringVal(self.ToString()!);

    /// <summary>头一个 / 末一个**元素**(不是那两个端点 —— 开区间的端点在区间外)。
    /// 接口默认那份要迭代一遍,这两条 O(1);空的照旧报错,措辞和默认那份一致。</summary>
    [ClassMethod("First")]
    private static RuntimeValue First(RuntimeValue self)
    {
        var r = (RangeVal)self;
        if (r.IsEmpty()) throw new RuntimeException("First: 元素不够（一共 0 个）", ErrorKind.Index);
        return r.FirstElement();
    }

    [ClassMethod("Last")]
    private static RuntimeValue Last(RuntimeValue self)
    {
        var r = (RangeVal)self;
        if (r.IsEmpty()) throw new RuntimeException("Last: 元素不够（一共 0 个）", ErrorKind.Index);
        return r.LastElement();
    }
}
