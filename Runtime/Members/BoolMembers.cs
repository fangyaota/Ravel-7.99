namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class BoolMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(BoolMembers), ObjectMembers.Set);

    [ClassCtor]
    private static RuntimeValue Init(RuntimeValue arg) => BuiltinClasses.CastToBool(arg);

    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(b is BoolVal o && ((BoolVal)a).Value == o.Value);

    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(b is not BoolVal o || ((BoolVal)a).Value != o.Value);

    [ClassOp("&")]
    private static RuntimeValue And(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((BoolVal)a).Value && BuiltinClasses.Operand<BoolVal>(b, "&").Value);

    [ClassOp("|")]
    private static RuntimeValue Or(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((BoolVal)a).Value || BuiltinClasses.Operand<BoolVal>(b, "|").Value);

    [ClassOp("^")]
    private static RuntimeValue Xor(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((BoolVal)a).Value ^ BuiltinClasses.Operand<BoolVal>(b, "^").Value);
}
