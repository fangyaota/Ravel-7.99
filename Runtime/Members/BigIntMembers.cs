namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class BigIntMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(BigIntMembers), ObjectMembers.Set);

    [ClassCtor]
    private static RuntimeValue Init(RuntimeValue arg) => BuiltinClasses.CastToBigInt(arg);

    [ClassOp("**")]
    private static RuntimeValue Pow(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.BigIntPow(BuiltinClasses.AsBigInt(a, "**"), b);

    [ClassOp("+")]
    private static RuntimeValue Add(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "+") + BuiltinClasses.AsBigInt(b, "+"));

    [ClassOp("-")]
    private static RuntimeValue Subtract(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "-") - BuiltinClasses.AsBigInt(b, "-"));

    [ClassOp("*")]
    private static RuntimeValue Multiply(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "*") * BuiltinClasses.AsBigInt(b, "*"));

    [ClassOp("/")]
    private static RuntimeValue Divide(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "/") / BuiltinClasses.NonZero(BuiltinClasses.AsBigInt(b, "/"), "/"));

    [ClassOp("%")]
    private static RuntimeValue Modulo(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "%") % BuiltinClasses.NonZero(BuiltinClasses.AsBigInt(b, "%"), "%"));

    [ClassOp("<<")]
    private static RuntimeValue ShiftLeft(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, "<<") << BuiltinClasses.ShiftCount(b, "<<"));

    [ClassOp(">>")]
    private static RuntimeValue ShiftRight(RuntimeValue a, RuntimeValue b)
        => new BigIntVal(BuiltinClasses.AsBigInt(a, ">>") >> BuiltinClasses.ShiftCount(b, ">>"));

    [ClassOp("<<<")]
    private static RuntimeValue RotateLeft(RuntimeValue a, RuntimeValue b)
        => throw new RuntimeException(
            "运算符 '<<<' 不支持 bigint：循环移位要在**固定宽度**上转，而 bigint 没有宽度（要移就用 '<<'）", ErrorKind.Type);

    [ClassOp(">>>")]
    private static RuntimeValue RotateRight(RuntimeValue a, RuntimeValue b)
        => throw new RuntimeException(
            "运算符 '>>>' 不支持 bigint：循环移位要在**固定宽度**上转，而 bigint 没有宽度（要移就用 '>>'）", ErrorKind.Type);

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
