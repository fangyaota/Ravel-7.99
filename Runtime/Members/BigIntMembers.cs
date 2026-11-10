namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`bigint` 这个类给它的实例准备什么** —— 只有运算符这一份(方法在别的成员集里,
/// 建类时用 `MemberSet.Combine` 把几份接成一条)。
///
/// `bigint` 没有固定宽度,所以 `<<` 就是乘 2^k —— **没有丢位这回事**(要多少位有多少位)。
/// 循环移位对 bigint **不成立**:转圈得先有个圈,所以 `<<<` / `>>>` 那两条明确报错
/// (而不是偷偷当普通移位)。其余算术都是 `BigInteger` 里的精确运算。
///
/// 声明顺序就是 `Fields ()` 报的顺序(`MemberSet.Scan` 按声明收名字),所以这里
/// **保持原来注册的顺序**:`**` 最先,然后 `+ - * / %`、四条移位、最后那六条比较。</summary>
internal static class BigIntMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(BigIntMembers), ObjectMembers.Set);

    /// <summary>`bigint ** n`:指数非负时是**精确**的(bigint 不会溢出);负指数落回 real
    /// —— 和 int 那条一个规矩(总规矩见 `RealMembers` 的 `**`)。
    ///
    /// 指数本身超过 32 位就报:那是 .NET 的 `BigInteger.Pow` 收不下(它只收 `int`),
    /// 不是"算得出来却不给算"—— 真算出来也是个存不下的东西。</summary>
    [ClassOp("**")]
    private static RuntimeValue Pow(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.BigIntPow(BuiltinClasses.AsBigInt(a, "**"), b);

    /// <summary>`bigint + bigint`。`AsBigInt` 两边都收 —— int 也照样升上来
    /// (`1 + bigint 2` 和 `bigint 2 + 1` 从此对称;从前只特判了 real,前者报
    /// 「运算符 '+' 不支持 BigInt 操作数」)。</summary>
    [ClassOp("+")]
    private static RuntimeValue Add(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "+") + BuiltinClasses.AsBigInt(b, "+"));

    /// <summary>`bigint - bigint` —— 和 `+` 一个口径。</summary>
    [ClassOp("-")]
    private static RuntimeValue Subtract(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "-") - BuiltinClasses.AsBigInt(b, "-"));

    /// <summary>`bigint * bigint` —— 和 `+` 一个口径。</summary>
    [ClassOp("*")]
    private static RuntimeValue Multiply(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "*") * BuiltinClasses.AsBigInt(b, "*"));

    /// <summary>`bigint / bigint` —— 整除。除数为零走 `NonZero`:不查的话 C# 抛的是
    /// 英文的 `DivideByZeroException`,用户看到 "Attempted to divide by zero."。</summary>
    [ClassOp("/")]
    private static RuntimeValue Divide(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "/") / BuiltinClasses.NonZero(BuiltinClasses.AsBigInt(b, "/"), "/"));

    /// <summary>`bigint % bigint` —— 余数取**截断**那个(C# 的整数除法朝零截断),
    /// 和 int 那条一个口径。除数为零同样先过 `NonZero`。</summary>
    [ClassOp("%")]
    private static RuntimeValue Modulo(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "%") % BuiltinClasses.NonZero(BuiltinClasses.AsBigInt(b, "%"), "%"));

    /// <summary>`bigint << n`:就是乘 2^n —— 没有固定宽度,**不会丢位**。
    /// 移位数由 `ShiftCount` 收成一个非负 int(负数报错:想往另一头移就换 `>>`)。</summary>
    [ClassOp("<<")]
    private static RuntimeValue ShiftLeft(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "<<") << BuiltinClasses.ShiftCount(b, "<<"));

    /// <summary>`bigint >> n`:除以 2^n 往下取整(算术右移,保符号)。
    /// 同样没有宽度,所以不丢位。</summary>
    [ClassOp(">>")]
    private static RuntimeValue ShiftRight(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, ">>") >> BuiltinClasses.ShiftCount(b, ">>"));

    /// <summary>`<<<` **对 bigint 明确报错**:循环移位要在**固定宽度**上转,而 bigint
    /// 没有宽度 —— 与其偷偷当普通移位(`x <<< 33` 变成 `x << 33`),不如当场说清楚。
    /// (int 那条才是真的循环左移,见 `IntegerScope`。)</summary>
    [ClassOp("<<<")]
    private static RuntimeValue RotateLeft(RuntimeValue a, RuntimeValue b)
        => throw new RuntimeException(
            "运算符 '<<<' 不支持 bigint：循环移位要在**固定宽度**上转，而 bigint 没有宽度（要移就用 '<<'）", ErrorKind.Type);

    /// <summary>`>>>` **对 bigint 明确报错** —— 和 `<<<` 同一个理由(没有边,也就没有
    /// "从另一头回来")。逻辑右移是另一件事,用 `lib/bits.rav` 的 `Shr`。</summary>
    [ClassOp(">>>")]
    private static RuntimeValue RotateRight(RuntimeValue a, RuntimeValue b)
        => throw new RuntimeException(
            "运算符 '>>>' 不支持 bigint：循环移位要在**固定宽度**上转，而 bigint 没有宽度（要移就用 '>>'）", ErrorKind.Type);

    /// <summary>`==`:走 `CompareNumeric` —— **精确那一族不经过 double**,两边通成
    /// 分子/分母交叉相乘再比。从前六条比较一律 `AsDouble`,于是
    /// `bigint 10^30 == bigint (10^30+1)` 交回 **true**,而 `-` 交回 `-1` ——
    /// 同一个程序自己跟自己打架。</summary>
    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "==") == 0);

    /// <summary>`!=` —— 同 `==`,取反。</summary>
    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "!=") != 0);

    /// <summary>`<` —— 精确比较,交叉相乘不溢出。</summary>
    [ClassOp("<")]
    private static RuntimeValue Less(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "<") < 0);

    /// <summary>`>`。</summary>
    [ClassOp(">")]
    private static RuntimeValue Greater(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, ">") > 0);

    /// <summary>`<=`。</summary>
    [ClassOp("<=")]
    private static RuntimeValue LessOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "<=") <= 0);

    /// <summary>`>=`。</summary>
    [ClassOp(">=")]
    private static RuntimeValue GreaterOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, ">=") >= 0);
}
