namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class FunctionOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(FunctionOperatorMembers), ObjectMembers.Set);

    [ClassOp("|")]
    private static RuntimeValue Alternate(RuntimeValue a, RuntimeValue b)
    {
        var branches = a is ControlFunction { Kind: ControlKind.Alternate } chain
            ? chain.Args.Add(b)
            : RList<RuntimeValue>.Empty.Add(a).Add(b);
        return new ControlFunction(ControlKind.Alternate, 1, branches);
    }

    [ClassOp(">>")]
    private static RuntimeValue Then(RuntimeValue a, RuntimeValue b)
        => new ControlFunction(ControlKind.Then, 1, RList<RuntimeValue>.Empty.Add(a).Add(b));
}
