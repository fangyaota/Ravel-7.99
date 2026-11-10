namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Math")]
internal static class MathNative
{
    // ---- 常量 ----
    // 三枚 `[RavelConst]`:它们**是值不是函数**(`Math.Pi` 不是 `Math.Pi ()`),
    // 所以走常量那条路,不走 `[RavelFn]`。

    [RavelConst("Pi")] public static readonly RuntimeValue Pi = new RealVal(Math.PI);
    [RavelConst("E")] public static readonly RuntimeValue E = new RealVal(Math.E);
    [RavelConst("Tau")] public static readonly RuntimeValue Tau = new RealVal(Math.Tau);

    // ---- 三角 ----

    [RavelFn("Sin")] public static RuntimeValue Sin(RuntimeValue a) => new RealVal(Math.Sin(Num(a, "Sin")));
    [RavelFn("Cos")] public static RuntimeValue Cos(RuntimeValue a) => new RealVal(Math.Cos(Num(a, "Cos")));
    [RavelFn("Tan")] public static RuntimeValue Tan(RuntimeValue a) => new RealVal(Math.Tan(Num(a, "Tan")));
    [RavelFn("Asin")] public static RuntimeValue Asin(RuntimeValue a) => new RealVal(Math.Asin(Num(a, "Asin")));
    [RavelFn("Acos")] public static RuntimeValue Acos(RuntimeValue a) => new RealVal(Math.Acos(Num(a, "Acos")));
    [RavelFn("Atan")] public static RuntimeValue Atan(RuntimeValue a) => new RealVal(Math.Atan(Num(a, "Atan")));

    [RavelFn("Atan2")]
    public static RuntimeValue Atan2(RuntimeValue y, RuntimeValue x)
        => new RealVal(Math.Atan2(Num(y, "Atan2"), Num(x, "Atan2")));

    // ---- 双曲 ----

    [RavelFn("Sinh")] public static RuntimeValue Sinh(RuntimeValue a) => new RealVal(Math.Sinh(Num(a, "Sinh")));
    [RavelFn("Cosh")] public static RuntimeValue Cosh(RuntimeValue a) => new RealVal(Math.Cosh(Num(a, "Cosh")));
    [RavelFn("Tanh")] public static RuntimeValue Tanh(RuntimeValue a) => new RealVal(Math.Tanh(Num(a, "Tanh")));
    [RavelFn("Asinh")] public static RuntimeValue Asinh(RuntimeValue a) => new RealVal(Math.Asinh(Num(a, "Asinh")));
    [RavelFn("Acosh")] public static RuntimeValue Acosh(RuntimeValue a) => new RealVal(Math.Acosh(Num(a, "Acosh")));
    [RavelFn("Atanh")] public static RuntimeValue Atanh(RuntimeValue a) => new RealVal(Math.Atanh(Num(a, "Atanh")));

    // ---- 幂与对数 ----

    [RavelFn("Sqrt")] public static RuntimeValue Sqrt(RuntimeValue a) => new RealVal(Math.Sqrt(Num(a, "Sqrt")));
    [RavelFn("Cbrt")] public static RuntimeValue Cbrt(RuntimeValue a) => new RealVal(Math.Cbrt(Num(a, "Cbrt")));
    [RavelFn("Exp")] public static RuntimeValue Exp(RuntimeValue a) => new RealVal(Math.Exp(Num(a, "Exp")));

    [RavelFn("Log")] public static RuntimeValue Log(RuntimeValue a) => new RealVal(Math.Log(Num(a, "Log")));

    [RavelFn("Log2")] public static RuntimeValue Log2(RuntimeValue a) => new RealVal(Math.Log2(Num(a, "Log2")));
    [RavelFn("Log10")] public static RuntimeValue Log10(RuntimeValue a) => new RealVal(Math.Log10(Num(a, "Log10")));

    [RavelFn("Pow")]
    public static RuntimeValue Pow(RuntimeValue x, RuntimeValue y)
        => new RealVal(Math.Pow(Num(x, "Pow"), Num(y, "Pow")));

    [RavelFn("LogBase")]
    public static RuntimeValue LogBase(RuntimeValue x, RuntimeValue b)
        => new RealVal(Math.Log(Num(x, "LogBase"), Num(b, "LogBase")));

    [RavelFn("Hypot")]
    public static RuntimeValue Hypot(RuntimeValue x, RuntimeValue y)
        => new RealVal(HypotOf(Num(x, "hypot"), Num(y, "hypot")));

    private static double HypotOf(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        if (x < y) (x, y) = (y, x);
        return x == 0 ? 0 : x * Math.Sqrt(1 + y / x * (y / x));
    }

    // ---- 取整 ----

