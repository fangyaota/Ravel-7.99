namespace Ravel.Runtime;

/// <summary>**`DotNetObject` / `DotNetType` 两个类 + `Object.ToDot`** ——
/// "把手里的值交给 .NET"这一层。
///
/// 2026-10-07 从 `Ravel.Reflect` 插件搬进引擎。为什么要搬：`DotNetVal` 一造出来就要
/// **按名字拿 `DotNetObject` 那个类**（`ObjectVal` 的基类 ctor 收的是 `ClassVal`），
/// 类不在引擎里的话，没 `using` 那个插件的场合连**造都造不出来** —— 于是
/// `Object.ToDot ()` 根本没法挂在根类上。搬过来之后 `Ravel.Reflect` 只剩
/// `Reflect` 模块那五个函数（`Type` / `New` / `Call` / `Text` / `Unwrap`）。
///
/// **`ToDot` 为什么挂在根类 `Object` 上**：它不是"某几种类型的事" —— 任何值都能
/// （也才应该能）交给 .NET，`5` 和 `[1 2]` 在这件事上没有理由不一样。挂在根类上，
/// 这一份成员就在**一个地方**。
///
/// **两个方向分开**：`ToDot` 是"我按自己那套给你"（`NetBridge.ToNet`），
/// `ToObject 类型` 是"**你要什么我给你什么**"（`NetBridge.To`，表可在 C# 层注册）——
/// 生成出来的适配层要的正是后者：参数要 `IEnumerable<string>`，交个 `object[]` 过去
/// 就是运行期炸。</summary>
internal static partial class BuiltinClasses
{
    private static void RegisterNetMethods()
    {
        // **`Object.ToDot ()`** —— Ravel 值 → `DotNetObject`。转不过去的那一支
        // （Ravel 函数）照样抛，消息在 `NetBridge.ToNet` 里。
        Object.DefineMethod("ToDot", (s, _) => new DotNetVal(NetBridge.ToNet(s)));

        // ── DotNetObject：一个任意 .NET 对象 ──
        //
        // 方法名都避开引擎自己占的那几个 —— `Type` 被占了（`x.Type` 是"它的类"），
        // 挂上去读不到（从前那个 `Type` → `TypeOf` 的改名就是这么来的）。
        DotNetObject.DefineMethod("TypeOf", (s, _) =>
            new DotNetTypeVal(V(s).Value?.GetType() ?? typeof(object)));
        DotNetObject.DefineMethod("Text", (s, _) => new StringVal(NetBridge.Show(V(s).Value)));
        DotNetObject.DefineMethod("Unwrap", (s, _) => NetBridge.ToRavel(V(s).Value));

        // **`.ToObject 类型`** —— 按目标类型转出来。
        DotNetObject.DefineMethod("ToObject", (s, a) =>
        {
            var t = AsType(a, "DotNetObject.ToObject 的类型");
            return NetBridge.FromNet(V(s).ToObject(t, "DotNetObject.ToObject"));
        });

        // ── DotNetType：一个 System.Type ──
        DotNetType.DefineMethod("Name", (s, _) => new StringVal(T(s).Name));
        DotNetType.DefineMethod("FullName", (s, _) => new StringVal(T(s).FullName ?? T(s).Name));
        DotNetType.DefineMethod("Text", (s, _) => new StringVal(NetBridge.Show(T(s))));
        DotNetType.DefineMethod("IsValue", (s, _) => new BoolVal(T(s).IsValueType));
        DotNetType.DefineMethod("IsInterface", (s, _) => new BoolVal(T(s).IsInterface));

        // **`.AsObject ()`** —— 把这个类型**当成一个对象**拿回来。
        // `DotNetType` 是"静态访问的把手"（`Reflect.Call` 在它身上找的是**静态**成员），
        // 而 `GetMethods` / `GetParameters` 这些是 `Type` 自己的**实例**成员，够不着它。
        // 想扫一个类型的全部方法，要的恰恰是它们。
        DotNetType.DefineMethod("AsObject", (s, _) => new DotNetVal(T(s)));

        // 公开成员名（不重名、排过序）—— 不知道有什么可调的时候先列这个。
        DotNetType.DefineMethod("Members", (s, _) =>
        {
            var names = T(s)
                .GetMembers(System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.Static)
                .Select(m => m.Name).Distinct().OrderBy(x => x, StringComparer.Ordinal);
            return new ListVal([.. names.Select(x => (RuntimeValue)new StringVal(x))]);
        });
    }

    private static DotNetVal V(RuntimeValue self) => (DotNetVal)self;
    private static Type T(RuntimeValue self) => ((DotNetTypeVal)self).DotNetType;

    /// <summary>收一个"类型"参数 —— `.ToObject` 那一格要。两种都收：
    /// `DotNetType`（`Reflect.Type` 交回的那种）和包着 `Type` 的 `DotNetObject`
    /// （`.AsObject ()` / `.TypeOf ()` 交回的那种）。别的当场报。</summary>
    private static Type AsType(RuntimeValue a, string what) => a switch
    {
        DotNetTypeVal t => t.DotNetType,
        DotNetVal { Value: Type ty } => ty,
        _ => throw new RuntimeException(
            $"{what} 得是个类型（`Reflect.Type \"…\"` 交回的那种，或者 `.AsObject ()` 包回来的），得到 {a.Type}",
            ErrorKind.Type),
    };
}
