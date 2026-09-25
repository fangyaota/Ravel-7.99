namespace Ravel.Runtime;

/// <summary>内置 Math 模块 —— 第二块「用 C# 写死」的模块(第一块是 System)。
///
/// 为什么是 C#:这些函数要落到 `System.Math` 上,写不出 Ravel 源码。
/// 模块**启动时就建好**,所以 `Math.sin 1` 不需要 `using` 任何东西 ——
/// `lib/math.rav` 只在它上面补 Ravel 说得清楚的那几个(`square`/`cube`/角度换算)。
///
/// 三条约定:
/// - **参数收任何数值**(int/float/bigint/fraction),内部按 double 算 —— 和 `<` 那批运算符
///   同一个口径(`BuiltinClasses.TryAsDouble`),免得 `sin 1` 和 `1 &lt; 2` 给出两套说法。
/// - **返回值一律是 float**,除了那几个**保型**的:`abs` / `min` / `max` / `clamp` /
///   `minMagnitude` / `maxMagnitude` 原样交出胜出的那个实参(所以 `abs -5` 还是 int、
///   `min 3 bigint 9999999999999` 还是 bigint),`sign` 给 int。
/// - **C# 会抛异常的地方自己先拦**(`sign` 收到 NaN、`clamp` 的下界大于上界、`roundTo` 的
///   位数越界):那些异常不是 RuntimeException,Ravel 的 try 接不住,会一路把程序打掉。
///   这也是 `randint` 的先例。
///
/// 没搬的 System.Math 成员(以及为什么):`DivRem` 要返回两个值,Ravel 里 `/` 和 `%` 就是;
/// `BigMul` 是 long×long 的溢出规避,这里用 bigint;`IEEERemainder` 与 `%` 是同一类需求;
/// `ScaleB` / `ILogB` / `BitIncrement` / `BitDecrement` / `CopySign` 是 IEEE **位级**操作,
/// 不是算术。</summary>
public partial class Interpreter
{
    private ModuleVal BuildMathModule()
    {
        var moduleType = BuiltinClasses.NewModuleClass("Math", BuiltinClasses.Ravel);
        var module = new ModuleVal(moduleType, new Scope(_global));
        var scope = module.ModuleScope;

        void Def(string name, ObjectVal type, RuntimeValue value) => scope.Define(name, type, value);
        void Fn(string name, FunctionVal fn) => Def(name, BuiltinClasses.Function, fn);

        // 收一个 double 算、给回 float —— 大半的数就是这一个形状
        void D(string name, Func<double, double> f)
            => Fn(name, FunctionVal.From(v => new FloatVal(f(Num(v, name)))));
        void D2(string name, Func<double, double, double> f)
            => Fn(name, FunctionVal.From((a, b) => new FloatVal(f(Num(a, name), Num(b, name)))));

        // ---- 常量 ----
        Def("pi", BuiltinClasses.Float, new FloatVal(Math.PI));
        Def("e", BuiltinClasses.Float, new FloatVal(Math.E));
        Def("tau", BuiltinClasses.Float, new FloatVal(Math.Tau));

        // ---- 三角 ----
        D("sin", Math.Sin);
        D("cos", Math.Cos);
        D("tan", Math.Tan);
        D("asin", Math.Asin);
        D("acos", Math.Acos);
        D("atan", Math.Atan);
        D2("atan2", Math.Atan2);          // atan2 y x(参数顺序同 C#:先 y 后 x)

        // ---- 双曲 ----
        D("sinh", Math.Sinh);
        D("cosh", Math.Cosh);
        D("tanh", Math.Tanh);
        D("asinh", Math.Asinh);
        D("acosh", Math.Acosh);
        D("atanh", Math.Atanh);

        // ---- 幂与对数 ----
        D("sqrt", Math.Sqrt);
        D("cbrt", Math.Cbrt);
        D("exp", Math.Exp);
        D("log", Math.Log);               // 自然对数(和 System.Math 一致)
        D("log2", Math.Log2);
        D("log10", Math.Log10);
        D2("pow", Math.Pow);
        D2("logBase", Math.Log);          // logBase x b = 以 b 为底
        Fn("hypot", FunctionVal.From((a, b) => new FloatVal(Hypot(Num(a, "hypot"), Num(b, "hypot")))));

        // ---- 取整 ----
        D("floor", Math.Floor);
        D("ceil", Math.Ceiling);
        D("trunc", Math.Truncate);
        // 四舍五入,不是 .NET 默认的"银行家舍入"(`round 2.5` → 3,不是 2)
        D("round", x => Math.Round(x, MidpointRounding.AwayFromZero));
        Fn("roundTo", FunctionVal.From((v, digits) =>
        {
            if (digits is not IntVal d) throw new RuntimeException($"roundTo 的小数位数需要 int，得到 {digits.Type}");
            // Math.Round 的 digits 只收 0..15,越界抛的是 C# 的 ArgumentOutOfRangeException
            if (d.Value is < 0 or > 15) throw new RuntimeException($"roundTo 的小数位数要在 0..15，得到 {d.Value}");
            return new FloatVal(Math.Round(Num(v, "roundTo"), d.Value, MidpointRounding.AwayFromZero));
        }));

        // ---- 保型的那几个:交回原始实参,不折成 double ----
        Fn("abs", FunctionVal.From(Abs));
        Fn("sign", FunctionVal.From(Sign));
        Fn("min", FunctionVal.From((a, b) => Num(a, "min") <= Num(b, "min") ? a : b));
        Fn("max", FunctionVal.From((a, b) => Num(a, "max") >= Num(b, "max") ? a : b));
        Fn("minMagnitude", FunctionVal.From((a, b) =>
            Math.Abs(Num(a, "minMagnitude")) <= Math.Abs(Num(b, "minMagnitude")) ? a : b));
        Fn("maxMagnitude", FunctionVal.From((a, b) =>
            Math.Abs(Num(a, "maxMagnitude")) >= Math.Abs(Num(b, "maxMagnitude")) ? a : b));
        Fn("clamp", FunctionVal.From((x, lo, hi) =>
        {
            double l = Num(lo, "clamp"), h = Num(hi, "clamp"), v = Num(x, "clamp");
            // Math.Clamp 在下界大于上界时抛的是 C# 的 ArgumentException
            if (l > h) throw new RuntimeException($"clamp 的下界 {lo} 不能大于上界 {hi}");
            return v < l ? lo : v > h ? hi : x;
        }));
        Fn("fma", FunctionVal.From((a, b, c) =>
            new FloatVal(Math.FusedMultiplyAdd(Num(a, "fma"), Num(b, "fma"), Num(c, "fma")))));

        return module;
    }

