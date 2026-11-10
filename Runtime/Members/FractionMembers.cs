namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class FractionMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(FractionMembers), ObjectMembers.Set);

    [ClassCtor]
    private static RuntimeValue Init(RuntimeValue arg) => BuiltinClasses.CastToFraction(arg);

    [ClassOp("**")]
    private static RuntimeValue Pow(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionPow(BuiltinClasses.Operand<FractionVal>(a, "**"), b);

    [ClassOp("+")]
    private static RuntimeValue Add(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) => BuiltinClasses.MakeFraction(na * db + nb * da, da * db));

    [ClassOp("-")]
    private static RuntimeValue Subtract(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) => BuiltinClasses.MakeFraction(na * db - nb * da, da * db));

    [ClassOp("*")]
    private static RuntimeValue Multiply(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) => BuiltinClasses.MakeFraction(na * nb, da * db));

    [ClassOp("/")]
    private static RuntimeValue Divide(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) =>
            nb != 0
                ? BuiltinClasses.MakeFraction(na * db, da * nb)
                : throw new RuntimeException("运算符 '/' 的除数为零", ErrorKind.ZeroDivision));

    [ClassOp("%")]
    private static RuntimeValue Modulo(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.FractionBinOp(a, b, (na, da, nb, db) =>
            nb != 0
                ? BuiltinClasses.MakeFraction(na * db - ((na * db) / (da * nb)) * (nb * da), da * db)
                : throw new RuntimeException("运算符 '%' 的除数为零", ErrorKind.ZeroDivision));

    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "==") == 0);

    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "!=") != 0);

    [ClassOp("<")]
    private static RuntimeValue Less(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "<") < 0);

    [ClassOp(">")]
    private static RuntimeValue Greater(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, ">") > 0);

    [ClassOp("<=")]
    private static RuntimeValue LessOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "<=") <= 0);

    [ClassOp(">=")]
    private static RuntimeValue GreaterOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, ">=") >= 0);
}
