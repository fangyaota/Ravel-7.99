namespace Ravel.Runtime;

/// <summary>**`DotNetObject`** —— 一个任意 .NET 对象。反射交回来的东西装在这儿。
///
///     o := Reflect.New (Reflect.Type "System.Text.StringBuilder") []
///     o.Append "hi"          # 走 `Reflect.Call`
///     Reflect.Text o         # "hi"
///
/// 认识的那几种(int / string / 列表…)在 `Bridge` 里当场化成 Ravel 值,
/// 走不到这一类;落到这儿的都是"引擎不认识的"。</summary>
[RavelModule("Reflect")]
[RavelClass("DotNetObject")]
internal static class DotNetClass
{
    /// <summary>它的 .NET 类型(交一个 `DotNetType`)。
    /// **不叫 `Type`** —— 那个名字引擎自己占了(`x.Type` 是"它的类"),挂上去读不到。</summary>
    [ClassMethod("TypeOf")]
    public static RuntimeValue TypeOf(RuntimeValue self)
        => new DotNetTypeVal(Val(self).Value?.GetType() ?? typeof(object));

    /// <summary>`ToString ()`。</summary>
    [ClassMethod("Text")]
    public static RuntimeValue Text(RuntimeValue self) => new StringVal(Bridge.Show(Val(self).Value));

    /// <summary>**脱壳**:把里头的 .NET 值按 `Bridge` 的规矩交回 Ravel(认识的就化成 Ravel 值)。</summary>
    [ClassMethod("Unwrap")]
    public static RuntimeValue Unwrap(RuntimeValue self) => Bridge.ToRavel(Val(self).Value);

    private static DotNetVal Val(RuntimeValue self) => (DotNetVal)self;
}
