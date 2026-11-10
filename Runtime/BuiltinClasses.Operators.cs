namespace Ravel.Runtime;

/// <summary>内置运算符的**帮手**（比较、升级、移位、乘方那批实现）。
///
/// 运算符本身已经不在这个文件里注册了：改成每个类自己带一份成员工厂
/// （`Runtime/Members/*OperatorMembers.cs` 里的 `[ClassOp("…")]`），
/// 建类时 `BuiltinClasses.AttachMembers` 把那份 `Set` 接到类对象上
/// （`ClassVal.Members`）。这里留着的都是被那些成员方法（以及 `IntegerScope`）
/// 叫的纯函数。</summary>
internal static partial class BuiltinClasses
{

    /// <summary>把操作数收成想要的运行时值类型。类型不对时报 Ravel 错误——
    /// 直接写 `((IntVal)b)` 会抛 C# 的 InvalidCastException,消息里全是
    /// `Ravel.Runtime.BoolVal` 这种实现细节,`1 + true` 就长这样。
    /// 左操作数不用过这里:它由方法表保证(查的就是该类型的方法)。</summary>
    internal static T Operand<T>(RuntimeValue v, string op) where T : RuntimeValue
        => v as T ?? throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type);

    /// <summary>拼接的右操作数:字符串或字符都行(别的照旧报「不支持 X 操作数」)——
    /// 于是 `"abc" + 'd'`、`'d' + "abc"`、`'a' + 'b'` 三条都通,结果都是字符串。</summary>
    internal static string AsText(RuntimeValue v, string op) => v switch
    {
        StringVal s => s.Value,
        CharVal c => c.Value.ToString(),
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type),
    };

    /// <summary>Int 与右操作数的二元运算。右操作数按「宽度」升级:real > bigint > int——
    /// 结果类型取较宽的那个。以前只特判了 Real,于是 `1 + bigint 2` 报「运算符 '+' 不支持
    /// BigInt 操作数」,而反过来的 `bigint 2 + 1` 却正常(AsBigInt 收 int),两边不对称。</summary>
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

    /// <summary>把 int 运算的中间结果(l长期算的)收窄回 int32,超出就报错。
    ///
    /// 不检查的话 C# 的 unchecked 会**静默回绕**:`100000 * 100000` 得 1410065408、
    /// `2147483647 + 1` 得 -2147483648 —— 「算出来了但是错的」比直接崩难查得多。
    /// int 是 32 位的类型,装不下就该说;要更宽就写 bigint,消息里明说。</summary>
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

    /// <summary>移位数:一个非负的 int。负数/大得装不下/根本不是数,各报各的。</summary>
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

    /// <summary>左移(位型):高位丢出去就没了。移满 32 位及以上,32 个格子全空 —— 给 0。</summary>
    internal static int ShiftUp(int x, int k)
        => k >= WordBits ? 0 : unchecked((int)((uint)x << k));

    /// <summary>算术右移(保符号):移满 32 位及以上只剩符号本身 —— 负数给 -1,其余给 0。</summary>
    internal static int ShiftDown(int x, int k)
        => k >= WordBits ? (x < 0 ? -1 : 0) : x >> k;

    /// <summary>循环左移。`k % 32` 之后再转 —— 转 32 位就是原样一圈。</summary>
    internal static int RotateUp(int x, int k)
    {
        k %= WordBits;
        return k == 0 ? x : unchecked((int)(((uint)x << k) | ((uint)x >> (WordBits - k))));
    }

    /// <summary>循环右移(同上,方向反过来)</summary>
    internal static int RotateDown(int x, int k)
    {
        k %= WordBits;
        return k == 0 ? x : unchecked((int)(((uint)x >> k) | ((uint)x << (WordBits - k))));
    }

    internal static IntVal Narrow(long r, string what)
        => r >= int.MinValue && r <= int.MaxValue
            ? IntVal.Of((int)r)
            : throw new RuntimeException($"{what} 超出 int 范围（int 是 32 位，大数用 bigint）", ErrorKind.Value);

    /// <summary>内置运算符的注册。**已经搬空** —— 每个类型的运算符现在由它自己那份
    /// 成员工厂给出，建类时 `AttachMembers` 把 `Set` 接到类对象上（`ClassVal.Members`）。
    ///
    /// 从前这里是两百行 `DefineOp(...)`：往外置注册表里写"哪个类有哪个符号"。
    /// 现在那些写在各自的成员类里（一个符号一个 `[ClassOp]` 静态方法），
    /// 好处是"这个类有哪些成员"跟着类自己走，而不是摊在旁边一张表上。
    ///
    /// 这个函数留着是因为 `BuiltinClasses` 的装配顺序里还调它（见 `BuiltinClasses.cs`）；
    /// **要加一条运算符，别往这儿写** —— 去对应的 `XxxMembers` 里挂 `[ClassOp("…")]`。</summary>
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

    /// <summary>整除的除数。不查的话 C# 会抛 DivideByZeroException,
    /// 用户看到的是英文的 "Attempted to divide by zero."</summary>
    internal static int NonZero(int d, string op)
        => d != 0 ? d : throw new RuntimeException($"运算符 '{op}' 的除数为零", ErrorKind.ZeroDivision);

    internal static System.Numerics.BigInteger NonZero(System.Numerics.BigInteger d, string op)
        => !d.IsZero ? d : throw new RuntimeException($"运算符 '{op}' 的除数为零", ErrorKind.ZeroDivision);

    /// <summary>把数值收成 double(大数/分数也接受)。**这是"近似"那条路** ——
    /// 给那些本来就按 double 算的地方用(`Math.Sin` 之类),**不要拿它去比大小**:
    /// 2^53 以上会抹平(见 <see cref="CompareNumeric"/>)。
    /// 非数值返回 false,由调用方决定报什么:运算符说"运算符 X 不支持",Math 模块说函数名。</summary>
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

    /// <summary>数值比大小。**精确那一族(int / bigint / fraction / bigfraction)不经过 double** ——
    /// 两边都通成 `分子/分母`(bigint),交叉相乘再比,乘不溢。
    ///
    /// 从前六条比较运算符一律 `AsDouble … `,于是 `bigint 10^30 == bigint (10^30+1)` 交回
    /// **true**,而 `-` 交回 `-1` —— 同一个程序自己跟自己打架。2^53 以上全被抹平。
    ///
    /// **掺了 real 才落回 double**(它本来就是近似值,没有"更准"可言;和 `<bigint>` 比也
    /// 只能这样)。`NaN` 就和它一个口径:和谁都没法比,交 `null`,六条运算符各自读成
    /// IEEE 那个答案(`==`→false、`!=`→true、四条序→false)。
    ///
    /// 非数值由 <see cref="AsDouble"/> 报「运算符 'X' 不支持 … 操作数」。</summary>
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

    /// <summary>把"精确那一族"写成一对 bigint 分子分母(整数就是分母 1)。
    /// 调用方保证只送那四种进来。</summary>
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

    /// <summary>`Type` 那层 `==` / `!=` 的实现:<see cref="ObjectVal"/> 按**身份**比,
    /// 原子值按**值**比(`()` 是单例,异常值比消息)。
    ///
    /// **两边都不能硬转 `ObjectVal`** —— 这条运算符是沿 `parent` 往上兜底找来的
    /// (`MemberView` 查完值自己那一层,还要往它的元类走),于是 `()`(Void)、
    /// `default`(Every)、异常值这些**原子值**也走得到它:硬转会抛 C# 的
    /// InvalidCastException 漏到顶层,`print (() == ())` 从前就是这么把程序打掉的
    /// (它不是 RuntimeException,Ravel 的 `Ex.try` 也接不住)。
    ///
    /// 一边是对象一边不是 -> 报「不支持操作数」,和别的运算符一个口径,不静默给 false。</summary>
    internal static bool SameValue(RuntimeValue a, RuntimeValue b, string op) => (a, b) switch
    {
        (ObjectVal oa, ObjectVal ob) => ReferenceEquals(oa, ob),
        (not ObjectVal, not ObjectVal) => a.Equals(b),
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {b.Type} 操作数", ErrorKind.Type),
    };

    /// <summary>`<:` / `:>` 的操作数:两边都得是**类型对象**(类对象,接口也是)。
    /// 值那一边不认 —— 那是 `is` 的活。</summary>
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

    /// <summary>`is` / `isnot` 的实现:值的类型是不是(是某个类型的子类型)。
    /// 返回 bool,由调用点决定要不要取反 —— `isnot` 是同一个判据,不是两套。</summary>
    internal static bool IsA(RuntimeValue v, RuntimeValue type, string op)
        => type is ObjectVal t
            ? v.Type.IsAssignableTo(t)
            : throw new RuntimeException($"'{op}' 的右边要是一个类型，得到 {type.Type}", ErrorKind.Type);

    internal static System.Numerics.BigInteger AsBigInt(RuntimeValue v, string op) => v switch
    {
        IntVal i => i.Value,
        BigIntVal bi => bi.Value,
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type)
    };

    /// <summary>分数的分子分母**在 long 里**参与运算:两个 int 相乘再收窄会静默回绕
    /// (`fraction 100000 1 * fraction 100000 1` 从前得到 1410065408/1),
    /// 而 int×int 的积一定装得进 long,所以这一层够用。收窄交给 <see cref="MakeFraction"/>。</summary>
    internal static RuntimeValue FractionBinOp(RuntimeValue a, RuntimeValue b, Func<long, long, long, long, RuntimeValue> f)
    {
        long na = a is FractionVal fa ? fa.Num : Operand<IntVal>(a, "分数运算").Value;
        long da = a is FractionVal fa2 ? fa2.Den : 1;
        long nb = b is FractionVal fb ? fb.Num : Operand<IntVal>(b, "分数运算").Value;
        long db = b is FractionVal fb2 ? fb2.Den : 1;
        return f(na, da, nb, db);
    }

    /// <summary>收窄回 int32 时**装不下就明说**改用 bigfraction,别静默回绕。
    /// 判据和那句话说一份 —— 两个入口(见下)各摊一遍的话,改口径时必漏一处。</summary>
    internal static void FitsFraction(bool tooBig)
    {
        if (tooBig)
            throw new RuntimeException("分数运算结果超出 int 范围（fraction 的分子分母是 32 位，改用 bigfraction）", ErrorKind.Value);
    }

    /// <summary>分数运算的结果收窄回 int32。
    /// **`long` 那条是主力** —— int×int 的积一定装得进它,不必惊动 `BigInteger`。</summary>
    internal static RuntimeValue MakeFraction(long num, long den)
    {
        FitsFraction(num < int.MinValue || num > int.MaxValue || den < int.MinValue || den > int.MaxValue);
        return new FractionVal((int)num, (int)den);
    }

    /// <summary>同上,但先从 `BigInteger` 收 —— 乘方涨得太快,中间那一步 `long` 就装不下了
    /// (`(fraction 2000000000 1) ** 20` 是个 190 位的数)。</summary>
    internal static RuntimeValue MakeFraction(System.Numerics.BigInteger num, System.Numerics.BigInteger den)
    {
        FitsFraction(num < int.MinValue || num > int.MaxValue || den < int.MinValue || den > int.MaxValue);
        return new FractionVal((int)num, (int)den);
    }

    // ── 乘方 `**` 的几条实现 ──

    /// <summary>`int ** n`:指数非负就在整数里算,负的(或 real / 分数那种)交给 `Math.Pow` 出小数。</summary>
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

    /// <summary>在 `BigInteger` 里算出来再收窄回 int —— 和 `*` 那条 `Narrow((long)…)` 一个意思,
    /// 只是乘方涨得太快,`long` 那个中间层不够用。
    ///
    /// **先拦一道**:`|底| >= 2` 时指数过 31 必定装不下,当场报 —— 不拦的话
    /// `2 ** 1000000000` 会真的去算那个几十万位的数,而不是报错。
    /// (`|底| <= 1` 不用拦:`0` / `1` / `-1` 的多少次方都还是那三个。)</summary>
    internal static IntVal NarrowIntPow(int x, int e)
    {
        if (e > 31 && (x >= 2 || x <= -2))
            throw new RuntimeException($"{x} ** {e} 超出 int 范围（int 是 32 位，大数用 bigint）", ErrorKind.Value);

        var r = System.Numerics.BigInteger.Pow(x, e);
        if (r < int.MinValue || r > int.MaxValue)
            throw new RuntimeException($"{x} ** {e} 超出 int 范围（int 是 32 位，大数用 bigint）", ErrorKind.Value);
        return IntVal.Of((int)r);
    }

    /// <summary>`bigint ** n`:指数非负时是**精确**的(bigint 不会溢出);负指数同样落回 real
    /// —— 和 int 那条一个规矩。
    ///
    /// 指数本身超过 32 位就报:那是 `.NET` 的 `BigInteger.Pow` 收不下(它只收 `int`),
    /// 不是"算得出来却不给算"—— 真算出来也是个存不下的东西。</summary>
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

    /// <summary>`fraction ** n`。**负指数不用落回 real**:上下颠倒就行,分数本来就装得下
    /// (`fraction 1 2 ** -1` 是 `2/1`)—— 只有"指数不是整数"那几种才交给 double。</summary>
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

    /// <summary>`bigfraction ** n` —— 和 <see cref="FractionPow"/> 一个规矩,只是不必收窄。</summary>
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

    /// <summary>这个指数能不能当"数得清的次数"用 —— 是整数、而且装得进 int32 才有值。
    /// 交回 null 的那些(浮点、超宽的 bigint)一律落回 `Math.Pow`。</summary>
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

    /// <summary>`int` 的运算符 —— 一个整数一张成员表，某一格**第一次被读到时**才造。
    /// 名字到实现只在这一处，值的那张表（`IntegerScope`）按名字来取。</summary>
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
