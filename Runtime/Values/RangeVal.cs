namespace Ravel.Runtime;

using System.Numerics;

/// <summary>一个**区间**:`[1..3]` / `(3..5)` / `[1..5)` / `(1..5]`。
///
/// **端点收任何数值**(int / bigint / float / fraction / bigfraction,混着写也行)。
/// 而**元素是"区间里的整数"**:能枚举的是夹在中间的那些整数 ——
///
///     1..3   → 1 2 3          [1.5..3.5] → 2 3          [0.1..0.9] → 一个都没有
///
/// 所以端点带小数照样能 `foreach` / `Count` / `First`;而 `Clamp` 那种"把值夹进区间"
/// 读的是**端点本身**(`Start` / `End`),两回事互不冲突。
///
/// 形状和 <see cref="IntVal"/> 那一族一样:一个**不可变的值**(C# record),自己不挂成员表 ——
/// 那几条方法住在 `BuiltinClasses.Range.InstanceTable` 里,这个值借 <c>Type</c> 那条链去读
/// (基类的伪 Scope,见 <see cref="RuntimeValue.MemberScope"/>)。
///
/// 值语义是白拿的:`Object` 上那条 `==` 对**两个非 `ObjectVal` 的值**比 `Equals`
/// (见 `BuiltinClasses.Operators.cs` 的 `SameValue`),record 逐字段比 —— 于是
/// `[1..3] == [1..3]` 成立。**端点类型不同就不相等**:`[1..3] != [1.0..3.0]`
/// (一个装的是 int、一个装的是 float)。
///
/// 打印出来就是写出来那个样子(`ToString` 拼那对括号),所以 `print [1..3]` 交回原文。
///
/// **界**一律用 bigint 算(端点可能是 bigint、个数也可能超出 int);比较走
/// `TryAsDouble`(和 `<` 那批运算符一个口径)。浮点的 NaN 当**空区间**,±∞ 当场报错
/// (无穷长的区间给不出界)。</summary>
public record RangeVal(RuntimeValue Start, RuntimeValue End, bool StartClosed, bool EndClosed) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Range;

    /// <summary>元素那一边的**闭**界 `[Lo, Hi]`;空区间给 `Lo &gt; Hi`
    /// (端点里有 NaN 的也算空)。</summary>
    public (BigInteger Lo, BigInteger Hi) Bounds()
    {
        if (!Whole(Start, StartClosed, lower: true, out var lo) ||
            !Whole(End, EndClosed, lower: false, out var hi))
            return (1, 0);                                  // NaN:当成空

        return (lo, hi);
    }

    /// <summary>这个区间里有多少个整数(空的给 0)。`Count ()` 用它 ——
    /// 装得下就是 int、装不下给 bigint。</summary>
    public BigInteger CountValue()
    {
        var (lo, hi) = Bounds();
        return lo > hi ? BigInteger.Zero : hi - lo + 1;
    }

    public bool IsEmpty()
    {
        var (lo, hi) = Bounds();
        return lo > hi;
    }

    /// <summary>元素交 int 还是 bigint:端点里有 bigint,或者界超出 int 范围,就给 bigint
    /// (值有多大就多大,不静默绕圈)。</summary>
    public bool Wide()
    {
        var (lo, hi) = Bounds();
        return Start is BigIntVal || End is BigIntVal
            || lo < int.MinValue || hi > int.MaxValue;
    }

    /// <summary>界上的那个整数,按 `Wide ()` 的规矩交 int 或 bigint。</summary>
    public RuntimeValue Element(BigInteger v) => Wide() ? new BigIntVal(v) : new IntVal((int)v);

    /// <summary>这个值算不算区间里的元素 —— 得是**某个整数**且在界里:
    /// `[1..10].Contains 2` ✓、`Contains 2.0` ✓(2.0 就是整数 2)、`Contains 2.5` ✗。</summary>
    public bool ContainsValue(RuntimeValue v)
    {
        if (!AsInteger(v, out var n)) return false;
        var (lo, hi) = Bounds();
        return lo <= n && n <= hi;
    }

    public override string ToString()
        => (StartClosed ? "[" : "(") + Start + ".." + End + (EndClosed ? "]" : ")");

    // ============================================================
    //  端点 → 界
    // ============================================================

    /// <summary>端点折成**元素那一边**的界:起点取"不小于它的最小整数"、终点取"不大于它的最大整数"
    /// (端点本来就是整数就用它自己);开的那端再各挪一格。
    /// NaN 给 false(那就是空区间),无穷直接报错 —— 给不出界。</summary>
    private static bool Whole(RuntimeValue v, bool closed, bool lower, out BigInteger bound)
    {
        switch (v)
        {
            case IntVal i:
                bound = i.Value;
                break;
            case BigIntVal b:
                bound = b.Value;
                break;
            case FloatVal f:
                if (double.IsNaN(f.Value))
                {
                    bound = BigInteger.Zero;
                    return false;
                }

                if (double.IsInfinity(f.Value))
                    throw new RuntimeException($"区间的端点不能是 {f.Value} —— 无穷长的区间给不出界");

                bound = new BigInteger(lower ? Math.Ceiling(f.Value) : Math.Floor(f.Value));
                break;
            case FractionVal fr:
                bound = Div(fr.Num, fr.Den, lower);
                break;
            case BigFractionVal bf:
                bound = Div(bf.Num, bf.Den, lower);
                break;
            default:
                throw new RuntimeException($"区间的端点需要数值，得到 {v.Type}");
        }

        if (!closed) bound += lower ? 1 : -1;               // 开的那端往里挪一格
        return true;
    }

    /// <summary>`num/den` 的上取整(`lower` = false 时是下取整)。分母恒正 —— 分数构造时就
    /// 归一了(见 `FractionVal` 的说明)。C# 的整数除法往零截断(负数那边不对),所以按余数符号分一分。</summary>
    private static BigInteger Div(BigInteger num, BigInteger den, bool lower)
    {
        if (den.IsZero) throw new RuntimeException("区间的端点是分数，分母不能为零");

        var q = BigInteger.DivRem(num, den, out var r);
        if (r.IsZero) return q;                             // 本来就整
        if (r.Sign > 0) return lower ? q + 1 : q;           // 正余数:上取整要进一
        return lower ? q : q - 1;                           // 负余数(刚才往零截断过):下取整要退一
    }

    /// <summary>这个值算不算"某个整数"(`2.0` 算 2、`2.5` 不算、`6/3` 算 2、`5/2` 不算)。</summary>
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
            case FloatVal f when !double.IsNaN(f.Value) && !double.IsInfinity(f.Value)
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
