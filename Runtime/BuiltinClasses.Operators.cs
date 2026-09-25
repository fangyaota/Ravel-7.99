namespace Ravel.Runtime;

/// <summary>内置运算符的注册（端口自旧的 RuntimeType.Operators.cs）。
/// 接收者是类对象，注册就是往它的 Scope 里 DefineMethod。</summary>
internal static partial class BuiltinClasses
{

    /// <summary>把二元运算符注册为名字是符号的方法（op 如 "+"、"=="）</summary>
    private static void DefineOp(ObjectVal type, string op, Func<RuntimeValue, RuntimeValue, RuntimeValue> impl)
        => type.DefineMethod(op, impl);

    /// <summary>把操作数收成想要的运行时值类型。类型不对时报 Ravel 错误——
    /// 直接写 `((IntVal)b)` 会抛 C# 的 InvalidCastException,消息里全是
    /// `Ravel.Runtime.BoolVal` 这种实现细节,`1 + true` 就长这样。
    /// 左操作数不用过这里:它由方法表保证(查的就是该类型的方法)。</summary>
    private static T Operand<T>(RuntimeValue v, string op) where T : RuntimeValue
        => v as T ?? throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数");

    /// <summary>Int 与右操作数的二元运算。右操作数按「宽度」升级:float > bigint > int——
    /// 结果类型取较宽的那个。以前只特判了 Float,于是 `1 + bigint 2` 报「运算符 '+' 不支持
    /// BigInt 操作数」,而反过来的 `bigint 2 + 1` 却正常(AsBigInt 收 int),两边不对称。</summary>
    private static RuntimeValue IntOp(RuntimeValue a, RuntimeValue b, string op,
        Func<int, int, RuntimeValue> ii,
        Func<int, double, RuntimeValue> id,
        Func<int, System.Numerics.BigInteger, RuntimeValue> ib)
    {
        var x = ((IntVal)a).Value;
        return b switch
        {
            FloatVal f => id(x, f.Value),
            BigIntVal g => ib(x, g.Value),
            IntVal i => ii(x, i.Value),
            _ => throw new RuntimeException($"运算符 '{op}' 不支持 {b.Type} 操作数"),
        };
    }

    /// <summary>把 int 运算的中间结果(l长期算的)收窄回 int32,超出就报错。
    ///
    /// 不检查的话 C# 的 unchecked 会**静默回绕**:`100000 * 100000` 得 1410065408、
    /// `2147483647 + 1` 得 -2147483648 —— 「算出来了但是错的」比直接崩难查得多。
    /// int 是 32 位的类型,装不下就该说;要更宽就写 bigint,消息里明说。</summary>
    internal static IntVal Narrow(long r, string what)
        => r >= int.MinValue && r <= int.MaxValue
            ? new IntVal((int)r)
            : throw new RuntimeException($"{what} 超出 int 范围（int 是 32 位，大数用 bigint）");

