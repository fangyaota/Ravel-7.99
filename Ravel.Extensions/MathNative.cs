namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>Math 模块的成员(C# 侧)—— 常量、三角、双曲、幂与对数、取整、极值。
///
/// 为什么是 C#:这些函数要落到 `System.Math` 上,写不出 Ravel 源码。
///
/// 从前这是引擎里的一格(`Runtime/Builtins/MathModule.cs`),由 `EnterModule` 在
/// **第一次 `ravel "Math"` 时**往里填——那是再早一轮的"模块成员是 C# 造的"机制,
/// 现在整台引擎只剩这一个用户,索性搬进官方扩展、走**和别处一样的插件那条路**:
/// 挂 `[RavelModule("Math")]`,收到 `using` 那一刻把成员摆好。于是
/// `Interpreter.CSharpModules` 那张表空了、删掉了,`EnterModule` 也回到"只建模块"。
///
/// 三条约定(一个字没改):
/// - **参数收任何数值**(int/float/bigint/fraction),内部按 double 算 —— 和 `<` 那批运算符
///   同一个口径(`PluginKit.Num`),免得 `sin 1` 和 `1 &lt; 2` 给出两套说法。
/// - **返回值一律是 float**,除了那几个**保型**的:`abs` / `min` / `max` / `clamp` /
///   `minMagnitude` / `maxMagnitude` 原样交出胜出的那个实参(所以 `abs -5` 还是 int、
///   `min 3 bigint 9999999999999` 还是 bigint),`sign` 给 int。
/// - **C# 会抛异常的地方自己先拦**(`sign` 收到 NaN、`clamp` 的下界大于上界、`roundTo` 的
///   位数越界):那些异常不是 RuntimeException,Ravel 的 try 接不住,会一路把程序打掉。
///
/// 没搬的 System.Math 成员(以及为什么):`DivRem` 要返回两个值,Ravel 里 `/` 和 `%` 就是;
/// `BigMul` 是 long×long 的溢出规避,这里用 bigint;`IEEERemainder` 与 `%` 是同一类需求;
/// `ScaleB` / `ILogB` / `BitIncrement` / `BitDecrement` / `CopySign` 是 IEEE **位级**操作,
/// 不是算术。</summary>
[RavelModule("Math")]
internal static class MathNative
{
    // ---- 常量 ----
    // 三枚 `[RavelConst]`:它们**是值不是函数**(`Math.Pi` 不是 `Math.Pi ()`),
    // 所以走常量那条路,不走 `[RavelFn]`。

    [RavelConst("Pi")] public static readonly RuntimeValue Pi = new FloatVal(Math.PI);
    [RavelConst("E")] public static readonly RuntimeValue E = new FloatVal(Math.E);
    [RavelConst("Tau")] public static readonly RuntimeValue Tau = new FloatVal(Math.Tau);

    // ---- 三角 ----

    [RavelFn("Sin")] public static RuntimeValue Sin(RuntimeValue a) => new FloatVal(Math.Sin(Num(a, "Sin")));
    [RavelFn("Cos")] public static RuntimeValue Cos(RuntimeValue a) => new FloatVal(Math.Cos(Num(a, "Cos")));
    [RavelFn("Tan")] public static RuntimeValue Tan(RuntimeValue a) => new FloatVal(Math.Tan(Num(a, "Tan")));
    [RavelFn("Asin")] public static RuntimeValue Asin(RuntimeValue a) => new FloatVal(Math.Asin(Num(a, "Asin")));
    [RavelFn("Acos")] public static RuntimeValue Acos(RuntimeValue a) => new FloatVal(Math.Acos(Num(a, "Acos")));
    [RavelFn("Atan")] public static RuntimeValue Atan(RuntimeValue a) => new FloatVal(Math.Atan(Num(a, "Atan")));

    /// <summary>`atan2 y x`(参数顺序同 C#:先 y 后 x)</summary>
    [RavelFn("Atan2")]
    public static RuntimeValue Atan2(RuntimeValue y, RuntimeValue x)
        => new FloatVal(Math.Atan2(Num(y, "Atan2"), Num(x, "Atan2")));

    // ---- 双曲 ----

    [RavelFn("Sinh")] public static RuntimeValue Sinh(RuntimeValue a) => new FloatVal(Math.Sinh(Num(a, "Sinh")));
    [RavelFn("Cosh")] public static RuntimeValue Cosh(RuntimeValue a) => new FloatVal(Math.Cosh(Num(a, "Cosh")));
    [RavelFn("Tanh")] public static RuntimeValue Tanh(RuntimeValue a) => new FloatVal(Math.Tanh(Num(a, "Tanh")));
    [RavelFn("Asinh")] public static RuntimeValue Asinh(RuntimeValue a) => new FloatVal(Math.Asinh(Num(a, "Asinh")));
    [RavelFn("Acosh")] public static RuntimeValue Acosh(RuntimeValue a) => new FloatVal(Math.Acosh(Num(a, "Acosh")));
    [RavelFn("Atanh")] public static RuntimeValue Atanh(RuntimeValue a) => new FloatVal(Math.Atanh(Num(a, "Atanh")));

    // ---- 幂与对数 ----

    [RavelFn("Sqrt")] public static RuntimeValue Sqrt(RuntimeValue a) => new FloatVal(Math.Sqrt(Num(a, "Sqrt")));
    [RavelFn("Cbrt")] public static RuntimeValue Cbrt(RuntimeValue a) => new FloatVal(Math.Cbrt(Num(a, "Cbrt")));
    [RavelFn("Exp")] public static RuntimeValue Exp(RuntimeValue a) => new FloatVal(Math.Exp(Num(a, "Exp")));

    /// <summary>自然对数(和 `System.Math` 一致)</summary>
    [RavelFn("Log")] public static RuntimeValue Log(RuntimeValue a) => new FloatVal(Math.Log(Num(a, "Log")));

    [RavelFn("Log2")] public static RuntimeValue Log2(RuntimeValue a) => new FloatVal(Math.Log2(Num(a, "Log2")));
    [RavelFn("Log10")] public static RuntimeValue Log10(RuntimeValue a) => new FloatVal(Math.Log10(Num(a, "Log10")));

    [RavelFn("Pow")]
    public static RuntimeValue Pow(RuntimeValue x, RuntimeValue y)
        => new FloatVal(Math.Pow(Num(x, "Pow"), Num(y, "Pow")));

    /// <summary>`logBase x b` = 以 b 为底</summary>
    [RavelFn("LogBase")]
    public static RuntimeValue LogBase(RuntimeValue x, RuntimeValue b)
        => new FloatVal(Math.Log(Num(x, "LogBase"), Num(b, "LogBase")));

    [RavelFn("Hypot")]
    public static RuntimeValue Hypot(RuntimeValue x, RuntimeValue y)
        => new FloatVal(HypotOf(Num(x, "hypot"), Num(y, "hypot")));

    /// <summary>`hypot` = sqrt (x²+y²),但先按较大的那个归一 —— 直接 x*x 在
    /// `1e200` 这种量级上会先溢出成 inf,再开方还是 inf。</summary>
    private static double HypotOf(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        if (x < y) (x, y) = (y, x);
        return x == 0 ? 0 : x * Math.Sqrt(1 + y / x * (y / x));
    }

    // ---- 取整 ----

    [RavelFn("Floor")] public static RuntimeValue Floor(RuntimeValue a) => new FloatVal(Math.Floor(Num(a, "Floor")));
    [RavelFn("Ceil")] public static RuntimeValue Ceil(RuntimeValue a) => new FloatVal(Math.Ceiling(Num(a, "Ceil")));
    [RavelFn("Trunc")] public static RuntimeValue Trunc(RuntimeValue a) => new FloatVal(Math.Truncate(Num(a, "Trunc")));

    /// <summary>四舍五入,不是 .NET 默认的"银行家舍入"(`round 2.5` → 3,不是 2)</summary>
    [RavelFn("Round")]
    public static RuntimeValue Round(RuntimeValue a)
        => new FloatVal(Math.Round(Num(a, "Round"), MidpointRounding.AwayFromZero));

    [RavelFn("RoundTo")]
    public static RuntimeValue RoundTo(RuntimeValue v, RuntimeValue digits)
    {
        var d = Int(digits, "RoundTo 的小数位数");
        // Math.Round 的 digits 只收 0..15,越界抛的是 C# 的 ArgumentOutOfRangeException
        if (d is < 0 or > 15)
            throw Fail($"RoundTo 的小数位数要在 0..15，得到 {d}");
        return new FloatVal(Math.Round(Num(v, "roundTo"), d, MidpointRounding.AwayFromZero));
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

    /// <summary>`Clamp x [下界..上界]` —— **收一个区间**(从前"三个数字"那个写法已经删掉)。
    /// 读的是那**两个端点本身**(开区间也一样夹到端点上 —— "夹进开区间里"没有最小值,
    /// 与其在这儿编一套规矩,不如老实说端点就是那两个界)。**保型照旧**:交回的仍是实参。</summary>
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
        => new FloatVal(Math.FusedMultiplyAdd(Num(a, "fma"), Num(b, "fma"), Num(c, "fma")));

    // ── 下面是自己人 ──

    /// <summary>`abs` 按实参类型给回同类型的结果 —— int 的绝对值还是 int(大数还是 bigint)。
    /// 整数那条走 `Narrow`:`abs` 的 `int.MinValue` 翻不过来,报错比静默回绕成自己强。</summary>
    private static RuntimeValue Abs(RuntimeValue v) => v switch
    {
        IntVal i => Narrow(Math.Abs((long)i.Value), "abs"),
        FloatVal f => new FloatVal(Math.Abs(f.Value)),
        BigIntVal bi => new BigIntVal(System.Numerics.BigInteger.Abs(bi.Value)),
        FractionVal fr => new FractionVal(Math.Abs(fr.Num), fr.Den),          // 分母恒正,符号在分子上
        BigFractionVal bf => new BigFractionVal(System.Numerics.BigInteger.Abs(bf.Num), bf.Den),
        _ => throw Fail($"Abs 需要数值参数，得到 {v.Type}", ErrorKind.Type),
    };

    /// <summary>`sign`:负 -1 / 零 0 / 正 1(int)。NaN 没有符号,报 Ravel 错误
    /// (C# 的 `Math.Sign(double.NaN)` 抛的是 ArithmeticException)。</summary>
    private static RuntimeValue Sign(RuntimeValue v) => v switch
    {
        IntVal i => IntVal.Of(Math.Sign(i.Value)),
        FloatVal f => double.IsNaN(f.Value)
            ? throw Fail("Sign 的 NaN 没有符号")
            : IntVal.Of(Math.Sign(f.Value)),
        BigIntVal bi => IntVal.Of(bi.Value.Sign),
        FractionVal fr => IntVal.Of(Math.Sign(fr.Num)),
        BigFractionVal bf => IntVal.Of(bf.Num.Sign),
        _ => throw Fail($"Sign 需要数值参数，得到 {v.Type}", ErrorKind.Type),
    };
}
