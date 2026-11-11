namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class FunctionOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(FunctionOperatorMembers), ObjectMembers.Set);

    [ClassOp(">>")]
    private static RuntimeValue Then(RuntimeValue a, RuntimeValue b)
        => new ControlFunction(ControlKind.Then, 1, RList<RuntimeValue>.Empty.Add(a).Add(b));
}
