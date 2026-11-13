namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class ObjectOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ObjectOperatorMembers), ObjectMembers.Set);

    [ClassMethod(":")]
    private static RuntimeValue TypeTest(Interpreter interp, RuntimeValue self, RuntimeValue type)
        => type is ObjectVal t
            ? new BoolVal(BuiltinClasses.Matches(interp, self, t))
            : throw new RuntimeException($"':' 的右边要是一个类型，得到 {type.Type}", ErrorKind.Type);

    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.SameValue(a, b, "=="));

    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(!BuiltinClasses.SameValue(a, b, "!="));

    // `===` / `!==` —— 同一性:类型不同即 false,值语义比值,对象比引用(见 `StrictSame`)。
    // **普通的运算符**:与 `==` / `!=` 同住这张表,所以类可以自己写 `=== := …` 覆盖它。
    // `->` —— 造一对(与 `pair k v` 同一件事)。**普通的运算符**:与 `==` / `===` 同住这张表,
    // 所以类可以自己写 `-> := …` 覆盖它。
    [ClassOp("->")]
    private static RuntimeValue PairOf(RuntimeValue a, RuntimeValue b) => BuiltinClasses.MakePair(a, b);

    [ClassOp("===")]
    private static RuntimeValue Identical(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.StrictSame(a, b));

    [ClassOp("!==")]
    private static RuntimeValue NotIdentical(RuntimeValue a, RuntimeValue b)
        => new BoolVal(!BuiltinClasses.StrictSame(a, b));

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
