namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class TypeMembers
{
    // **声明必须排在 `Set` 之前** —— `Set` 是 `MemberSet.Scan` 靠反射读这两格建的,
    // 字段还没初始化就读到 null 了。
    [ClassValue("GetImplements")]
    private static readonly RuntimeValue GetImplements = new TraitQuery("GetImplements", TraitQueryKind.Implements);

    [ClassValue("GetImplementors")]
    private static readonly RuntimeValue GetImplementors = new TraitQuery("GetImplementors", TraitQueryKind.Implementors);

    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(TypeMembers), FunctionMembers.Set);

    [ClassMethod("Default")]
    private static RuntimeValue Default(RuntimeValue self) => BuiltinClasses.ConvertDirect((ObjectVal)self, VoidVal.Instance);

    [ClassMethod("Subtypes")]
    private static RuntimeValue Subtypes(RuntimeValue self)
    {
        var t = (ObjectVal)self;
        var subs = new List<RuntimeValue>();
        foreach (var sub in BuiltinClasses.AllTypes)
        {
            if (sub != t && sub.IsAssignableTo(t))
                subs.Add(sub);
        }

        return new ListVal(subs);
    }
}