    private static void RegisterOperators()
    {
        // int 运算符 —— 右操作数按"宽度"升级:float > bigint > int。
        // int×int 一律在 long 里算再收窄,免得 unchecked 静默回绕见 Narrow
        DefineOp(Int, "+", (a, b) => IntOp(a, b, "+",
            (x, y) => Narrow((long)x + y, $"{x} + {y}"), (x, y) => new FloatVal(x + y), (x, y) => new BigIntVal(x + y)));
        DefineOp(Int, "-", (a, b) => IntOp(a, b, "-",
            (x, y) => Narrow((long)x - y, $"{x} - {y}"), (x, y) => new FloatVal(x - y), (x, y) => new BigIntVal(x - y)));
        DefineOp(Int, "*", (a, b) => IntOp(a, b, "*",
            (x, y) => Narrow((long)x * y, $"{x} * {y}"), (x, y) => new FloatVal(x * y), (x, y) => new BigIntVal(x * y)));
        DefineOp(Int, "/", (a, b) => IntOp(a, b, "/",
            (x, y) => Narrow((long)x / NonZero(y, "/"), $"{x} / {y}"), (x, y) => new FloatVal(x / y),
            (x, y) => new BigIntVal(x / NonZero(y, "/"))));
        DefineOp(Int, "%", (a, b) => IntOp(a, b, "%",
            (x, y) => Narrow((long)x % NonZero(y, "%"), $"{x} % {y}"), (x, y) => new FloatVal(x % y),
            (x, y) => new BigIntVal(x % NonZero(y, "%"))));

        // float 运算符 —— **全程 double**。
        // 曾经这里走 `AsFloat`(转成 32 位 float 再算),于是 `Math.pi * 180` 得
        // 565.4866943359375(float32 的 π 乘出来的),而 `180 * Math.pi` 得
        // 565.4866776461628 —— 同一个数换个顺序两个答案,还和比较运算符
        // (那边一直是 double)也对不上。float 值本身就是 double,没必要经过 32 位。
        DefineOp(Float, "+", (a, b) => new FloatVal(AsDouble(a, "+") + AsDouble(b, "+")));
        DefineOp(Float, "-", (a, b) => new FloatVal(AsDouble(a, "-") - AsDouble(b, "-")));
        DefineOp(Float, "*", (a, b) => new FloatVal(AsDouble(a, "*") * AsDouble(b, "*")));
        DefineOp(Float, "/", (a, b) => new FloatVal(AsDouble(a, "/") / AsDouble(b, "/")));
        DefineOp(Float, "%", (a, b) => new FloatVal(AsDouble(a, "%") % AsDouble(b, "%")));

        // BigInt 运算符
        DefineOp(BigInt, "+", (a, b) => new BigIntVal(AsBigInt(a, "+") + AsBigInt(b, "+")));
        DefineOp(BigInt, "-", (a, b) => new BigIntVal(AsBigInt(a, "-") - AsBigInt(b, "-")));
        DefineOp(BigInt, "*", (a, b) => new BigIntVal(AsBigInt(a, "*") * AsBigInt(b, "*")));
        DefineOp(BigInt, "/", (a, b) => new BigIntVal(AsBigInt(a, "/") / NonZero(AsBigInt(b, "/"), "/")));
        DefineOp(BigInt, "%", (a, b) => new BigIntVal(AsBigInt(a, "%") % NonZero(AsBigInt(b, "%"), "%")));

        // Fraction 运算符
        DefineOp(Fraction, "+",
            (a, b) => FractionBinOp(a, b, (na, da, nb, db) => MakeFraction(na * db + nb * da, da * db)));
        DefineOp(Fraction, "-",
            (a, b) => FractionBinOp(a, b, (na, da, nb, db) => MakeFraction(na * db - nb * da, da * db)));
        DefineOp(Fraction, "*", (a, b) => FractionBinOp(a, b, (na, da, nb, db) => MakeFraction(na * nb, da * db)));
        DefineOp(Fraction, "/", (a, b) => FractionBinOp(a, b, (na, da, nb, db) =>
            nb != 0
                ? MakeFraction(na * db, da * nb)
                : throw new RuntimeException("运算符 '/' 的除数为零")));

        // BigFraction 运算符
        DefineOp(BigFraction, "+",
            (a, b) => BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * db + nb * da, da * db)));
        DefineOp(BigFraction, "-",
            (a, b) => BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * db - nb * da, da * db)));
        DefineOp(BigFraction, "*",
            (a, b) => BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * nb, da * db)));
        DefineOp(BigFraction, "/",
            (a, b) => BigFractionBinOp(a, b, (na, da, nb, db) =>
                nb.IsZero
                    ? throw new RuntimeException("运算符 '/' 的除数为零")
                    : new BigFractionVal(na * db, da * nb)));

        // 比较运算符 — 数字
        foreach (var t in new[] { Int, Float, BigInt, Fraction, BigFraction })
        {
            DefineOp(t, "==", (a, b) => new BoolVal(AsDouble(a, "==") == AsDouble(b, "==")));
            DefineOp(t, "!=", (a, b) => new BoolVal(AsDouble(a, "!=") != AsDouble(b, "!=")));
            DefineOp(t, "<", (a, b) => new BoolVal(AsDouble(a, "<") < AsDouble(b, "<")));
            DefineOp(t, ">", (a, b) => new BoolVal(AsDouble(a, ">") > AsDouble(b, ">")));
            DefineOp(t, "<=", (a, b) => new BoolVal(AsDouble(a, "<=") <= AsDouble(b, "<=")));
            DefineOp(t, ">=", (a, b) => new BoolVal(AsDouble(a, ">=") >= AsDouble(b, ">=")));
        }

        // bool 比较
        DefineOp(Bool, "==", (a, b) => new BoolVal(((BoolVal)a).Value == Operand<BoolVal>(b, "==").Value));
        DefineOp(Bool, "!=", (a, b) => new BoolVal(((BoolVal)a).Value != Operand<BoolVal>(b, "!=").Value));
        // string 比较
        DefineOp(String, "==", (a, b) => new BoolVal(((StringVal)a).Value == Operand<StringVal>(b, "==").Value));
        DefineOp(String, "!=", (a, b) => new BoolVal(((StringVal)a).Value != Operand<StringVal>(b, "!=").Value));
        // string 拼接
        DefineOp(String, "+", (a, b) => new StringVal(((StringVal)a).Value + Operand<StringVal>(b, "+").Value));
        // 类型判定 `is` / `isnot` —— 注册在 Object 上,于是**任何值**都有
        // (每个类的 parent 链都到 Object)。判据就是类型树上的 `IsAssignableTo`:
        //   `1 is int`     Integer <: Integer          ✓
        //   `1 is float`   Integer 与 Float 是兄弟       ✗
        //   `1 is object`  Integer <: ValueType <: Object ✓
        //   `int is type`  类对象是 type 的实例           ✓
        //   `default is int`  Every(底类型)特判           ✓
        // 右边必须是个类型对象,否则报 Ravel 错误(不是 InvalidCastException)。
        DefineOp(Object, "is", (a, b) => new BoolVal(IsA(a, b, "is")));
        DefineOp(Object, "isnot", (a, b) => new BoolVal(!IsA(a, b, "isnot")));

        // 类对象自己就是那个值,直接按身份比(和 ObjectVal.Equals 一致)
        DefineOp(Type, "==", (a, b) => new BoolVal((ObjectVal)a == Operand<ObjectVal>(b, "==")));
        DefineOp(Type, "!=", (a, b) => new BoolVal((ObjectVal)a != Operand<ObjectVal>(b, "!=")));

        // bool 逻辑运算符
        DefineOp(Bool, "&", (a, b) => new BoolVal(((BoolVal)a).Value && Operand<BoolVal>(b, "&").Value));
        DefineOp(Bool, "|", (a, b) => new BoolVal(((BoolVal)a).Value || Operand<BoolVal>(b, "|").Value));
        DefineOp(Bool, "^", (a, b) => new BoolVal(((BoolVal)a).Value ^ Operand<BoolVal>(b, "^").Value));
        // int 位运算符
        DefineOp(Int, "&", (a, b) => new IntVal(((IntVal)a).Value & Operand<IntVal>(b, "&").Value));
        DefineOp(Int, "|", (a, b) => new IntVal(((IntVal)a).Value | Operand<IntVal>(b, "|").Value));
        DefineOp(Int, "^", (a, b) => new IntVal(((IntVal)a).Value ^ Operand<IntVal>(b, "^").Value));

        // 函数交替 |（前一个不收这个参数就试下一个）→ Alternate 控制帧。
        // 左边已经是交替时**摊平**成一个分支列表:`x | y | z` 解析成 `(x | y) | z`,
        // 嵌套着写的话内层是在外层 try 之外才抛 TypeMismatchException 的
        // (CallInto 只推帧,不调用),外层那个 catch 早返回了 —— 第三个分支永远试不到。
        DefineOp(Function, "|", (a, b) =>
        {
            var branches = a is ControlFunction { Kind: ControlKind.Alternate } chain
                ? chain.Args.Add(b)
                : RList<RuntimeValue>.Empty.Add(a).Add(b);
            return new ControlFunction(ControlKind.Alternate, 1, branches);
        });
    }

    /// <summary>整除的除数。不查的话 C# 会抛 DivideByZeroException,
    /// 用户看到的是英文的 "Attempted to divide by zero."</summary>
    private static int NonZero(int d, string op)
        => d != 0 ? d : throw new RuntimeException($"运算符 '{op}' 的除数为零");

    private static System.Numerics.BigInteger NonZero(System.Numerics.BigInteger d, string op)
        => !d.IsZero ? d : throw new RuntimeException($"运算符 '{op}' 的除数为零");

    /// <summary>把数值收成 double(大数/分数也接受 —— 和 `<` 那批运算符同一个口径)。
    /// 非数值返回 false,由调用方决定报什么:运算符说"运算符 X 不支持",Math 模块说函数名。</summary>
    internal static bool TryAsDouble(RuntimeValue v, out double d)
    {
        switch (v)
        {
            case IntVal i: d = i.Value; return true;
            case FloatVal f: d = f.Value; return true;
            case BigIntVal bi: d = (double)bi.Value; return true;
            case FractionVal fr: d = (double)fr.Num / fr.Den; return true;
            case BigFractionVal bf: d = (double)bf.Num / (double)bf.Den; return true;
            default: d = 0; return false;
        }
    }

    private static double AsDouble(RuntimeValue v, string op)
        => TryAsDouble(v, out var d) ? d : throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数");

    /// <summary>`is` / `isnot` 的实现:值的类型是不是(是某个类型的子类型)。
    /// 返回 bool,由调用点决定要不要取反 —— `isnot` 是同一个判据,不是两套。</summary>
    private static bool IsA(RuntimeValue v, RuntimeValue type, string op)
        => type is ObjectVal t
            ? v.Type.IsAssignableTo(t)
            : throw new RuntimeException($"'{op}' 的右边要是一个类型，得到 {type.Type}");

    private static System.Numerics.BigInteger AsBigInt(RuntimeValue v, string op) => v switch
    {
        IntVal i => i.Value,
        BigIntVal bi => bi.Value,
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数")
    };

    /// <summary>分数的分子分母**在 long 里**参与运算:两个 int 相乘再收窄会静默回绕
    /// (`fraction 100000 1 * fraction 100000 1` 从前得到 1410065408/1),
    /// 而 int×int 的积一定装得进 long,所以这一层够用。收窄交给 <see cref="MakeFraction"/>。</summary>
    private static RuntimeValue FractionBinOp(RuntimeValue a, RuntimeValue b, Func<long, long, long, long, RuntimeValue> f)
    {
        long na = a is FractionVal fa ? fa.Num : Operand<IntVal>(a, "分数运算").Value;
        long da = a is FractionVal fa2 ? fa2.Den : 1;
        long nb = b is FractionVal fb ? fb.Num : Operand<IntVal>(b, "分数运算").Value;
        long db = b is FractionVal fb2 ? fb2.Den : 1;
        return f(na, da, nb, db);
    }

    /// <summary>分数运算的结果收窄回 int32。fraction 是 32 位的类型,装不下就明说改用 bigfraction,
    /// 别静默回绕。</summary>
    private static RuntimeValue MakeFraction(long num, long den)
    {
        if (num < int.MinValue || num > int.MaxValue || den < int.MinValue || den > int.MaxValue)
            throw new RuntimeException("分数运算结果超出 int 范围（fraction 的分子分母是 32 位，改用 bigfraction）");
        return new FractionVal((int)num, (int)den);
    }

    private static RuntimeValue BigFractionBinOp(RuntimeValue a, RuntimeValue b,
        Func<System.Numerics.BigInteger, System.Numerics.BigInteger, System.Numerics.BigInteger,
            System.Numerics.BigInteger, RuntimeValue> f)
    {
        var na = a is BigFractionVal bfa ? bfa.Num : a is BigIntVal bia ? bia.Value : Operand<IntVal>(a, "大分数运算").Value;
        var da = a is BigFractionVal bfa2 ? bfa2.Den : 1;
        var nb = b is BigFractionVal bfb ? bfb.Num : b is BigIntVal bib ? bib.Value : Operand<IntVal>(b, "大分数运算").Value;
        var db = b is BigFractionVal bfb2 ? bfb2.Den : 1;
        return f(na, da, nb, db);
    }
}
