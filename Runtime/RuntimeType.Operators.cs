namespace Ravel.Runtime;

public partial class RuntimeType
{
    /// <summary>把二元运算符注册为名字是符号的方法（op 如 "+"、"=="）</summary>
    private static void DefineOp(RuntimeType type, string op, Func<RuntimeValue, RuntimeValue, RuntimeValue> impl)
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

    private static void RegisterOperators()
    {
        // int 运算符 —— 右操作数按"宽度"升级:float > bigint > int
        DefineOp(Int, "+", (a, b) => IntOp(a, b, "+",
            (x, y) => new IntVal(x + y), (x, y) => new FloatVal(x + y), (x, y) => new BigIntVal(x + y)));
        DefineOp(Int, "-", (a, b) => IntOp(a, b, "-",
            (x, y) => new IntVal(x - y), (x, y) => new FloatVal(x - y), (x, y) => new BigIntVal(x - y)));
        DefineOp(Int, "*", (a, b) => IntOp(a, b, "*",
            (x, y) => new IntVal(x * y), (x, y) => new FloatVal(x * y), (x, y) => new BigIntVal(x * y)));
        DefineOp(Int, "/", (a, b) => IntOp(a, b, "/",
            (x, y) => new IntVal(x / NonZero(y, "/")), (x, y) => new FloatVal(x / y),
            (x, y) => new BigIntVal(x / NonZero(y, "/"))));
        DefineOp(Int, "%", (a, b) => IntOp(a, b, "%",
            (x, y) => new IntVal(x % NonZero(y, "%")), (x, y) => new FloatVal(x % y),
            (x, y) => new BigIntVal(x % NonZero(y, "%"))));

        // float 运算符
        DefineOp(Float, "+", (a, b) => new FloatVal(AsFloat(a, "+") + AsFloat(b, "+")));
        DefineOp(Float, "-", (a, b) => new FloatVal(AsFloat(a, "-") - AsFloat(b, "-")));
        DefineOp(Float, "*", (a, b) => new FloatVal(AsFloat(a, "*") * AsFloat(b, "*")));
        DefineOp(Float, "/", (a, b) => new FloatVal(AsFloat(a, "/") / AsFloat(b, "/")));
        DefineOp(Float, "%", (a, b) => new FloatVal(AsFloat(a, "%") % AsFloat(b, "%")));

        // BigInt 运算符
        DefineOp(BigInt, "+", (a, b) => new BigIntVal(AsBigInt(a, "+") + AsBigInt(b, "+")));
        DefineOp(BigInt, "-", (a, b) => new BigIntVal(AsBigInt(a, "-") - AsBigInt(b, "-")));
        DefineOp(BigInt, "*", (a, b) => new BigIntVal(AsBigInt(a, "*") * AsBigInt(b, "*")));
        DefineOp(BigInt, "/", (a, b) => new BigIntVal(AsBigInt(a, "/") / NonZero(AsBigInt(b, "/"), "/")));
        DefineOp(BigInt, "%", (a, b) => new BigIntVal(AsBigInt(a, "%") % NonZero(AsBigInt(b, "%"), "%")));

        // Fraction 运算符
        DefineOp(Fraction, "+",
            (a, b) => FractionBinOp(a, b, (na, da, nb, db) => new FractionVal(na * db + nb * da, da * db)));
        DefineOp(Fraction, "-",
            (a, b) => FractionBinOp(a, b, (na, da, nb, db) => new FractionVal(na * db - nb * da, da * db)));
        DefineOp(Fraction, "*", (a, b) => FractionBinOp(a, b, (na, da, nb, db) => new FractionVal(na * nb, da * db)));
        DefineOp(Fraction, "/", (a, b) => FractionBinOp(a, b, (na, da, nb, db) =>
            nb != 0
                ? new FractionVal(na * db, da * nb)
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
        DefineOp(Type, "==", (a, b) => new BoolVal(((TypeVal)a).Value == Operand<TypeVal>(b, "==").Value));
        DefineOp(Type, "!=", (a, b) => new BoolVal(((TypeVal)a).Value != Operand<TypeVal>(b, "!=").Value));

        // bool 逻辑运算符
        DefineOp(Bool, "&", (a, b) => new BoolVal(((BoolVal)a).Value && Operand<BoolVal>(b, "&").Value));
        DefineOp(Bool, "|", (a, b) => new BoolVal(((BoolVal)a).Value || Operand<BoolVal>(b, "|").Value));
        DefineOp(Bool, "^", (a, b) => new BoolVal(((BoolVal)a).Value ^ Operand<BoolVal>(b, "^").Value));
        // int 位运算符
        DefineOp(Int, "&", (a, b) => new IntVal(((IntVal)a).Value & Operand<IntVal>(b, "&").Value));
        DefineOp(Int, "|", (a, b) => new IntVal(((IntVal)a).Value | Operand<IntVal>(b, "|").Value));
        DefineOp(Int, "^", (a, b) => new IntVal(((IntVal)a).Value ^ Operand<IntVal>(b, "^").Value));

        // 函数交替 |（左失败则右）→ Alternate 控制帧
        DefineOp(Function, "|", (a, b) =>
            new ControlFunction(ControlKind.Alternate, 1, RList<RuntimeValue>.Empty.Add(a).Add(b)));
    }

    /// <summary>整除的除数。不查的话 C# 会抛 DivideByZeroException,
    /// 用户看到的是英文的 "Attempted to divide by zero."</summary>
    private static int NonZero(int d, string op)
        => d != 0 ? d : throw new RuntimeException($"运算符 '{op}' 的除数为零");

    private static System.Numerics.BigInteger NonZero(System.Numerics.BigInteger d, string op)
        => !d.IsZero ? d : throw new RuntimeException($"运算符 '{op}' 的除数为零");

    private static double AsDouble(RuntimeValue v, string op) => v switch
    {
        IntVal i => i.Value,
        FloatVal f => f.Value,
        BigIntVal bi => (double)bi.Value,
        FractionVal fr => (double)fr.Num / fr.Den,
        BigFractionVal bf => (double)bf.Num / (double)bf.Den,
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数")
    };

    private static float AsFloat(RuntimeValue v, string op) => v switch
    {
        IntVal i => i.Value,
        FloatVal f => (float)f.Value,
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数")
    };

    private static System.Numerics.BigInteger AsBigInt(RuntimeValue v, string op) => v switch
    {
        IntVal i => i.Value,
        BigIntVal bi => bi.Value,
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数")
    };

    private static RuntimeValue FractionBinOp(RuntimeValue a, RuntimeValue b, Func<int, int, int, int, RuntimeValue> f)
    {
        int na = a is FractionVal fa ? fa.Num : Operand<IntVal>(a, "分数运算").Value;
        int da = a is FractionVal fa2 ? fa2.Den : 1;
        int nb = b is FractionVal fb ? fb.Num : Operand<IntVal>(b, "分数运算").Value;
        int db = b is FractionVal fb2 ? fb2.Den : 1;
        return f(na, da, nb, db);
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
