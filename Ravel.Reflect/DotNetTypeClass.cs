namespace Ravel.Runtime;

/// <summary>**`DotNetType`** —— 一个 .NET 类型(`System.Type`)。
///
/// 名字不叫 `Type`:那个被 Ravel 自己占了(`typeof` 交回的就是它)。
///
///     t := Reflect.Type "System.Console"
///     t.Name          # "Console"
///     Reflect.Call t "WriteLine" ["hi"]      # 静态方法:头一个实参给"类型"就是静态</summary>
[RavelModule("Reflect")]
[RavelClass("DotNetType")]
internal static class DotNetTypeClass
{
    [ClassMethod("Name")]
    public static RuntimeValue Name(RuntimeValue self) => new StringVal(Val(self).DotNetType.Name);

    [ClassMethod("FullName")]
    public static RuntimeValue FullName(RuntimeValue self)
        => new StringVal(Val(self).DotNetType.FullName ?? Val(self).DotNetType.Name);

    [ClassMethod("Text")]
    public static RuntimeValue Text(RuntimeValue self) => new StringVal(Bridge.Show(Val(self).DotNetType));

    /// <summary>是不是**值类型** / 枚举 / 接口 —— 探索的时候顺手。</summary>
    [ClassMethod("IsValue")]
    public static RuntimeValue IsValue(RuntimeValue self) => new BoolVal(Val(self).DotNetType.IsValueType);

    [ClassMethod("IsInterface")]
    public static RuntimeValue IsInterface(RuntimeValue self) => new BoolVal(Val(self).DotNetType.IsInterface);

    /// <summary>**把这个类型当成一个对象** —— 交回 `DotNetObject`(里头就是那个 `System.Type`)。
    ///
    /// `DotNetType` 的意思是"**静态访问的把手**":`Reflect.Call t "Max" […]` 在它身上找的是
    /// **静态**成员。而 `Type` 自己的那些**实例**成员(`GetMethods` / `GetParameters` /
    /// `IsSpecialName` …)就够不着了 —— 想扫一个类型的全部方法,要的恰恰是它们。
    /// 这一格就是那道门:
    ///
    ///     t := Reflect.Type "System.Math"
    ///     o := t.AsObject ()                 # 现在是个对象了
    ///     Reflect.Unwrap (Reflect.Call o "GetMethods" [])</summary>
    [ClassMethod("AsObject")]
    public static RuntimeValue AsObject(RuntimeValue self) => new DotNetVal(Val(self).DotNetType);

    /// <summary>它的公开成员名(不重名、排过序)—— 不知道有什么可调的时候先列这个。</summary>
    [ClassMethod("Members")]
    public static RuntimeValue Members(RuntimeValue self)
    {
        var names = Val(self).DotNetType
            .GetMembers(System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal);
        return new ListVal([.. names.Select(x => (RuntimeValue)new StringVal(x))]);
    }

    private static DotNetTypeVal Val(RuntimeValue self) => (DotNetTypeVal)self;
}
