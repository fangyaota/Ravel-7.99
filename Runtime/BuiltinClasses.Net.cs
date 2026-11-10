namespace Ravel.Runtime;

internal static partial class BuiltinClasses
{
    // `DotNetObject` / `DotNetType` 的方法搬走了,见 `Runtime/Members/NetMembers.cs`
    // (`DotNetObjectMembers` / `DotNetTypeMembers`);`Object.ToDot` 收进了
    // `Runtime/Members/ObjectMembers.cs`(`ObjectMembers`)。这里不再注册任何实例成员。

    internal static DotNetVal V(RuntimeValue self) => (DotNetVal)self;

    internal static Type T(RuntimeValue self) => ((DotNetTypeVal)self).DotNetType;

    internal static Type AsType(RuntimeValue a, string what) => a switch
    {
        DotNetTypeVal t => t.DotNetType,
        DotNetVal { Value: Type ty } => ty,
        _ => throw new RuntimeException(
            $"{what} 得是个类型（`Reflect.Type \"…\"` 交回的那种，或者 `.AsObject ()` 包回来的），得到 {a.Type}",
            ErrorKind.Type),
    };
}
