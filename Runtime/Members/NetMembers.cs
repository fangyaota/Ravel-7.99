namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

/// <summary>**`DotNetObject` / `DotNetType` 两个类的成员。**
///
/// 从前在 `BuiltinClasses.Net.cs` 的 `RegisterNetMethods()` 里一条条 `DefineMethod` 挂上去
/// (一张外置的注册表)。现在和 `ObjectMembers` 一个写法:成员就在**这个类**里,
/// 静态方法 + `[ClassMethod]`,由 `MemberSet.Scan` 反射一次收齐。
///
/// 辅助那几条(`V` / `T` / `AsType`、`NetBridge`)留在引擎那一侧 —— 和 `TextArg` / `IntArg`
/// 一个性质:它们不隶属于"某个类的实例"。
///
/// 2026-10-07 从 `Ravel.Reflect` 插件搬进引擎。为什么要搬:`DotNetVal` 一造出来就要
/// **按名字拿 `DotNetObject` 那个类**(`ObjectVal` 的基类 ctor 收的是 `ClassVal`),
/// 类不在引擎里的话,没 `using` 那个插件的场合连**造都造不出来** —— 于是
/// `Object.ToDot ()` 根本没法挂在根类上。搬过来之后 `Ravel.Reflect` 只剩
/// `Reflect` 模块那五个函数(`Type` / `New` / `Call` / `Text` / `Unwrap`)。
///
/// **`Object.ToDot` 不在这儿**:它是 `Object` 的实例成员 —— 任何值都能(也才应该能)交给
/// .NET,`5` 和 `[1 2]` 在这件事上没有理由不一样,所以它属于 `ObjectMembers`。
/// (眼下它还在 `BuiltinClasses.Net.cs` 里注册着,等 `ObjectMembers.cs` 收编。)
///
/// **两个方向分开**:`ToDot` 是"我按自己那套给你"(`NetBridge.ToNet`),
/// `ToObject 类型` 是"**你要什么我给你什么**"(`NetBridge.To`,表可在 C# 层注册)——
/// 生成出来的适配层要的正是后者:参数要 `IEnumerable<string>`,交个 `object[]` 过去
/// 就是运行期炸。

/// <summary>**`DotNetObject`:一个任意 .NET 对象。**
///
/// 方法名都避开引擎自己占的那几个 —— `Type` 被占了(`x.Type` 是"它的类"),
/// 挂上去读不到(从前那个 `Type` → `TypeOf` 的改名就是这么来的)。</summary>
internal static class DotNetObjectMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(DotNetObjectMembers), ObjectMembers.Set);

    /// <summary>`x.TypeOf ()` —— 这个 .NET 对象**实际**是什么类型(交回 `DotNetType`)。
    /// 装了个 null 的 `DotNetVal` 给 `object`。</summary>
    [ClassMethod("TypeOf")]
    private static RuntimeValue TypeOf(RuntimeValue self)
        => new DotNetTypeVal(BuiltinClasses.V(self).Value?.GetType() ?? typeof(object));

    /// <summary>`x.Text ()` —— `NetBridge.Show` 那份给人看的文本(`null` 也说得清)。</summary>
    [ClassMethod("Text")]
    private static RuntimeValue Text(RuntimeValue self) => new StringVal(NetBridge.Show(BuiltinClasses.V(self).Value));

    /// <summary>`x.Unwrap ()` —— 认识的就化成 Ravel 值,不认识的照旧包着(`NetBridge.ToRavel`)。</summary>
    [ClassMethod("Unwrap")]
    private static RuntimeValue Unwrap(RuntimeValue self) => NetBridge.ToRavel(BuiltinClasses.V(self).Value);

    /// <summary>**`.ToObject 类型`** —— 按目标类型转出来。</summary>
    [ClassMethod("ToObject")]
    private static RuntimeValue ToObject(RuntimeValue self, RuntimeValue arg)
    {
        var t = BuiltinClasses.AsType(arg, "DotNetObject.ToObject 的类型");
        return NetBridge.FromNet(BuiltinClasses.V(self).ToObject(t, "DotNetObject.ToObject"));
    }
}

/// <summary>**`DotNetType`:一个 `System.Type`。** 静态访问的把手(`Reflect.Call` 在它身上
/// 找的是**静态**成员),所以"这个类型自己有哪些实例成员"要另外走 `.AsObject ()`。</summary>
internal static class DotNetTypeMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(DotNetTypeMembers), ObjectMembers.Set);

    [ClassMethod("Name")]
    private static RuntimeValue Name(RuntimeValue self) => new StringVal(BuiltinClasses.T(self).Name);

    [ClassMethod("FullName")]
    private static RuntimeValue FullName(RuntimeValue self) => new StringVal(BuiltinClasses.T(self).FullName ?? BuiltinClasses.T(self).Name);

    [ClassMethod("Text")]
    private static RuntimeValue Text(RuntimeValue self) => new StringVal(NetBridge.Show(BuiltinClasses.T(self)));

    [ClassMethod("IsValue")]
    private static RuntimeValue IsValue(RuntimeValue self) => new BoolVal(BuiltinClasses.T(self).IsValueType);

    [ClassMethod("IsInterface")]
    private static RuntimeValue IsInterface(RuntimeValue self) => new BoolVal(BuiltinClasses.T(self).IsInterface);

    /// <summary>**`.AsObject ()`** —— 把这个类型**当成一个对象**拿回来。
    /// `DotNetType` 是"静态访问的把手"(`Reflect.Call` 在它身上找的是**静态**成员),
    /// 而 `GetMethods` / `GetParameters` 这些是 `Type` 自己的**实例**成员,够不着它。
    /// 想扫一个类型的全部方法,要的恰恰是它们。</summary>
    [ClassMethod("AsObject")]
    private static RuntimeValue AsObject(RuntimeValue self) => new DotNetVal(BuiltinClasses.T(self));

    /// <summary>公开成员名(不重名、排过序)—— 不知道有什么可调的时候先列这个。</summary>
    [ClassMethod("Members")]
    private static RuntimeValue Members(RuntimeValue self)
    {
        var names = BuiltinClasses.T(self)
            .GetMembers(System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static)
            .Select(m => m.Name).Distinct().OrderBy(x => x, StringComparer.Ordinal);
        return new ListVal([.. names.Select(x => (RuntimeValue)new StringVal(x))]);
    }
}
