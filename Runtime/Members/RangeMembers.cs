namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class RangeMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(RangeMembers), ObjectMembers.Set);

    // 端点:写出来那两个数(**任意数值** —— 开区间的那端不在元素里,要元素用 First / Last)
    [ClassMethod("Start")]
    private static RuntimeValue Start(RuntimeValue self) => ((RangeVal)self).Start;

    [ClassMethod("End")]
    private static RuntimeValue End(RuntimeValue self) => ((RangeVal)self).End;

    [ClassMethod("Count")]
    private static RuntimeValue Count(RuntimeValue self)
    {
        var n = ((RangeVal)self).CountValue();
        return n >= int.MinValue && n <= int.MaxValue ? IntVal.Of((int)n) : new BigIntVal(n);
    }

    [ClassMethod("IsEmpty")]
    private static RuntimeValue IsEmpty(RuntimeValue self) => new BoolVal(((RangeVal)self).IsEmpty());

    [ClassMethod("IsBounded")]
    private static RuntimeValue IsBounded(RuntimeValue self) => new BoolVal(((RangeVal)self).IsBounded());

    [ClassMethod("IsWhole")]
    private static RuntimeValue IsWhole(RuntimeValue self) => new BoolVal(((RangeVal)self).IsWhole());

    [ClassMethod("Step")]
    private static RuntimeValue Step(RuntimeValue self) => IntVal.Of(((RangeVal)self).Step());

    [ClassMethod("Contains")]
    private static RuntimeValue Contains(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((RangeVal)self).ContainsValue(arg));

    [ClassMethod("Covers")]
    private static RuntimeValue Covers(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((RangeVal)self).CoversValue(arg));

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

    [ClassMethod("Text")]
    private static RuntimeValue Text(RuntimeValue self) => new StringVal(self.ToString()!);

    [ClassMethod("First")]
    private static RuntimeValue First(RuntimeValue self)
    {
        var r = (RangeVal)self;
        if (r.IsEmpty()) throw new RuntimeException(Messages.T("runtime.range.first_on_empty"), ErrorKind.Index);
        return r.FirstElement();
    }

    [ClassMethod("Last")]
    private static RuntimeValue Last(RuntimeValue self)
    {
        var r = (RangeVal)self;
        if (r.IsEmpty()) throw new RuntimeException(Messages.T("runtime.range.last_on_empty"), ErrorKind.Index);
        return r.LastElement();
    }
}
