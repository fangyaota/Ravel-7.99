namespace Ravel.Runtime;

/// <summary>**一个 .NET 类型**（`System.Type`）。
///
/// 名字不能叫 `Type` —— 那个名字被 Ravel 自己占了（`typeof` 交回的就是它）。
/// 拿到它之后：`.Name` / `.FullName` 看名字，`Reflect.New` 造一个，
/// `Reflect.Call` 调它的静态方法，`.Members` 列成员。
///
/// **它是个"静态访问的把手"**：`Reflect.Call` 在它身上找的是**静态**成员。
/// `Type` 自己的实例成员（`GetMethods` / `GetParameters`…）要 `.AsObject ()`
/// 拿成对象才够得着。
///
/// 和 <see cref="DotNetVal"/> 一样，2026-10-07 从 `Ravel.Reflect` 搬进引擎。</summary>
public record DotNetTypeVal : ObjectVal
{
    public Type DotNetType { get; init; }

    public DotNetTypeVal(Type t, Scope? members = null)
        : base(BuiltinClasses.DotNetType, members ?? new Scope())
        => DotNetType = t;

    public override string ToString() => ShowDepth.Guard(() => "DotNetType " + DotNetType.FullName);
}
