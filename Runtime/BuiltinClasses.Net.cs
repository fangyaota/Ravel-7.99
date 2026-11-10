namespace Ravel.Runtime;

/// <summary>**.NET 互操作那一层留下的辅助。**
///
/// 成员本身(`DotNetObject` / `DotNetType` 的方法、`Object.ToDot`)已经搬到
/// `Runtime/Members/NetMembers.cs` —— 每个类自己带一份成员工厂,反射一次收齐。
/// 这儿只剩三样**不隶属于"某个类的实例"**的东西:`V` / `T` 两个拆包、`AsType` 收"类型"参数
/// —— 和 `TextArg` / `IntArg` 一个性质,由 `NetBridge` 那侧的实现兜着。
///
/// **为什么这一层在引擎里而不是 `Ravel.Reflect` 插件里**(2026-10-07 搬进来):
/// `DotNetVal` 一造出来就要**按名字拿 `DotNetObject` 那个类**（`ObjectVal` 的基类 ctor
/// 收的是 `ClassVal`），类不在引擎里的话，没 `using` 那个插件的场合连**造都造不出来**
/// —— 于是 `Object.ToDot ()` 根本没法挂在根类上。搬过来之后 `Ravel.Reflect` 只剩
/// `Reflect` 模块那五个函数（`Type` / `New` / `Call` / `Text` / `Unwrap`）。
///
/// 两个方向仍然分开：`ToDot` 是"我按自己那套给你"（`NetBridge.ToNet`），
/// `ToObject 类型` 是"**你要什么我给你什么**"（`NetBridge.To`，表可在 C# 层注册）——
/// 生成出来的适配层要的正是后者：参数要 `IEnumerable<string>`，交个 `object[]` 过去
/// 就是运行期炸。</summary>
internal static partial class BuiltinClasses
{
    // `DotNetObject` / `DotNetType` 的方法搬走了,见 `Runtime/Members/NetMembers.cs`
    // (`DotNetObjectMembers` / `DotNetTypeMembers`);`Object.ToDot` 收进了
    // `Runtime/Members/ObjectMembers.cs`(`ObjectMembers`)。这里不再注册任何实例成员。

    /// <summary>`DotNetVal` 拆回 CLR 那一侧。</summary>
    internal static DotNetVal V(RuntimeValue self) => (DotNetVal)self;

    /// <summary>`DotNetType` 拆回 `System.Type`。</summary>
    internal static Type T(RuntimeValue self) => ((DotNetTypeVal)self).DotNetType;

    /// <summary>收一个"类型"参数 —— `.ToObject` 那一格要。两种都收：
    /// `DotNetType`（`Reflect.Type` 交回的那种）和包着 `Type` 的 `DotNetObject`
    /// （`.AsObject ()` / `.TypeOf ()` 交回的那种）。别的当场报。</summary>
    internal static Type AsType(RuntimeValue a, string what) => a switch
    {
        DotNetTypeVal t => t.DotNetType,
        DotNetVal { Value: Type ty } => ty,
        _ => throw new RuntimeException(
            $"{what} 得是个类型（`Reflect.Type \"…\"` 交回的那种，或者 `.AsObject ()` 包回来的），得到 {a.Type}",
            ErrorKind.Type),
    };
}
