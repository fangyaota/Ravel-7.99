namespace Ravel.Runtime;

/// <summary>**一个任意 .NET 对象** —— 进了 .NET 世界的值都装在这儿。
///
/// 为什么非有它：引擎那些值类型（IntVal / ListVal / JsonVal…）全是**专用**的，
/// 没有"我不知道这是什么，先兜着"的那一个。`StringBuilder` / `DateTime` / `FileInfo`
/// 那种得有个地方待。
///
/// **从前住在 `Ravel.Reflect` 插件里**，2026-10-07 搬进引擎 —— 因为
/// 「把一个 Ravel 值交给 .NET」和「按我要的类型把它转出来」是**语言层**的事，
/// 不该非 `using` 一个反射插件才有（见 `Object.ToDot` 与 `ToObject`）。
///
/// **它不实现 `IValue`**（那是内置值类型的名单，引擎/插件说了算，封着的）。</summary>
public record DotNetVal : ObjectVal
{
    public object? Value { get; init; }

    public DotNetVal(object? value, Scope? members = null)
        : base(BuiltinClasses.DotNetObject, members ?? new Scope())
        => Value = value;

    /// <summary>用**另一个类**造一个 —— **生成出来的适配层**用这条：它把 .NET 实例包成
    /// **自己那个 Ravel 类**（`[RavelClass("StringBuilder")]`），不是泛泛的 `DotNetObject`。
    ///
    /// 不这么做的话 `sb.Append "x"` 会报「类型 'DotNetObject' 没有方法 'Append'」——
    /// 值是 `DotNetObject`、方法挂在 `StringBuilder` 上，而成员查找沿**值自己的类**
    /// 往上走，走不到那条路（实测踩过）。生成器那边写成
    /// `new DotNetVal (…, PluginKit.ClassOf ("StringBuilder"))`。</summary>
    public DotNetVal(object? value, ClassVal classType, Scope? members = null)
        : base(classType, members ?? new Scope())
        => Value = value;

    public override string ToString()
        => ShowDepth.Guard(() => Value is null ? "DotNetObject (null)" : "DotNetObject " + NetBridge.Show(Value));

    /// <summary>**按目标类型转出来**（`Reflect.Call` 那种"不管你想要什么，我按自己那套给你"
    /// 的反面）。表在 <see cref="NetBridge.To"/>，插件能在 C# 层加规则
    /// （<see cref="NetBridge.Register"/>）。</summary>
    public object? ToObject(Type target, string what = "ToObject") => NetBridge.To(Value, target, what);
}
