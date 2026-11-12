namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class CharOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(CharOperatorMembers), ObjectMembers.Set);

    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(b is CharVal o && ((CharVal)a).Value == o.Value);

    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(b is not CharVal o || ((CharVal)a).Value != o.Value);

    [ClassOp("<")]
    private static RuntimeValue Less(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value < BuiltinClasses.Operand<CharVal>(b, "<").Value);

    [ClassOp(">")]
    private static RuntimeValue Greater(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value > BuiltinClasses.Operand<CharVal>(b, ">").Value);

    [ClassOp("<=")]
    private static RuntimeValue LessOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value <= BuiltinClasses.Operand<CharVal>(b, "<=").Value);

    [ClassOp(">=")]
    private static RuntimeValue GreaterOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value >= BuiltinClasses.Operand<CharVal>(b, ">=").Value);

    [ClassOp("+")]
    private static RuntimeValue Concat(RuntimeValue a, RuntimeValue b)
        => new StringVal(((CharVal)a).Value + BuiltinClasses.AsText(b, "+"));

    [ClassOp("*")]
    private static RuntimeValue Repeat(RuntimeValue a, RuntimeValue b)
        => new StringVal(string.Concat(
            Enumerable.Repeat(((CharVal)a).Value, Math.Max(0, BuiltinClasses.Operand<IntVal>(b, "*").Value))));
}