    /// <summary>`hypot` = sqrt (x²+y²),但先按较大的那个归一 —— 直接 x*x 在
    /// `1e200` 这种量级上会先溢出成 inf,再开方还是 inf。
    /// (System.Math 从 .NET 9 才有 `Math.Hypot`,这里的框架是 net8.0。)</summary>
    private static double Hypot(double x, double y)
    {
        x = Math.Abs(x);
        y = Math.Abs(y);
        if (x < y) (x, y) = (y, x);
        return x == 0 ? 0 : x * Math.Sqrt(1 + y / x * (y / x));
    }

    /// <summary>收成 double 算。非数值报**函数名**归属的 Ravel 错误(运算符那份说的是"运算符 X")。</summary>
    private static double Num(RuntimeValue v, string fn)
        => BuiltinClasses.TryAsDouble(v, out var d)
            ? d
            : throw new RuntimeException($"{fn} 需要数值参数，得到 {v.Type}");

    /// <summary>`abs` 按实参类型给回同类型的结果 —— int 的绝对值还是 int(大数还是 bigint)。
    /// 整数那条走 `Narrow`:`abs` 的 `int.MinValue` 翻不过来,报错比静默回绕成自己强。</summary>
    private static RuntimeValue Abs(RuntimeValue v) => v switch
    {
        IntVal i => BuiltinClasses.Narrow(Math.Abs((long)i.Value), "abs"),
        FloatVal f => new FloatVal(Math.Abs(f.Value)),
        BigIntVal bi => new BigIntVal(System.Numerics.BigInteger.Abs(bi.Value)),
        FractionVal fr => new FractionVal(Math.Abs(fr.Num), fr.Den),          // 分母恒正,符号在分子上
        BigFractionVal bf => new BigFractionVal(System.Numerics.BigInteger.Abs(bf.Num), bf.Den),
        _ => throw new RuntimeException($"abs 需要数值参数，得到 {v.Type}"),
    };

    /// <summary>`sign`:负 -1 / 零 0 / 正 1(int)。NaN 没有符号,报 Ravel 错误
    /// (C# 的 `Math.Sign(double.NaN)` 抛的是 ArithmeticException)。</summary>
    private static RuntimeValue Sign(RuntimeValue v) => v switch
    {
        IntVal i => new IntVal(Math.Sign(i.Value)),
        FloatVal f => double.IsNaN(f.Value)
            ? throw new RuntimeException("sign 的 NaN 没有符号")
            : new IntVal(Math.Sign(f.Value)),
        BigIntVal bi => new IntVal(bi.Value.Sign),
        FractionVal fr => new IntVal(Math.Sign(fr.Num)),
        BigFractionVal bf => new IntVal(bf.Num.Sign),
        _ => throw new RuntimeException($"sign 需要数值参数，得到 {v.Type}"),
    };
}
