namespace Ravel.Runtime;

/// <summary>**一个 .NET 类型**(`System.Type`)。
///
/// 名字不能叫 `Type` —— 那个名字被 Ravel 自己占了(`typeof` 交回的就是它)。
/// 拿到它之后:`.Name` / `.FullName` 看名字,`Reflect.New` 造一个,
/// `Reflect.Call` 调它的静态方法,`Reflect.Members` 列成员。</summary>
public record DotNetTypeVal : ObjectVal
{
    public Type DotNetType { get; init; }

    public DotNetTypeVal(Type t, Scope? members = null)
        : base(PluginKit.ClassOf("DotNetType"), members ?? new Scope())
        => DotNetType = t;

    public override string ToString() => PluginKit.Guard(() => "DotNetType " + DotNetType.FullName);
}
