namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class FunctionOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(FunctionOperatorMembers), ObjectMembers.Set);

    [ClassOp(">>")]
    private static RuntimeValue Then(RuntimeValue a, RuntimeValue b)
        => new ControlFunction(ControlKind.Then, 1, RList<RuntimeValue>.Empty.Add(a).Add(b));

    // `f << g` = 右边先跑:`(f << g) x` 就是 `f (g x)`。
    // 和 `>>` 只差一个顺序,所以直接把两枚函数**倒过来**交给同一个 `Then`。
    [ClassOp("<<")]
    private static RuntimeValue Compose(RuntimeValue a, RuntimeValue b)
        => new ControlFunction(ControlKind.Then, 1, RList<RuntimeValue>.Empty.Add(b).Add(a));
}