    [RavelFn("Floor")] public static RuntimeValue Floor(RuntimeValue a) => new RealVal(Math.Floor(Num(a, "Floor")));
    [RavelFn("Ceil")] public static RuntimeValue Ceil(RuntimeValue a) => new RealVal(Math.Ceiling(Num(a, "Ceil")));
    [RavelFn("Trunc")] public static RuntimeValue Trunc(RuntimeValue a) => new RealVal(Math.Truncate(Num(a, "Trunc")));

    [RavelFn("Round")]
    public static RuntimeValue Round(RuntimeValue a)
        => new RealVal(Math.Round(Num(a, "Round"), MidpointRounding.AwayFromZero));

    [RavelFn("RoundTo")]
    public static RuntimeValue RoundTo(RuntimeValue v, RuntimeValue digits)
    {
        var d = Int(digits, "RoundTo 的小数位数");
        // Math.Round 的 digits 只收 0..15,越界抛的是 C# 的 ArgumentOutOfRangeException
        if (d is < 0 or > 15)
            throw Fail($"RoundTo 的小数位数要在 0..15，得到 {d}");
        return new RealVal(Math.Round(Num(v, "roundTo"), d, MidpointRounding.AwayFromZero));
    }

    // ---- 保型的那几个:交回原始实参,不折成 double ----

    [RavelFn("Abs")] public static RuntimeValue AbsFn(RuntimeValue v) => Abs(v);
    [RavelFn("Sign")] public static RuntimeValue SignFn(RuntimeValue v) => Sign(v);

    [RavelFn("Min")]
    public static RuntimeValue Min(RuntimeValue a, RuntimeValue b)
        => Num(a, "min") <= Num(b, "min") ? a : b;

    [RavelFn("Max")]
    public static RuntimeValue Max(RuntimeValue a, RuntimeValue b)
        => Num(a, "max") >= Num(b, "max") ? a : b;

    [RavelFn("MinMagnitude")]
    public static RuntimeValue MinMagnitude(RuntimeValue a, RuntimeValue b)
        => Math.Abs(Num(a, "minMagnitude")) <= Math.Abs(Num(b, "minMagnitude")) ? a : b;

    [RavelFn("MaxMagnitude")]
    public static RuntimeValue MaxMagnitude(RuntimeValue a, RuntimeValue b)
        => Math.Abs(Num(a, "maxMagnitude")) >= Math.Abs(Num(b, "maxMagnitude")) ? a : b;

    [RavelFn("Clamp")]
    public static RuntimeValue Clamp(RuntimeValue x, RuntimeValue lo)
    {
        if (lo is not RangeVal rng)
            throw Fail($"Clamp 的第二个实参要是个区间（如 `Math.Clamp x [0..1]`），得到 {lo.Type}", ErrorKind.Argument);

        double v = Num(x, "clamp"), l = Num(rng.Start, "clamp"), h = Num(rng.End, "clamp");
        if (l > h) throw Fail($"Clamp 的下界 {rng.Start} 不能大于上界 {rng.End}");
        return v < l ? rng.Start : v > h ? rng.End : x;
    }

    [RavelFn("Fma")]
    public static RuntimeValue Fma(RuntimeValue a, RuntimeValue b, RuntimeValue c)
        => new RealVal(Math.FusedMultiplyAdd(Num(a, "fma"), Num(b, "fma"), Num(c, "fma")));

    // ── 下面是自己人 ──

    private static RuntimeValue Abs(RuntimeValue v) => v switch
    {
        IntVal i => Narrow(Math.Abs((long)i.Value)),
        RealVal f => new RealVal(Math.Abs(f.Value)),
        BigIntVal bi => new BigIntVal(System.Numerics.BigInteger.Abs(bi.Value)),
        FractionVal fr => new FractionVal(Math.Abs(fr.Num), fr.Den),          // 分母恒正,符号在分子上
        BigFractionVal bf => new BigFractionVal(System.Numerics.BigInteger.Abs(bf.Num), bf.Den),
        _ => throw Fail($"Abs 需要数值参数，得到 {v.Type}", ErrorKind.Type),
    };

    private static RuntimeValue Sign(RuntimeValue v) => v switch
    {
        IntVal i => IntVal.Of(Math.Sign(i.Value)),
        RealVal f => double.IsNaN(f.Value)
            ? throw Fail("Sign 的 NaN 没有符号")
            : IntVal.Of(Math.Sign(f.Value)),
        BigIntVal bi => IntVal.Of(bi.Value.Sign),
        FractionVal fr => IntVal.Of(Math.Sign(fr.Num)),
        BigFractionVal bf => IntVal.Of(bf.Num.Sign),
        _ => throw Fail($"Sign 需要数值参数，得到 {v.Type}", ErrorKind.Type),
    };
}
