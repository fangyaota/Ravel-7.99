namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class RealMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(RealMembers), ObjectMembers.Set);

    [ClassCtor]
    private static RuntimeValue Init(RuntimeValue arg) => BuiltinClasses.CastToFloat(arg);

    [ClassOp("+")]
    private static RuntimeValue Add(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "+") + BuiltinClasses.AsDouble(b, "+"));

    [ClassOp("-")]
    private static RuntimeValue Subtract(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "-") - BuiltinClasses.AsDouble(b, "-"));

    [ClassOp("*")]
    private static RuntimeValue Multiply(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "*") * BuiltinClasses.AsDouble(b, "*"));

    [ClassOp("/")]
    private static RuntimeValue Divide(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "/") / BuiltinClasses.AsDouble(b, "/"));

    [ClassOp("%")]
    private static RuntimeValue Modulo(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "%") % BuiltinClasses.AsDouble(b, "%"));

    [ClassOp("**")]
    private static RuntimeValue Pow(RuntimeValue a, RuntimeValue b)
        => new RealVal(Math.Pow(BuiltinClasses.AsDouble(a, "**"), BuiltinClasses.AsDouble(b, "**")));

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
