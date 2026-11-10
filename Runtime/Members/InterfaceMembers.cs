namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class InterfaceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(InterfaceMembers), ObjectMembers.Set);

    [ClassMethod(BuiltinClasses.DisposeMember)]
    private static RuntimeValue Dispose(RuntimeValue self)
        => BuiltinClasses.DisposeImplementation((ObjectVal)self);
}
