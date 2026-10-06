namespace Ravel.Runtime;

/// <summary>内置运算符的注册（端口自旧的 RuntimeType.Operators.cs）。
/// 接收者是类对象，注册就是往它的 Scope 里 DefineMethod。</summary>
internal static partial class BuiltinClasses
{

    /// <summary>把二元运算符注册为名字是符号的方法（op 如 "+"、"=="）。
    ///
    /// 落点是**实例表**，和 `BindOperator` 找的地方正好对上（它也查 `MemberScope`）——
    /// `1 + 2` 找 `Integer` 那张、`C1 == C2` 找 `Type` 那张、`c1 == c2` 找 `C` 那张，各是各的。
    /// 加一条：这批"任何值都有"的（`is`/`isnot`/`<:`/`:>`/`==`/`!=`）得挂 `Object`
    /// **和自指的 `Every`/`Any`**，见下面那段循环。</summary>
    private static void DefineOp(ObjectVal type, string op, Func<RuntimeValue, RuntimeValue, RuntimeValue> impl)
        => type.DefineMethod(op, impl);

    /// <summary>把操作数收成想要的运行时值类型。类型不对时报 Ravel 错误——
    /// 直接写 `((IntVal)b)` 会抛 C# 的 InvalidCastException,消息里全是
    /// `Ravel.Runtime.BoolVal` 这种实现细节,`1 + true` 就长这样。
    /// 左操作数不用过这里:它由方法表保证(查的就是该类型的方法)。</summary>
    private static T Operand<T>(RuntimeValue v, string op) where T : RuntimeValue
        => v as T ?? throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type);

    /// <summary>拼接的右操作数:字符串或字符都行(别的照旧报「不支持 X 操作数」)——
    /// 于是 `"abc" + 'd'`、`'d' + "abc"`、`'a' + 'b'` 三条都通,结果都是字符串。</summary>
    private static string AsText(RuntimeValue v, string op) => v switch
    {
        StringVal s => s.Value,
        CharVal c => c.Value.ToString(),
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type),
    };

    /// <summary>Int 与右操作数的二元运算。右操作数按「宽度」升级:real > bigint > int——
    /// 结果类型取较宽的那个。以前只特判了 Real,于是 `1 + bigint 2` 报「运算符 '+' 不支持
    /// BigInt 操作数」,而反过来的 `bigint 2 + 1` 却正常(AsBigInt 收 int),两边不对称。</summary>
    private static RuntimeValue IntOp(RuntimeValue a, RuntimeValue b, string op,
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
    // ── 移位与循环移位(见 RegisterOperators 里注册的那四条)──
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
    private static int ShiftCount(RuntimeValue v, string op)
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
    private static int ShiftUp(int x, int k)
        => k >= WordBits ? 0 : unchecked((int)((uint)x << k));

    /// <summary>算术右移(保符号):移满 32 位及以上只剩符号本身 —— 负数给 -1,其余给 0。</summary>
    private static int ShiftDown(int x, int k)
        => k >= WordBits ? (x < 0 ? -1 : 0) : x >> k;

    /// <summary>循环左移。`k % 32` 之后再转 —— 转 32 位就是原样一圈。</summary>
    private static int RotateUp(int x, int k)
    {
        k %= WordBits;
        return k == 0 ? x : unchecked((int)(((uint)x << k) | ((uint)x >> (WordBits - k))));
    }

    /// <summary>循环右移(同上,方向反过来)</summary>
    private static int RotateDown(int x, int k)
    {
        k %= WordBits;
        return k == 0 ? x : unchecked((int)(((uint)x >> k) | ((uint)x << (WordBits - k))));
    }

    internal static IntVal Narrow(long r, string what)
        => r >= int.MinValue && r <= int.MaxValue
            ? IntVal.Of((int)r)
            : throw new RuntimeException($"{what} 超出 int 范围（int 是 32 位，大数用 bigint）", ErrorKind.Value);

    private static void RegisterOperators()
    {
        // int 运算符 —— 右操作数按"宽度"升级:real > bigint > int。
        // int×int 一律在 long 里算再收窄,免得 unchecked 静默回绕见 Narrow
        DefineOp(Int, "+", (a, b) => IntOp(a, b, "+",
            (x, y) => Narrow((long)x + y, $"{x} + {y}"), (x, y) => new RealVal(x + y), (x, y) => new BigIntVal(x + y)));
        DefineOp(Int, "-", (a, b) => IntOp(a, b, "-",
            (x, y) => Narrow((long)x - y, $"{x} - {y}"), (x, y) => new RealVal(x - y), (x, y) => new BigIntVal(x - y)));
        DefineOp(Int, "*", (a, b) => IntOp(a, b, "*",
            (x, y) => Narrow((long)x * y, $"{x} * {y}"), (x, y) => new RealVal(x * y), (x, y) => new BigIntVal(x * y)));
        DefineOp(Int, "/", (a, b) => IntOp(a, b, "/",
            (x, y) => Narrow((long)x / NonZero(y, "/"), $"{x} / {y}"), (x, y) => new RealVal(x / y),
            (x, y) => new BigIntVal(x / NonZero(y, "/"))));
        DefineOp(Int, "%", (a, b) => IntOp(a, b, "%",
            (x, y) => Narrow((long)x % NonZero(y, "%"), $"{x} % {y}"), (x, y) => new RealVal(x % y),
            (x, y) => new BigIntVal(x % NonZero(y, "%"))));

        // 移位 / 循环移位(四条,`int` 上;语义见上面那一段)。
        // **不走 IntOp**:右边是"移多少位",不是参与运算的另一个值 —— 不该按宽度升级。
        DefineOp(Int, "<<", (a, b) => IntVal.Of(ShiftUp(((IntVal)a).Value, ShiftCount(b, "<<"))));
        DefineOp(Int, ">>", (a, b) => IntVal.Of(ShiftDown(((IntVal)a).Value, ShiftCount(b, ">>"))));
        DefineOp(Int, "<<<", (a, b) => IntVal.Of(RotateUp(((IntVal)a).Value, ShiftCount(b, "<<<"))));
        DefineOp(Int, ">>>", (a, b) => IntVal.Of(RotateDown(((IntVal)a).Value, ShiftCount(b, ">>>"))));

        // real 运算符 —— **全程 double**。
        // 曾经这里走 `AsReal`(转成 32 位 real 再算),于是 `Math.pi * 180` 得
        // 565.4866943359375(float32 的 π 乘出来的),而 `180 * Math.pi` 得
        // 565.4866776461628 —— 同一个数换个顺序两个答案,还和比较运算符
        // (那边一直是 double)也对不上。real 值本身就是 double,没必要经过 32 位。
        DefineOp(Real, "+", (a, b) => new RealVal(AsDouble(a, "+") + AsDouble(b, "+")));
        DefineOp(Real, "-", (a, b) => new RealVal(AsDouble(a, "-") - AsDouble(b, "-")));
        DefineOp(Real, "*", (a, b) => new RealVal(AsDouble(a, "*") * AsDouble(b, "*")));
        DefineOp(Real, "/", (a, b) => new RealVal(AsDouble(a, "/") / AsDouble(b, "/")));
        DefineOp(Real, "%", (a, b) => new RealVal(AsDouble(a, "%") % AsDouble(b, "%")));

        // ── 乘方 `**` ──
        //
        // 两条规矩,都照这门语言已有的口径定:
        //
        //   * **左边是什么类型就在那个类型里算** —— `int ** int` 还是 int(装不下照旧报
        //     「大数用 bigint」,和 `*` 一样,不悄悄换成 bigint);掺了 real 就全程 double。
        //   * **整数的负次幂交回 real**(`2 ** -1` = 0.5)。整数装不下 1/2,而"报错让人
        //     自己去转 real"太不划算 —— Python 也是这么干的。**分数不受这条影响**:
        //     负次幂只是上下颠倒,分数本来就装得下,所以还是分数。
        //
        // 优先级和结合性在解析器那头(`Parser.Expressions` 的 `ParsePower`):右结合、
        // 而且比一元负号紧 —— `-2 ** 2` 是 `-(2 ** 2)` = -4。
        DefineOp(Int, "**", (a, b) => IntPow(((IntVal)a).Value, b));
        DefineOp(Real, "**", (a, b) => new RealVal(Math.Pow(AsDouble(a, "**"), AsDouble(b, "**"))));
        DefineOp(BigInt, "**", (a, b) => BigIntPow(AsBigInt(a, "**"), b));
        DefineOp(Fraction, "**", (a, b) => FractionPow(Operand<FractionVal>(a, "**"), b));
        DefineOp(BigFraction, "**", (a, b) => BigFractionPow(Operand<BigFractionVal>(a, "**"), b));

        // BigInt 运算符
        DefineOp(BigInt, "+", (a, b) => new BigIntVal(AsBigInt(a, "+") + AsBigInt(b, "+")));
        DefineOp(BigInt, "-", (a, b) => new BigIntVal(AsBigInt(a, "-") - AsBigInt(b, "-")));
        DefineOp(BigInt, "*", (a, b) => new BigIntVal(AsBigInt(a, "*") * AsBigInt(b, "*")));
        DefineOp(BigInt, "/", (a, b) => new BigIntVal(AsBigInt(a, "/") / NonZero(AsBigInt(b, "/"), "/")));
        DefineOp(BigInt, "%", (a, b) => new BigIntVal(AsBigInt(a, "%") % NonZero(AsBigInt(b, "%"), "%")));
        // 移位:bigint 没有固定宽度,`<<` 就是乘 2^k,**没有丢位这回事**(要多少位有多少位)。
        // 循环移位对 bigint **不成立** —— 转圈得先有个圈,所以那两条明确报错(而不是偷偷当普通移位)。
        DefineOp(BigInt, "<<", (a, b) => new BigIntVal(AsBigInt(a, "<<") << ShiftCount(b, "<<")));
        DefineOp(BigInt, ">>", (a, b) => new BigIntVal(AsBigInt(a, ">>") >> ShiftCount(b, ">>")));
        DefineOp(BigInt, "<<<", (a, b) =>
            throw new RuntimeException("运算符 '<<<' 不支持 bigint：循环移位要在**固定宽度**上转，而 bigint 没有宽度（要移就用 '<<'）", ErrorKind.Type));
        DefineOp(BigInt, ">>>", (a, b) =>
            throw new RuntimeException("运算符 '>>>' 不支持 bigint：循环移位要在**固定宽度**上转，而 bigint 没有宽度（要移就用 '>>'）", ErrorKind.Type));

        // Fraction 运算符
        DefineOp(Fraction, "+",
            (a, b) => FractionBinOp(a, b, (na, da, nb, db) => MakeFraction(na * db + nb * da, da * db)));
        DefineOp(Fraction, "-",
            (a, b) => FractionBinOp(a, b, (na, da, nb, db) => MakeFraction(na * db - nb * da, da * db)));
        DefineOp(Fraction, "*", (a, b) => FractionBinOp(a, b, (na, da, nb, db) => MakeFraction(na * nb, da * db)));
        DefineOp(Fraction, "/", (a, b) => FractionBinOp(a, b, (na, da, nb, db) =>
            nb != 0
                ? MakeFraction(na * db, da * nb)
                : throw new RuntimeException("运算符 '/' 的除数为零", ErrorKind.ZeroDivision)));
        // `%` 和 Int 那条**一个口径** —— 余数取**截断**那个(`-7 % 3` 是 -1,不是 Python 的 2):
        //     a % b = a - b * trunc(a / b)
        // 这里的 `(na * db) / (da * nb)` 就是 `trunc(a / b)`(C# 的整数除法朝零截断)。
        DefineOp(Fraction, "%", (a, b) => FractionBinOp(a, b, (na, da, nb, db) =>
            nb != 0
                ? MakeFraction(na * db - ((na * db) / (da * nb)) * (nb * da), da * db)
                : throw new RuntimeException("运算符 '%' 的除数为零", ErrorKind.ZeroDivision)));

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
                    ? throw new RuntimeException("运算符 '/' 的除数为零", ErrorKind.ZeroDivision)
                    : new BigFractionVal(na * db, da * nb)));
        DefineOp(BigFraction, "%",
            (a, b) => BigFractionBinOp(a, b, (na, da, nb, db) =>
                nb.IsZero
                    ? throw new RuntimeException("运算符 '%' 的除数为零", ErrorKind.ZeroDivision)
                    : new BigFractionVal(na * db - ((na * db) / (da * nb)) * (nb * da), da * db)));

        // 比较运算符 — 数字。**精确那一族不经过 double**(见 `CompareNumeric`);
        // `int?` 和 0 比:`NaN` 那条路给 null,于是 `null == 0` 是 false、`null != 0` 是 true、
        // 四条序全是 false —— 正好是 IEEE 要的那组答案。
        foreach (var t in new[] { Int, Real, BigInt, Fraction, BigFraction })
        {
            DefineOp(t, "==", (a, b) => new BoolVal(CompareNumeric(a, b, "==") == 0));
            DefineOp(t, "!=", (a, b) => new BoolVal(CompareNumeric(a, b, "!=") != 0));
            DefineOp(t, "<", (a, b) => new BoolVal(CompareNumeric(a, b, "<") < 0));
            DefineOp(t, ">", (a, b) => new BoolVal(CompareNumeric(a, b, ">") > 0));
            DefineOp(t, "<=", (a, b) => new BoolVal(CompareNumeric(a, b, "<=") <= 0));
            DefineOp(t, ">=", (a, b) => new BoolVal(CompareNumeric(a, b, ">=") >= 0));
        }

        // bool 比较
        DefineOp(Bool, "==", (a, b) => new BoolVal(((BoolVal)a).Value == Operand<BoolVal>(b, "==").Value));
        DefineOp(Bool, "!=", (a, b) => new BoolVal(((BoolVal)a).Value != Operand<BoolVal>(b, "!=").Value));
        // string 比较
        DefineOp(String, "==", (a, b) => new BoolVal(((StringVal)a).Value == Operand<StringVal>(b, "==").Value));
        DefineOp(String, "!=", (a, b) => new BoolVal(((StringVal)a).Value != Operand<StringVal>(b, "!=").Value));
        // string 拼接(**也收字符**:`"abc" + 'd'` 和 `'d' + "abc"` 都是字符串)
        DefineOp(String, "+", (a, b) => new StringVal(((StringVal)a).Value + AsText(b, "+")));
        // 重复:`"-" * 10` → 十个短横。0 或负数给空串(负数由 IntOp 那套先把类型卡住)
        DefineOp(String, "*", (a, b) => new StringVal(string.Concat(
            Enumerable.Repeat(((StringVal)a).Value, Math.Max(0, Operand<IntVal>(b, "*").Value)))));

        // ══ 字符 ══(`Char <: ValueType`,和数、字符串一个待遇:能比、能拼、能重复)
        DefineOp(Char, "==", (a, b) => new BoolVal(((CharVal)a).Value == Operand<CharVal>(b, "==").Value));
        DefineOp(Char, "!=", (a, b) => new BoolVal(((CharVal)a).Value != Operand<CharVal>(b, "!=").Value));
        DefineOp(Char, "<", (a, b) => new BoolVal(((CharVal)a).Value < Operand<CharVal>(b, "<").Value));
        DefineOp(Char, ">", (a, b) => new BoolVal(((CharVal)a).Value > Operand<CharVal>(b, ">").Value));
        DefineOp(Char, "<=", (a, b) => new BoolVal(((CharVal)a).Value <= Operand<CharVal>(b, "<=").Value));
        DefineOp(Char, ">=", (a, b) => new BoolVal(((CharVal)a).Value >= Operand<CharVal>(b, ">=").Value));
        // 拼接:两个字符拼成字符串(`'a' + 'b'` → `"ab"`),字符拼字符串同理
        DefineOp(Char, "+", (a, b) => new StringVal(((CharVal)a).Value + AsText(b, "+")));
        // 重复:`'-' * 10` → 十个短横
        DefineOp(Char, "*", (a, b) => new StringVal(string.Concat(
            Enumerable.Repeat(((CharVal)a).Value, Math.Max(0, Operand<IntVal>(b, "*").Value)))));
        // 字符串的序:**按序数比**(ordinal,和 C# 的 `string.CompareOrdinal` 一个口径),
        // 不跟当前区域设置走 —— 后者会让同一段程序换台机器就换个结果。
        // 有了它,`Min` / `Max` / `Sort` 那批序列方法对字符串也成立。
        DefineOp(String, "<", (a, b) => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, Operand<StringVal>(b, "<").Value) < 0));
        DefineOp(String, ">", (a, b) => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, Operand<StringVal>(b, ">").Value) > 0));
        DefineOp(String, "<=", (a, b) => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, Operand<StringVal>(b, "<=").Value) <= 0));
        DefineOp(String, ">=", (a, b) => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, Operand<StringVal>(b, ">=").Value) >= 0));
        // 类型判定 `x: T`。**得注册**(`BindOperator` 在求值器那条特判**之前**就查表了,
        // 表里没有就当场报「运算符 ':' 不支持 X 操作数」)—— 但注册的只是"表里有这个名字",
        // 真正算的是 `Interpreter.Binary` 那一支(它还要看接口实现 `HasTrait`)。
        // **不给自定义**:`OperatorSymbols.All` 里没有它,`ParseOperatorDefinition` 也拦了一道。
        //
        // 判据是类型树上的 `IsAssignableTo`(帮手 `IsA` 还在,那条特判用的就是它):
        //   `1: int`     Integer <: Integer          ✓
        //   `1: real`   Integer 与 Real 是兄弟       ✗
        //   `1: object`  Integer <: ValueType <: Object ✓
        //   `int: type`  类对象是 type 的实例           ✓
        //   `default: int`  Every(底类型)特判           ✓
        // 右边必须是个类型对象,否则报 Ravel 错误(不是 InvalidCastException)。
        //
        // **从前这里挂着 `is` / `isnot` 两个成员**(那时它们是词形运算符,于是 `1.is` /
        // `is.int` 两种节形式和 `a.+` 完全对称)。`:` 是标点,取成员那条路就没了 ——
        // 要当函数用写节:`:.int`。
        DefineOp(Object, ":", (a, b) => new BoolVal(IsA(a, b, ":")));
        DefineOp(Object, "==", (a, b) => new BoolVal(SameValue(a, b, "==")));
        DefineOp(Object, "!=", (a, b) => new BoolVal(!SameValue(a, b, "!=")));

        // 类型**之间**:`A <: B`(A 是不是 B 的子类型)/ `A :> B`(父类型)。
        // 和 `is` 分工清楚:`is` 收**值**("这个值是不是这个类型"),这一对两边都得是**类型对象**。
        // 也注册在 Object 上,是为了报错能说人话(`1 <: int` 得到的是「左边得是个类型」,
        // 而不是「类型 Integer 不支持运算符」)。接口那一半("这个类在当前作用域里算不算那个接口")
        // 要问作用域,在求值器里补 —— 这里只给名义答案。
        DefineOp(Object, "<:", (a, b) =>
        {
            var (x, y) = AsTypes(a, b, "<:");
            return new BoolVal(x.IsAssignableTo(y));
        });
        DefineOp(Object, ":>", (a, b) =>
        {
            var (x, y) = AsTypes(a, b, ":>");
            return new BoolVal(y.IsAssignableTo(x));
        });

        // `==` / `!=`:对象按身份比(`SameValue`;类对象也是对象,所以"类型之间"和
        // "实例之间"用的**同一个实现**,只是各自的表不同)。挂在 Object 上而不是 Type 上
        // —— 运算符查的是 `MemberScope`(自己那层 + 沿 `parent` 往上),而任何值往上都到
        // Object;挂在 Type 上只有**类对象**够得着(它从元类起往上走会经过 Type),
        // 普通实例、属性、`()` 这些就都摸不到了。

        // bool 逻辑运算符
        DefineOp(Bool, "&", (a, b) => new BoolVal(((BoolVal)a).Value && Operand<BoolVal>(b, "&").Value));
        DefineOp(Bool, "|", (a, b) => new BoolVal(((BoolVal)a).Value || Operand<BoolVal>(b, "|").Value));
        DefineOp(Bool, "^", (a, b) => new BoolVal(((BoolVal)a).Value ^ Operand<BoolVal>(b, "^").Value));
        // int 位运算符
        DefineOp(Int, "&", (a, b) => IntVal.Of(((IntVal)a).Value & Operand<IntVal>(b, "&").Value));
        DefineOp(Int, "|", (a, b) => IntVal.Of(((IntVal)a).Value | Operand<IntVal>(b, "|").Value));
        DefineOp(Int, "^", (a, b) => IntVal.Of(((IntVal)a).Value ^ Operand<IntVal>(b, "^").Value));

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

        // 函数组合 `>>` —— **和移位同名,但落在不同的类型上**(`8 >> 1` 还是算术右移,
        // 走的是 `Integer` 那张表)。这和 `|` 一个路子:整数上是位或、函数上是交替。
        //
        // 方向**照着 `|>` 来**:`f >> g` = 先 f 后 g,于是 `x |> (f >> g)` ≡ `x |> f |> g`。
        // 交回一个控制帧(内置运算符的体是纯 C#,拿不到解释器 —— 要调 Ravel 函数只能这么走),
        // 语义见 `Interpreter.Control` 的 `StepThen`。
        DefineOp(Function, ">>", (a, b) =>
            new ControlFunction(ControlKind.Then, 1, RList<RuntimeValue>.Empty.Add(a).Add(b)));
    }

    /// <summary>整除的除数。不查的话 C# 会抛 DivideByZeroException,
    /// 用户看到的是英文的 "Attempted to divide by zero."</summary>
    private static int NonZero(int d, string op)
        => d != 0 ? d : throw new RuntimeException($"运算符 '{op}' 的除数为零", ErrorKind.ZeroDivision);

    private static System.Numerics.BigInteger NonZero(System.Numerics.BigInteger d, string op)
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
    private static (System.Numerics.BigInteger Num, System.Numerics.BigInteger Den) Ratio(RuntimeValue v) => v switch
    {
        IntVal i => (i.Value, System.Numerics.BigInteger.One),
        BigIntVal b => (b.Value, System.Numerics.BigInteger.One),
        FractionVal f => (f.Num, f.Den),
        BigFractionVal bf => (bf.Num, bf.Den),
        _ => throw new RuntimeException($"{v.Type} 不是精确的数", ErrorKind.Type),
    };

    private static double AsDouble(RuntimeValue v, string op)
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
    private static bool SameValue(RuntimeValue a, RuntimeValue b, string op) => (a, b) switch
    {
        (ObjectVal oa, ObjectVal ob) => ReferenceEquals(oa, ob),
        (not ObjectVal, not ObjectVal) => a.Equals(b),
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {b.Type} 操作数", ErrorKind.Type),
    };

    /// <summary>`<:` / `:>` 的操作数:两边都得是**类型对象**(类对象,接口也是)。
    /// 值那一边不认 —— 那是 `is` 的活。</summary>
    private static (ObjectVal Left, ObjectVal Right) AsTypes(RuntimeValue a, RuntimeValue b, string op)
        => (AsType(a, op, "左"), AsType(b, op, "右"));

    private static ObjectVal AsType(RuntimeValue v, string op, string side)
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
    private static bool IsA(RuntimeValue v, RuntimeValue type, string op)
        => type is ObjectVal t
            ? v.Type.IsAssignableTo(t)
            : throw new RuntimeException($"'{op}' 的右边要是一个类型，得到 {type.Type}", ErrorKind.Type);

    private static System.Numerics.BigInteger AsBigInt(RuntimeValue v, string op) => v switch
    {
        IntVal i => i.Value,
        BigIntVal bi => bi.Value,
        _ => throw new RuntimeException($"运算符 '{op}' 不支持 {v.Type} 操作数", ErrorKind.Type)
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

    /// <summary>收窄回 int32 时**装不下就明说**改用 bigfraction,别静默回绕。
    /// 判据和那句话说一份 —— 两个入口(见下)各摊一遍的话,改口径时必漏一处。</summary>
    private static void FitsFraction(bool tooBig)
    {
        if (tooBig)
            throw new RuntimeException("分数运算结果超出 int 范围（fraction 的分子分母是 32 位，改用 bigfraction）", ErrorKind.Value);
    }

    /// <summary>分数运算的结果收窄回 int32。
    /// **`long` 那条是主力** —— int×int 的积一定装得进它,不必惊动 `BigInteger`。</summary>
    private static RuntimeValue MakeFraction(long num, long den)
    {
        FitsFraction(num < int.MinValue || num > int.MaxValue || den < int.MinValue || den > int.MaxValue);
        return new FractionVal((int)num, (int)den);
    }

    /// <summary>同上,但先从 `BigInteger` 收 —— 乘方涨得太快,中间那一步 `long` 就装不下了
    /// (`(fraction 2000000000 1) ** 20` 是个 190 位的数)。</summary>
    private static RuntimeValue MakeFraction(System.Numerics.BigInteger num, System.Numerics.BigInteger den)
    {
        FitsFraction(num < int.MinValue || num > int.MaxValue || den < int.MinValue || den > int.MaxValue);
        return new FractionVal((int)num, (int)den);
    }

    // ── 乘方 `**` 的几条实现 ──

    /// <summary>`int ** n`:指数非负就在整数里算,负的(或 real / 分数那种)交给 `Math.Pow` 出小数。</summary>
    private static RuntimeValue IntPow(int x, RuntimeValue b)
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
    private static IntVal NarrowIntPow(int x, int e)
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
    private static RuntimeValue BigIntPow(System.Numerics.BigInteger x, RuntimeValue b)
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

    private static RuntimeException PowExponentTooBig(System.Numerics.BigInteger e)
        => new RuntimeException($"指数太大（{e}）—— 乘方的指数要装得进 32 位", ErrorKind.Value);

    /// <summary>`fraction ** n`。**负指数不用落回 real**:上下颠倒就行,分数本来就装得下
    /// (`fraction 1 2 ** -1` 是 `2/1`)—— 只有"指数不是整数"那几种才交给 double。</summary>
    private static RuntimeValue FractionPow(FractionVal x, RuntimeValue b)
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
    private static RuntimeValue BigFractionPow(BigFractionVal x, RuntimeValue b)
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
    private static int? PowCount(RuntimeValue b) => b switch
    {
        IntVal i => i.Value,
        BigIntVal g when g.Value >= int.MinValue && g.Value <= int.MaxValue => (int)g.Value,
        _ => null,
    };

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
