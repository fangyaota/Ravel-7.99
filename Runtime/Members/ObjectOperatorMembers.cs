namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class ObjectOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ObjectOperatorMembers), ObjectMembers.Set);

    [ClassMethod(":")]
    private static RuntimeValue TypeTest(RuntimeValue self, RuntimeValue type)
        => new BoolVal(BuiltinClasses.IsA(self, type, ":"));

    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.SameValue(a, b, "=="));

    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(!BuiltinClasses.SameValue(a, b, "!="));

    [ClassOp("<:")]
    private static RuntimeValue SubtypeOf(RuntimeValue a, RuntimeValue b)
    {
        var (x, y) = BuiltinClasses.AsTypes(a, b, "<:");
        return new BoolVal(x.IsAssignableTo(y));
    }

    [ClassOp(":>")]
    private static RuntimeValue SupertypeOf(RuntimeValue a, RuntimeValue b)
    {
        var (x, y) = BuiltinClasses.AsTypes(a, b, ":>");
        return new BoolVal(y.IsAssignableTo(x));
    }
}
