namespace Ravel.Runtime;

using System.Numerics;

public record RangeVal(RuntimeValue Start, RuntimeValue End, bool StartClosed, bool EndClosed) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Range;

    public bool IsBounded() => !Infinite(Start, out _) && !Infinite(End, out _);

    public bool IsWhole() => Infinite(Start, out _) && Infinite(End, out _);

    public (BigInteger First, bool Up) Head()
    {
        if (IsWhole())
            throw new RuntimeException(Messages.T("runtime.range.whole_has_no_first", this), ErrorKind.Value);
        if (!Ascending(out var up)) return (1, true);              // NaN:空
        // 定得下来的那一头才是第一站:`[Inf..1]` 从 1 起(往上),`[-Inf..1]` 也从 1 起(往下)
        return Infinite(Start, out _)
            ? (Bound(End, EndClosed, goingUp: up), up)
            : (Bound(Start, StartClosed, goingUp: up), up);
    }

    public (BigInteger First, BigInteger Last, bool Up) Walk()
    {
        if (!Ascending(out var up)) return (1, 0, true);   // NaN:空(下面 `Bound` 折不出来)
        var first = Head().First;
        // 终点那一头:升序取"不大于它的最大整数"、降序取"不小于它的最小整数"(和起点那半相反);
        // 开的那端由 `Bound` 顺着方向往里挪一格。
        return (first, Bound(End, EndClosed, goingUp: !up), up);
    }

    public bool IsEmpty()
    {
        if (IsWhole()) return false;                // 整条数轴,不空
        if (!Ascending(out _)) return true;         // NaN
        if (!IsBounded()) return false;
        var (first, last, up) = Walk();
        return up ? first > last : first < last;
    }

    public BigInteger CountValue()
    {
        if (!IsBounded())
            throw new RuntimeException(
                IsWhole()
                    ? $"整条数轴数不出个数（{this} —— 它谁都在里头，但没有起手那一头）"
                    : $"无限长的区间数不出个数（{this} —— 用 Take 取前几个）",
                ErrorKind.Value);
        var (first, last, up) = Walk();
        if (up ? first > last : first < last) return BigInteger.Zero;
        return BigInteger.Abs(last - first) + 1;
    }

    public RuntimeValue FirstElement() => Element(Head().First);

    public RuntimeValue LastElement()
    {
        if (!IsBounded()) throw new RuntimeException(Messages.T("runtime.range.unbounded_has_no_last", this), ErrorKind.Value);
        return Element(Walk().Last);
    }

    public int Step() => Head().Up ? 1 : -1;

    public bool Wide()
    {
        if (Start is BigIntVal || End is BigIntVal) return true;
        if (IsWhole()) return true;                  // 连起手那一头都没有 —— 别去问 `Head`
        var (first, _) = Head();
        if (first < int.MinValue || first > int.MaxValue) return true;
        if (!IsBounded()) return false;
        var (_, last, _) = Walk();
        return last < int.MinValue || last > int.MaxValue;
    }

    public RuntimeValue Element(BigInteger v) => Wide() ? new BigIntVal(v) : IntVal.Of((int)v);

    public bool CoversValue(RuntimeValue v)
    {
        if (!BuiltinClasses.TryAsDouble(v, out _)) return false;     // 不是数,一律不盖(只要那个"是不是数")
        var dStart = BuiltinClasses.CompareNumeric(v, Start, "<");
        var dEnd = BuiltinClasses.CompareNumeric(v, End, "<");
        if (dStart is null || dEnd is null) return false;            // NaN 掺和:谁都盖不住

        // 方向对这个问法没意义:谁小谁是下端,开闭跟着各自那一端走
        // (和**精确**那条路一个口径 —— `bigint` 端点上也不许被抹平)
        var asc = BuiltinClasses.CompareNumeric(Start, End, "<") is not > 0;
        var (lo, hi) = asc ? (dStart.Value, dEnd.Value) : (dEnd.Value, dStart.Value);
        var (loOpen, hiOpen) = asc ? (!StartClosed, !EndClosed) : (!EndClosed, !StartClosed);
        return (loOpen ? lo > 0 : lo >= 0) && (hiOpen ? hi < 0 : hi <= 0);
    }

    public bool ContainsValue(RuntimeValue v)
    {
        if (!AsInteger(v, out var n)) return false;
        if (IsWhole()) return true;                                  // 整条数轴:是个整数就在里头
        if (!Ascending(out var up)) return false;                    // NaN:空
        var (first, _) = Head();
        // 无界:整条都在起手那一站的**一边**(往上就只管不小于它、往下只管不大于它)
        if (!IsBounded()) return up ? n >= first : n <= first;
        var (_, last, _) = Walk();
        var lo = BigInteger.Min(first, last);
        var hi = BigInteger.Max(first, last);
        return lo <= n && n <= hi;
    }

    public override string ToString()
        => (StartClosed ? "[" : "(") + Start + ".." + End + (EndClosed ? "]" : ")");

    // ============================================================
    //  端点 → 界
    // ============================================================

    private bool Ascending(out bool up)
    {
        up = true;
        if (Infinite(Start, out var s)) { up = s > 0; return true; }
        if (Infinite(End, out var e)) { up = e > 0; return true; }

        // 精确那一族不经过 double(`bigint` 端点 2^53 以上会被抹平 —— 见 `CompareNumeric`);
        // NaN 给 null,当空。端点一定是数(`StepRange` 那头验过),所以这里不报类型错。
        var c = BuiltinClasses.CompareNumeric(Start, End, "<");
        if (c is null) return false;
        up = c <= 0;
        return true;
    }

    internal static bool Infinite(RuntimeValue v, out int sign)
    {
        if (v is RealVal f && double.IsInfinity(f.Value)) { sign = f.Value > 0 ? 1 : -1; return true; }
        sign = 0;
        return false;
    }

    private static bool Whole(RuntimeValue v, bool closed, bool goingUp, out BigInteger bound)
    {
        if (!Point(v, goingUp, out bound)) return false;
        if (!closed) bound += goingUp ? 1 : -1;
        return true;
    }

    private static BigInteger Bound(RuntimeValue v, bool closed, bool goingUp)
    {
        if (!Whole(v, closed, goingUp, out var b))
            throw new RuntimeException(Messages.T("runtime.range.endpoint_not_boundable", v), ErrorKind.Value);
        return b;
    }

    private static bool Point(RuntimeValue v, bool goingUp, out BigInteger bound)
    {
        switch (v)
        {
            case IntVal i:
                bound = i.Value;
                return true;
            case BigIntVal b:
                bound = b.Value;
                return true;
            case RealVal f:
                if (double.IsNaN(f.Value))
                {
                    bound = BigInteger.Zero;
                    return false;
                }

                if (double.IsInfinity(f.Value))
                    throw new RuntimeException(Messages.T("runtime.range.infinite_endpoint_not_boundable", f.Value), ErrorKind.Value);

                bound = new BigInteger(goingUp ? Math.Ceiling(f.Value) : Math.Floor(f.Value));
                return true;
            case FractionVal fr:
                bound = Div(fr.Num, fr.Den, goingUp);
                return true;
            case BigFractionVal bf:
                bound = Div(bf.Num, bf.Den, goingUp);
                return true;
            default:
                throw new RuntimeException(Messages.T("runtime.range.endpoint_needs_number", v.Type), ErrorKind.Type);
        }
    }

    private static BigInteger Div(BigInteger num, BigInteger den, bool goingUp)
    {
        if (den.IsZero) throw new RuntimeException(Messages.T("runtime.range.fraction_zero_denominator"), ErrorKind.ZeroDivision);

        var q = BigInteger.DivRem(num, den, out var r);
        if (r.IsZero) return q;                             // 本来就整
        if (r.Sign > 0) return goingUp ? q + 1 : q;         // 正余数:往上取整要进一
        return goingUp ? q : q - 1;                         // 负余数(刚才往零截断过):往下取整要退一
    }

    private static bool AsInteger(RuntimeValue v, out BigInteger n)
    {
        switch (v)
        {
            case IntVal i:
                n = i.Value;
                return true;
            case BigIntVal b:
                n = b.Value;
                return true;
            case RealVal f when !double.IsNaN(f.Value) && !double.IsInfinity(f.Value)
                                 && Math.Floor(f.Value) == f.Value:
                n = new BigInteger(f.Value);
                return true;
            case FractionVal fr:
                return WholeFraction(fr.Num, fr.Den, out n);
            case BigFractionVal bf:
                return WholeFraction(bf.Num, bf.Den, out n);
            default:
                n = BigInteger.Zero;
                return false;
        }
    }

    private static bool WholeFraction(BigInteger num, BigInteger den, out BigInteger n)
    {
        n = BigInteger.Zero;
        if (den.IsZero) return false;
        if (!BigInteger.Remainder(num, den).IsZero) return false;
        n = num / den;
        return true;
    }
}
