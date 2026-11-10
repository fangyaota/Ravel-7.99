namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

internal static class ObjectMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ObjectMembers));

    internal static IMemberSet Any => BuiltinClasses.Object.Members ?? Set;

    [ClassMethod("ToString")]
    private static RuntimeValue ToString(RuntimeValue self) => new StringVal(self.ToString()!);

    [ClassMethod("Copy")]
    private static RuntimeValue Copy(RuntimeValue self) => BuiltinClasses.CopyValue(self);

    [ClassMethod("Fields")]
    private static RuntimeValue Fields(RuntimeValue self)
        => new ListVal([.. self.MemberScope.MemberNames.Select(n => (RuntimeValue)new StringVal(n))]);

    [ClassMethod("MemberScope")]
    private static RuntimeValue MemberScope(RuntimeValue self)
        => new ScopeVal(self is ObjectVal o ? o.Scope : self.MemberScope);

    [ClassMethod("CompareTo")]
    private static RuntimeValue CompareTo(RuntimeValue self, RuntimeValue arg)
        => IntVal.Of(BuiltinClasses.Less(self, arg) ? -1 : BuiltinClasses.Less(arg, self) ? 1 : 0);

    [ClassMethod("Key")]
    private static RuntimeValue Key(RuntimeValue self)
        => BuiltinClasses.CanBeKey(self.Type)
            ? self
            : throw new RuntimeException(
                $"Key: {self.Type} 没有默认的键（只有按值比的那些有：数 / 串 / 字符 / 布尔 / `()`）。"
                + "要拿它当键，就在类里写一条 Key，交回一个值类型的键", ErrorKind.Type);

    [ClassMethod("ToDot")]
    private static RuntimeValue ToDot(RuntimeValue self) => new DotNetVal(NetBridge.ToNet(self));
}

internal static class ModuleMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ModuleMembers), ObjectMembers.Set);

    [ClassMethod("References")]
    private static RuntimeValue References(RuntimeValue self)
        => new ListVal(self is ModuleVal { Owner: { } engine } m
            ? [.. engine.ResolveReferences(m)]
            : []);
}
