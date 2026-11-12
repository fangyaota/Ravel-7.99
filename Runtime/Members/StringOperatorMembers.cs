namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class StringOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(StringOperatorMembers), ObjectMembers.Set);

    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(b is StringVal o && ((StringVal)a).Value == o.Value);

    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(b is not StringVal o || ((StringVal)a).Value != o.Value);

    [ClassOp("+")]
    private static RuntimeValue Concat(RuntimeValue a, RuntimeValue b)
        => new StringVal(((StringVal)a).Value + BuiltinClasses.AsText(b, "+"));

    [ClassOp("*")]
    private static RuntimeValue Repeat(RuntimeValue a, RuntimeValue b)
        => new StringVal(string.Concat(
            Enumerable.Repeat(((StringVal)a).Value, Math.Max(0, BuiltinClasses.Operand<IntVal>(b, "*").Value))));

    [ClassOp("<")]
    private static RuntimeValue Less(RuntimeValue a, RuntimeValue b)
        => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, BuiltinClasses.Operand<StringVal>(b, "<").Value) < 0);

    [ClassOp(">")]
    private static RuntimeValue Greater(RuntimeValue a, RuntimeValue b)
        => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, BuiltinClasses.Operand<StringVal>(b, ">").Value) > 0);

    [ClassOp("<=")]
    private static RuntimeValue LessOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, BuiltinClasses.Operand<StringVal>(b, "<=").Value) <= 0);

    [ClassOp(">=")]
    private static RuntimeValue GreaterOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, BuiltinClasses.Operand<StringVal>(b, ">=").Value) >= 0);
}
