namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`fraction` 这个类给它的实例准备什么** —— 只有运算符这一份(方法在别的成员集里,
/// 建类时用 `MemberSet.Combine` 把几份接成一条)。
///
/// 分子分母**在 `long` 里**参与运算(见 `BuiltinClasses.FractionBinOp`):两个 int 相乘再收窄
/// 会静默回绕(`fraction 100000 1 * fraction 100000 1` 从前得到 1410065408/1),而 int×int
/// 的积一定装得进 `long`,所以这一层够用。收窄交给 `BuiltinClasses.MakeFraction`:装不下就
/// **明说**改用 bigfraction,别静默回绕。
///
/// 声明顺序就是 `Fields ()` 报的顺序(`MemberSet.Scan` 按声明收名字),所以这里
/// **保持原来注册的顺序**:`**` 最先,然后 `+ - * / %`,最后那六条比较。</summary>
internal static class FractionMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(FractionMembers), ObjectMembers.Set);

    /// <summary>`fraction ** n`:负次幂**照样是分数**(上下颠倒而已,分数本来就装得下)——
    /// 不受"整数的负次幂交回 real"那条影响(乘方总规矩见 `RealMembers` 的 `**`)。
    /// 走 `BuiltinClasses.FractionPow`:乘方涨得太快,中间那一步 `long` 就装不下了,
    /// 所以它在 `BigInteger` 里算,最后才收窄。</summary>
    [ClassOp("**")]
    private static RuntimeValue Pow(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionPow(BuiltinClasses.Operand<FractionVal>(a, "**"), b);

    /// <summary>`a + b`:`na/da + nb/db = (na*db + nb*da) / (da*db)`,约分在 `MakeFraction`。
    /// 右操作数可以是 int(`FractionBinOp` 把整数当分母 1)。</summary>
    [ClassOp("+")]
    private static RuntimeValue Add(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) => BuiltinClasses.MakeFraction(na * db + nb * da, da * db));

    /// <summary>`a - b` —— 通分后相减,同 `+`。</summary>
    [ClassOp("-")]
    private static RuntimeValue Subtract(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) => BuiltinClasses.MakeFraction(na * db - nb * da, da * db));

    /// <summary>`a * b = (na*nb) / (da*db)` —— 不必通分,直接乘。</summary>
    [ClassOp("*")]
    private static RuntimeValue Multiply(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) => BuiltinClasses.MakeFraction(na * nb, da * db));

    /// <summary>`a / b = (na*db) / (da*nb)`。除数为零报 Ravel 自己的 `ErrorKind.ZeroDivision`
    /// —— 分数在 C# 里是两整数相除,不先拦就是英文的 `DivideByZeroException`。</summary>
    [ClassOp("/")]
    private static RuntimeValue Divide(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) =>
            nb != 0
                ? BuiltinClasses.MakeFraction(na * db, da * nb)
                : throw new RuntimeException("运算符 '/' 的除数为零", ErrorKind.ZeroDivision));

    /// <summary>`a % b` 和 Int 那条**一个口径** —— 余数取**截断**那个(`-7 % 3` 是 -1,
    /// 不是 Python 的 2):
    ///     a % b = a - b * trunc(a / b)
    /// 这里的 `(na * db) / (da * nb)` 就是 `trunc(a / b)`(C# 的整数除法朝零截断)。</summary>
    [ClassOp("%")]
    private static RuntimeValue Modulo(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) =>
            nb != 0
                ? BuiltinClasses.MakeFraction(na * db - ((na * db) / (da * nb)) * (nb * da), da * db)
                : throw new RuntimeException("运算符 '%' 的除数为零", ErrorKind.ZeroDivision));

    /// <summary>`==`:走 `CompareNumeric` —— **精确那一族不经过 double**,两边通成
    /// 分子/分母交叉相乘再比(`2/4 == 1/2` 因此成立,不必先约分)。</summary>
    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "==") == 0);

    /// <summary>`!=` —— 同 `==`,取反。</summary>
    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "!=") != 0);

    /// <summary>`<` —— 交叉相乘比,不先约分(约分是构造时的事)。</summary>
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
