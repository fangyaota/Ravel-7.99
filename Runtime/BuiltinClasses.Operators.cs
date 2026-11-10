namespace Ravel.Runtime;

internal static partial class BuiltinClasses
{

    internal static T Operand<T>(RuntimeValue v, string op) where T : RuntimeValue
        => v as T ?? throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type);

    internal static string AsText(RuntimeValue v, string op) => v switch
    {
        StringVal s => s.Value,
        CharVal c => c.Value.ToString(),
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type),
    };

    internal static RuntimeValue IntOp(RuntimeValue a, RuntimeValue b, string op,
        Func<int, int, RuntimeValue> ii,
        Func<int, double, RuntimeValue> id,
        Func<int, System.Numerics.BigInteger, RuntimeValue> ib)
    {
        var x = ((IntVal)a).Value;
        return b switch
        {
            RealVal f => id(x, f.Value),
            BigIntVal g => ib(x, g.Value),
            IntVal i => ii(x, i.Value),
            _ => throw new RuntimeException($"运算符 '{op}' 不支持 {b.Type} 操作数", ErrorKind.Type),
        };
    }

    // ── 移位与循环移位(int 那四条在 `IntegerScope`，bigint 的在 `BigIntMembers`)──
    //
    // `<<` / `>>` 是**位型**操作,和 `&` `|` `^` 一伙:只看那 32 个格子,不检查数值溢出 ——
    // 所以 `1 << 31` 是 -2147483648(位型就是 0x80000000),不报错。
    //   · `<<`  左移:高位丢出去就没了
    //   · `>>`  **算术**右移(保符号):`-8 >> 1` 是 -4 —— 对一个带符号的数来说,右移就是除以 2
    //   · `<<<` / `>>>`  **循环移位**:移出去的位从另一头回来,在 32 位里转圈。
    //     `>>>` 是**循环右移**,不是"无符号右移";要逻辑右移(补零)用 `lib/bits.rav` 的 `Shr`。
    //
    // 移位量收成一个**非负的 int**(bigint 能收下也收);`<<` / `>>` 超过 31 的按数学来
    // (全移出去了:`x << 32` 是 0,`x >> 32` 是 0 或 -1),循环移位取 `k % 32`。
    // 都**不**接受负数 —— 想往另一头移,换个方向的符号就行(`<<<` ↔ `>>>`)。
    private const int WordBits = 32;

    internal static int ShiftCount(RuntimeValue v, string op)
    {
        if (v is IntVal i)
        {
            if (i.Value < 0)
                throw new RuntimeException($"运算符 '{op}' 的移位数不能是负数（{i.Value}）—— 想往另一头移就换 `<<<` / `>>>`", ErrorKind.Value);
            return i.Value;
        }

        if (v is BigIntVal g && g.Value >= 0 && g.Value <= int.MaxValue) return (int)g.Value;

        throw new RuntimeException($"运算符 '{op}' 的移位数需要一个非负的 int，得到 {v.Type}", ErrorKind.Type);
    }

    internal static int ShiftUp(int x, int k)
        => k >= WordBits ? 0 : unchecked((int)((uint)x << k));

    internal static int ShiftDown(int x, int k)
        => k >= WordBits ? (x < 0 ? -1 : 0) : x >> k;

    internal static int RotateUp(int x, int k)
    {
        k %= WordBits;
        return k == 0 ? x : unchecked((int)(((uint)x << k) | ((uint)x >> (WordBits - k))));
    }

    internal static int RotateDown(int x, int k)
    {
        k %= WordBits;
        return k == 0 ? x : unchecked((int)(((uint)x >> k) | ((uint)x << (WordBits - k))));
    }

    internal static IntVal Narrow(long r, string what)
        => r >= int.MinValue && r <= int.MaxValue
            ? IntVal.Of((int)r)
            : throw new RuntimeException($"{what} 超出 int 范围（int 是 32 位，大数用 bigint）", ErrorKind.Value);

    internal static void RegisterOperators()
    {
        // 运算符都在这儿（按类型分文件，声明顺序就是 `Fields ()` 的顺序）：
        //   `RealMembers`        `+ - * / % **` + 六条比较
        //   `BigIntMembers`      `** + - * / %`、`<< >>` 与报错的 `<<< >>>` + 六条比较
        //   `FractionMembers`    `** + - * / %` + 六条比较
        //   `BigFractionMembers` `** + - * / %` + 六条比较
        //   `BoolMembers`        `== != & | ^`
        //   `StringOperatorMembers` `== != + *` 与四条序
        //   `CharOperatorMembers`   `== != < > <= >= + *`
        //   `ObjectOperatorMembers` `:`（不是符号，走 `[ClassMethod]`）`== != <: :>`
        //   `FunctionOperatorMembers` `|` `>>`
        // `int` 那一份不在成员类里：它归值类型自己那张表（`Runtime/Values/IntegerScope.cs`，
        // 实现还是叫这里的模块函数 `IntegerOperator`）。
    }

    internal static int NonZero(int d, string op)
        => d != 0 ? d : throw new RuntimeException($"运算符 '{op}' 的除数为零", ErrorKind.ZeroDivision);

    internal static System.Numerics.BigInteger NonZero(System.Numerics.BigInteger d, string op)
        => !d.IsZero ? d : throw new RuntimeException($"运算符 '{op}' 的除数为零", ErrorKind.ZeroDivision);

    internal static bool TryAsDouble(RuntimeValue v, out double d)
    {
        switch (v)
        {
            case IntVal i: d = i.Value; return true;
            case RealVal f: d = f.Value; return true;
            case BigIntVal bi: d = (double)bi.Value; return true;
            case FractionVal fr: d = (double)fr.Num / fr.Den; return true;
            case BigFractionVal bf: d = (double)bf.Num / (double)bf.Den; return true;
            default: d = 0; return false;
        }
    }

    internal static int? CompareNumeric(RuntimeValue a, RuntimeValue b, string op)
    {
        if (a is IntVal or BigIntVal or FractionVal or BigFractionVal
            && b is IntVal or BigIntVal or FractionVal or BigFractionVal)
        {
            var (na, da) = Ratio(a);
            var (nb, db) = Ratio(b);
            return (na * db).CompareTo(nb * da);
        }

        var x = AsDouble(a, op);
        var y = AsDouble(b, op);
        if (double.IsNaN(x) || double.IsNaN(y)) return null;
        return x.CompareTo(y);
    }

    internal static (System.Numerics.BigInteger Num, System.Numerics.BigInteger Den) Ratio(RuntimeValue v) => v switch
    {
        IntVal i => (i.Value, System.Numerics.BigInteger.One),
        BigIntVal b => (b.Value, System.Numerics.BigInteger.One),
        FractionVal f => (f.Num, f.Den),
        BigFractionVal bf => (bf.Num, bf.Den),
        _ => throw new RuntimeException($"{v.Type} 不是精确的数", ErrorKind.Type),
    };

    internal static double AsDouble(RuntimeValue v, string op)
        => TryAsDouble(v, out var d) ? d : throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type);

    internal static bool SameValue(RuntimeValue a, RuntimeValue b, string op) => (a, b) switch
    {
        (ObjectVal oa, ObjectVal ob) => ReferenceEquals(oa, ob),
        (not ObjectVal, not ObjectVal) => a.Equals(b),
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {b.Type} 操作数", ErrorKind.Type),
    };

    internal static (ObjectVal Left, ObjectVal Right) AsTypes(RuntimeValue a, RuntimeValue b, string op)
        => (AsType(a, op, "左"), AsType(b, op, "右"));

    internal static ObjectVal AsType(RuntimeValue v, string op, string side)
        => v as ObjectVal is { IsClass: true } t
            ? t
            : throw new RuntimeException(v is ModuleVal m
                // 模块单说:它的类对象统一是 `Ravel`,照"X 的实例"报就成了
                // 「得到 Ravel 的实例」—— 而用户写的那个名字(比如 `Math`)是个**模块**,
                // 报出来得让他认出是自己那一行。(普通值那句一个字没动。)
                ? $"'{op}' 的{side}边得是个类型，得到模块 '{m.Label}'"
                : $"'{op}' 的{side}边得是个类型，得到 {v.Type} 的实例", ErrorKind.Type);

    internal static bool Matches(Interpreter interp, RuntimeValue v, ObjectVal t)
        => v.Type.IsAssignableTo(t) || HasTrait(interp, v.Type, t);

    internal static bool MatchesType(Interpreter interp, ObjectVal sub, ObjectVal sup)
        => sub.IsAssignableTo(sup) || HasTrait(interp, sub, sup);

    internal static System.Numerics.BigInteger AsBigInt(RuntimeValue v, string op) => v switch
    {
        IntVal i => i.Value,
        BigIntVal bi => bi.Value,
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type)
    };

    internal static RuntimeValue FractionBinOp(RuntimeValue a, RuntimeValue b, Func<long, long, long, long, RuntimeValue> f)
    {
        long na = a is FractionVal fa ? fa.Num : Operand<IntVal>(a, "分数运算").Value;
        long da = a is FractionVal fa2 ? fa2.Den : 1;
        long nb = b is FractionVal fb ? fb.Num : Operand<IntVal>(b, "分数运算").Value;
        long db = b is FractionVal fb2 ? fb2.Den : 1;
        return f(na, da, nb, db);
    }

    internal static void FitsFraction(bool tooBig)
    {
        if (tooBig)
            throw new RuntimeException("分数运算结果超出 int 范围（fraction 的分子分母是 32 位，改用 bigfraction）", ErrorKind.Value);
    }

    internal static RuntimeValue MakeFraction(long num, long den)
    {
        FitsFraction(num < int.MinValue || num > int.MaxValue || den < int.MinValue || den > int.MaxValue);
        return new FractionVal((int)num, (int)den);
    }

    internal static RuntimeValue MakeFraction(System.Numerics.BigInteger num, System.Numerics.BigInteger den)
    {
        FitsFraction(num < int.MinValue || num > int.MaxValue || den < int.MinValue || den > int.MaxValue);
        return new FractionVal((int)num, (int)den);
    }

    // ── 乘方 `**` 的几条实现 ──

    internal static RuntimeValue IntPow(int x, RuntimeValue b)
    {
        switch (b)
        {
            case IntVal i when i.Value >= 0:
                return NarrowIntPow(x, i.Value);
            case BigIntVal g when g.Value.Sign >= 0:
                if (g.Value > int.MaxValue) throw PowExponentTooBig(g.Value);
                return NarrowIntPow(x, (int)g.Value);
            default:
                return new RealVal(Math.Pow(x, AsDouble(b, "**")));
        }
    }

    internal static IntVal NarrowIntPow(int x, int e)
    {
        if (e > 31 && (x >= 2 || x <= -2))
            throw new RuntimeException($"{x} ** {e} 超出 int 范围（int 是 32 位，大数用 bigint）", ErrorKind.Value);

        var r = System.Numerics.BigInteger.Pow(x, e);
        if (r < int.MinValue || r > int.MaxValue)
            throw new RuntimeException($"{x} ** {e} 超出 int 范围（int 是 32 位，大数用 bigint）", ErrorKind.Value);
        return IntVal.Of((int)r);
    }

    internal static RuntimeValue BigIntPow(System.Numerics.BigInteger x, RuntimeValue b)
    {
        switch (b)
        {
            case IntVal i when i.Value >= 0:
                return new BigIntVal(System.Numerics.BigInteger.Pow(x, i.Value));
            case BigIntVal g when g.Value.Sign >= 0:
                if (g.Value > int.MaxValue) throw PowExponentTooBig(g.Value);
                return new BigIntVal(System.Numerics.BigInteger.Pow(x, (int)g.Value));
            default:
                return new RealVal(Math.Pow((double)x, AsDouble(b, "**")));
        }
    }

    internal static RuntimeException PowExponentTooBig(System.Numerics.BigInteger e)
        => new RuntimeException($"指数太大（{e}）—— 乘方的指数要装得进 32 位", ErrorKind.Value);

    internal static RuntimeValue FractionPow(FractionVal x, RuntimeValue b)
    {
        if (PowCount(b) is not { } k)
            return new RealVal(Math.Pow((double)x.Num / x.Den, AsDouble(b, "**")));
        if (k < 0 && x.Num == 0)
            throw new RuntimeException("运算符 '**' 的除数为零（0 的负数次幂）", ErrorKind.ZeroDivision);
        // 装不下就报(和别的分数运算一个口径)。**先拦一道**:`|分子|` 或 `|分母|` 过了 2,
        // 指数一过 31 必定超出 int32,直接报 —— 免得真去算那个天文数字。
        if ((k > 31 || k < -31) && (Math.Abs((long)x.Num) >= 2 || Math.Abs((long)x.Den) >= 2))
            throw new RuntimeException(
                "运算符 '**' 的结果超出 int 范围（fraction 的分子分母是 32 位，改用 bigfraction）", ErrorKind.Value);

        return k >= 0
            ? MakeFraction(System.Numerics.BigInteger.Pow(x.Num, k), System.Numerics.BigInteger.Pow(x.Den, k))
            : MakeFraction(System.Numerics.BigInteger.Pow(x.Den, -k), System.Numerics.BigInteger.Pow(x.Num, -k));
    }

    internal static RuntimeValue BigFractionPow(BigFractionVal x, RuntimeValue b)
    {
        if (PowCount(b) is not { } k)
            return new RealVal(Math.Pow((double)x.Num / (double)x.Den, AsDouble(b, "**")));
        if (k < 0 && x.Num.IsZero)
            throw new RuntimeException("运算符 '**' 的除数为零（0 的负数次幂）", ErrorKind.ZeroDivision);

        return k >= 0
            ? new BigFractionVal(System.Numerics.BigInteger.Pow(x.Num, k), System.Numerics.BigInteger.Pow(x.Den, k))
            : new BigFractionVal(System.Numerics.BigInteger.Pow(x.Den, -k), System.Numerics.BigInteger.Pow(x.Num, -k));
    }

    internal static int? PowCount(RuntimeValue b) => b switch
    {
        IntVal i => i.Value,
        BigIntVal g when g.Value >= int.MinValue && g.Value <= int.MaxValue => (int)g.Value,
        _ => null,
    };

    internal static RuntimeValue BigFractionBinOp(RuntimeValue a, RuntimeValue b,
        Func<System.Numerics.BigInteger, System.Numerics.BigInteger, System.Numerics.BigInteger,
            System.Numerics.BigInteger, RuntimeValue> f)
    {
        var na = a is BigFractionVal bfa ? bfa.Num : a is BigIntVal bia ? bia.Value : Operand<IntVal>(a, "大分数运算").Value;
        var da = a is BigFractionVal bfa2 ? bfa2.Den : 1;
        var nb = b is BigFractionVal bfb ? bfb.Num : b is BigIntVal bib ? bib.Value : Operand<IntVal>(b, "大分数运算").Value;
        var db = b is BigFractionVal bfb2 ? bfb2.Den : 1;
        return f(na, da, nb, db);
    }

    internal static Func<RuntimeValue, RuntimeValue, RuntimeValue>? IntegerOperator(string op) => op switch
    {
        "+" => (a, b) => IntOp(a, b, "+", (x, y) => Narrow((long)x + y, $"{x} + {y}"), (x, y) => new RealVal(x + y), (x, y) => new BigIntVal(x + y)),
        "-" => (a, b) => IntOp(a, b, "-", (x, y) => Narrow((long)x - y, $"{x} - {y}"), (x, y) => new RealVal(x - y), (x, y) => new BigIntVal(x - y)),
        "*" => (a, b) => IntOp(a, b, "*", (x, y) => Narrow((long)x * y, $"{x} * {y}"), (x, y) => new RealVal(x * y), (x, y) => new BigIntVal(x * y)),
        "/" => (a, b) => IntOp(a, b, "/", (x, y) => Narrow((long)x / NonZero(y, "/"), $"{x} / {y}"), (x, y) => new RealVal(x / y), (x, y) => new BigIntVal(x / NonZero(y, "/"))),
        "%" => (a, b) => IntOp(a, b, "%", (x, y) => Narrow((long)x % NonZero(y, "%"), $"{x} % {y}"), (x, y) => new RealVal(x % y), (x, y) => new BigIntVal(x % NonZero(y, "%"))),
        "<<" => (a, b) => IntVal.Of(ShiftUp(((IntVal)a).Value, ShiftCount(b, "<<"))),
        ">>" => (a, b) => IntVal.Of(ShiftDown(((IntVal)a).Value, ShiftCount(b, ">>"))),
        "<<<" => (a, b) => IntVal.Of(RotateUp(((IntVal)a).Value, ShiftCount(b, "<<<"))),
        ">>>" => (a, b) => IntVal.Of(RotateDown(((IntVal)a).Value, ShiftCount(b, ">>>"))),
        "**" => (a, b) => IntPow(((IntVal)a).Value, b),
        "&" => (a, b) => IntVal.Of(((IntVal)a).Value & Operand<IntVal>(b, "&").Value),
        "|" => (a, b) => IntVal.Of(((IntVal)a).Value | Operand<IntVal>(b, "|").Value),
        "^" => (a, b) => IntVal.Of(((IntVal)a).Value ^ Operand<IntVal>(b, "^").Value),
        "==" => (a, b) => new BoolVal(CompareNumeric(a, b, "==") == 0),
        "!=" => (a, b) => new BoolVal(CompareNumeric(a, b, "!=") != 0),
        "<" => (a, b) => new BoolVal(CompareNumeric(a, b, "<") < 0),
        ">" => (a, b) => new BoolVal(CompareNumeric(a, b, ">") > 0),
        "<=" => (a, b) => new BoolVal(CompareNumeric(a, b, "<=") <= 0),
        ">=" => (a, b) => new BoolVal(CompareNumeric(a, b, ">=") >= 0),
        _ => null,
    };
}
