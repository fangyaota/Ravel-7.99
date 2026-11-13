namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class ObjectOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ObjectOperatorMembers), ObjectMembers.Set);

    // `:` `===` `!==` `->` 四个都带 `ReadOnly = true` —— 它们是**钉住的**成员:
    // `:=` 与 `=` 都不收,类体里新开一格遮住也不行(`Scope.Define` 见 `Attr.NoShadow` 就报错)。
    // 理由各有各的:类型判定是 lowering 自己生成的东西(`x: T` 无处不在),同一性是"值/引用"
    // 这套口径的唯一出口,造一对只有一种解释。普通运算符(`+` / `==` …)不带这个记号,
    // 类里照旧能写 `+ := …` 定义自己的。
    [ClassOp(":", ReadOnly = true)]
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
    // `->` —— 造一对(与 `pair k v` 同一件事)。
    [ClassOp("->", ReadOnly = true)]
    private static RuntimeValue PairOf(RuntimeValue a, RuntimeValue b) => BuiltinClasses.MakePair(a, b);

    [ClassOp("===", ReadOnly = true)]
    private static RuntimeValue Identical(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.StrictSame(a, b));

    [ClassOp("!==", ReadOnly = true)]
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
