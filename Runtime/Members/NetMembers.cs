namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

internal static class DotNetObjectMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(DotNetObjectMembers), ObjectMembers.Set);

    [ClassMethod("TypeOf")]
    private static RuntimeValue TypeOf(RuntimeValue self)
        => new DotNetTypeVal(BuiltinClasses.V(self).Value?.GetType() ?? typeof(object));

    [ClassMethod("Text")]
    private static RuntimeValue Text(RuntimeValue self) => new StringVal(NetBridge.Show(BuiltinClasses.V(self).Value));

    [ClassMethod("Unwrap")]
    private static RuntimeValue Unwrap(RuntimeValue self) => NetBridge.ToRavel(BuiltinClasses.V(self).Value);

    [ClassMethod("ToObject")]
    private static RuntimeValue ToObject(RuntimeValue self, RuntimeValue arg)
    {
        var t = BuiltinClasses.AsType(arg, "DotNetObject.ToObject 的类型");
        return NetBridge.FromNet(BuiltinClasses.V(self).ToObject(t, "DotNetObject.ToObject"));
    }
}

internal static class DotNetTypeMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(DotNetTypeMembers), ObjectMembers.Set);

    [ClassMethod("Name")]
    private static RuntimeValue Name(RuntimeValue self) => new StringVal(BuiltinClasses.T(self).Name);

    [ClassMethod("FullName")]
    private static RuntimeValue FullName(RuntimeValue self) => new StringVal(BuiltinClasses.T(self).FullName ?? BuiltinClasses.T(self).Name);

    [ClassMethod("Text")]
    private static RuntimeValue Text(RuntimeValue self) => new StringVal(NetBridge.Show(BuiltinClasses.T(self)));

    [ClassMethod("IsValue")]
    private static RuntimeValue IsValue(RuntimeValue self) => new BoolVal(BuiltinClasses.T(self).IsValueType);

    [ClassMethod("IsInterface")]
    private static RuntimeValue IsInterface(RuntimeValue self) => new BoolVal(BuiltinClasses.T(self).IsInterface);

    [ClassMethod("AsObject")]
    private static RuntimeValue AsObject(RuntimeValue self) => new DotNetVal(BuiltinClasses.T(self));

    [ClassMethod("Members")]
    private static RuntimeValue Members(RuntimeValue self)
    {
        var names = BuiltinClasses.T(self)
            .GetMembers(System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static)
            .Select(m => m.Name).Distinct().OrderBy(x => x, StringComparer.Ordinal);
        return new ListVal([.. names.Select(x => (RuntimeValue)new StringVal(x))]);
    }
}
