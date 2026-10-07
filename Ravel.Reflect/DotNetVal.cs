namespace Ravel.Runtime;

/// <summary>**一个任意 .NET 对象** —— 反射世界里交出来的东西都装在这儿。
///
/// 为什么非有它:引擎那些值类型(IntVal / ListVal / JsonVal…)全是**专用**的,
/// 没有"我不知道这是什么,先兜着"的那一个。反射返回 `StringBuilder`、`DateTime`、
/// `FileInfo` 的时候,得有地方待。
///
/// 认识的那几种(int / string / 列表 / 字典…)在 `Bridge` 里**当场化成 Ravel 值**,
/// 走不到这儿;落到这儿的都是"引擎不认识的"。要用它,就再喂回 `Reflect.Call` /
/// `Reflect.Text` / `Reflect.Unwrap`。
///
/// **它不实现 `IValue`**(那是内置值类型的名单,引擎/插件说了算,封着的)。</summary>
public record DotNetVal : ObjectVal
{
    public object? Value { get; init; }

    public DotNetVal(object? value, Scope? members = null)
        : base(PluginKit.ClassOf("DotNetObject"), members ?? new Scope())
        => Value = value;

    public override string ToString()
        => PluginKit.Guard(() => Value is null ? "DotNetObject (null)" : "DotNetObject " + Value);
}
