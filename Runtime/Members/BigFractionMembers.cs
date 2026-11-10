namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`bigfraction` 这个类给它的实例准备什么** —— 只有运算符这一份(方法在别的成员集里,
/// 建类时用 `MemberSet.Combine` 把几份接成一条)。
///
/// 分子分母本身就是 `BigInteger`,**运算不会溢出**,所以没有 fraction 那层"`long` 里算完再
/// 收窄回 int32"的麻烦事 —— 直接算就是精确答案。除零自己拦(Ravel 的
/// `ErrorKind.ZeroDivision`),不把 C# 的英文异常漏给用户。`%` 与 fraction / int 同口径:
/// 余数取**截断**那个。
///
/// 声明顺序就是 `Fields ()` 报的顺序(`MemberSet.Scan` 按声明收名字),所以这里
/// **保持原来注册的顺序**:`**` 最先,然后 `+ - * / %`,最后那六条比较。</summary>
internal static class BigFractionMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(BigFractionMembers), ObjectMembers.Set);

    /// <summary>`bigfraction ** n`:负次幂照样是分数(上下颠倒),不受"整数的负次幂交回
    /// real"那条影响。走 `BuiltinClasses.BigFractionPow` —— 底数本身就是 `BigInteger`,
    /// 乘方一路精确。</summary>
    [ClassOp("**")]
    private static RuntimeValue Pow(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.BigFractionPow(BuiltinClasses.Operand<BigFractionVal>(a, "**"), b);

    /// <summary>`a + b`:`na/da + nb/db = (na*db + nb*da) / (da*db)`。整数右操作数由
    /// `BigFractionBinOp` 当分母 1 收进来。</summary>
    [ClassOp("+")]
    private static RuntimeValue Add(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * db + nb * da, da * db));

    /// <summary>`a - b` —— 通分后相减,同 `+`。</summary>
    [ClassOp("-")]
    private static RuntimeValue Subtract(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * db - nb * da, da * db));

    /// <summary>`a * b = (na*nb) / (da*db)` —— 不必通分,直接乘。</summary>
    [ClassOp("*")]
    private static RuntimeValue Multiply(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * nb, da * db));

    /// <summary>`a / b = (na*db) / (da*nb)`。除数为零报 `ErrorKind.ZeroDivision`
    /// (`BigInteger` 除零抛的是 `DivideByZeroException`,那是英文的)。
    /// 判零用 `nb.IsZero`:和 `nb != 0` 等价,但不必造那个临时整数。</summary>
    [ClassOp("/")]
    private static RuntimeValue Divide(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.BigFractionBinOp(a, b, (na, da, nb, db) =>
            nb.IsZero
                ? throw new RuntimeException("运算符 '/' 的除数为零", ErrorKind.ZeroDivision)
                : new BigFractionVal(na * db, da * nb));

    /// <summary>`a % b` —— 和 int / fraction 那条**一个口径**:余数取截断那个
    /// (`(na * db) / (da * nb)` 就是 `trunc(a / b)`)。除数为零同样先拦。</summary>
    [ClassOp("%")]
    private static RuntimeValue Modulo(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.BigFractionBinOp(a, b, (na, da, nb, db) =>
            nb.IsZero
                ? throw new RuntimeException("运算符 '%' 的除数为零", ErrorKind.ZeroDivision)
                : new BigFractionVal(na * db - ((na * db) / (da * nb)) * (nb * da), da * db));

    /// <summary>`==`:走 `CompareNumeric` —— 精确那一族不经过 double,交叉相乘再比。</summary>
    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "==") == 0);

    /// <summary>`!=` —— 同 `==`,取反。</summary>
    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "!=") != 0);

    /// <summary>`<`。</summary>
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
