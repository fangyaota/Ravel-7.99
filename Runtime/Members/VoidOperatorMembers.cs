namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class VoidOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(VoidOperatorMembers), ObjectOperatorMembers.Set);

    // `()` 只有一个值:和谁比都只是"是不是 ()"。右操作数不限类型。
    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue self, RuntimeValue other)
        => new BoolVal(other is VoidVal);

    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue self, RuntimeValue other)
        => new BoolVal(other is not VoidVal);
}
