using System.Reflection;

namespace Ravel.Runtime;

/// <summary>**`Reflect` 模块** —— 最基本的那五个函数。
///
///     using "plugins/Ravel.Reflect.dll"
///
///     t := Reflect.Type "System.Console"
///     Reflect.Call t "WriteLine" ["hi"]        # 静态:头一个实参给**类型**
///     sb := Reflect.New (Reflect.Type "System.Text.StringBuilder") []
///     Reflect.Call sb "Append" ["hi"]          # 实例:头一个实参给**对象**
///     Reflect.Text sb
///
/// **重载按参数个数挑**(同名、参数个数对得上的第一个)。这条规矩很粗,但够用:
/// 它让 `WriteLine` / `Append` / `String.Join` 这些一调就通;挑错了会在**调用的那一刻**
/// 明说"给的是什么、要的是什么",不会静默跑错一个。
///
/// **不在这儿的**:泛型类型实参、委托方向(Ravel 函数交给 C# —— 硬边界)、
/// 按目标类型探路挑重载。见 `Runtime/Builtins/NetBridge.cs`。</summary>
[RavelModule("Reflect")]
internal static class ReflectModule
{
    /// <summary>类型名 → `DotNetType`。核心库里的写简称就行(`"System.Console"`)。</summary>
    [RavelFn("Type")]
    public static RuntimeValue Type(RuntimeValue name)
    {
        var n = PluginKit.Text(name, "Reflect.Type 的类型名").Value;
        var t = System.Type.GetType(n, throwOnError: false)
                ?? AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(n, throwOnError: false))
                    .FirstOrDefault(x => x is not null);
        if (t is null)
            throw PluginKit.Fail(
                $"找不到 .NET 类型 '{n}'。核心库写全名(`\"System.Console\"`);"
                + "别的程序集里的先把它 `using` 进来(插件的 dll 都在),再写**带程序集的全名**", ErrorKind.Name);
        return new DotNetTypeVal(t);
    }

    /// <summary>造一个:类型 + 实参表。</summary>
    [RavelFn("New")]
    public static RuntimeValue New(RuntimeValue t, RuntimeValue args)
    {
        var type = TypeOf(t, "Reflect.New 的类型").DotNetType;
        var argv = Args(args);
        var ctors = type.GetConstructors()
            .Where(c => c.GetParameters().Length == argv.Length).ToList();
        if (ctors.Count == 0)
            throw PluginKit.Fail(
                $"{type.Name} 没有 {argv.Length} 个参数的公开构造器。有的是:"
                + ArityList(type.GetConstructors().Select(c => c.GetParameters().Length)), ErrorKind.Argument);
        return TryEach(ctors, c => c.Invoke(argv), "构造 " + type.Name);
    }

    /// <summary>调方法 / 读属性。
    /// **第一个实参给 `DotNetType` 就是静态**,给 `DotNetObject` 就是实例 —— 和 C# 一样。</summary>
    [RavelFn("Call")]
    public static RuntimeValue Call(RuntimeValue obj, RuntimeValue name, RuntimeValue args)
    {
        var n = PluginKit.Text(name, "Reflect.Call 的名字").Value;
        var argv = Args(args);

        // **给类型就是静态**(和 C# 一样)。别拿 `IsAbstract` 去挡 —— `System.Math`
        // 这种"静态类"在 .NET 里就是 abstract sealed,挡一下 `Math.Max` 就用不了了。
        if (obj is DotNetTypeVal tv) return CallStatic(tv.DotNetType, n, argv);

        var target = PluginKit.ToNet(obj);
        return CallInstance(target!.GetType(), target, n, argv);
    }

    /// <summary>`ToString ()`,基本就是"给我看看"。Ravel 值也行。</summary>
    [RavelFn("Text")]
    public static RuntimeValue Text(RuntimeValue v) => new StringVal(PluginKit.NetShow(PluginKit.ToNet(v)));

    /// <summary>**脱壳**:`DotNetObject` 里的 .NET 值按 `Bridge` 的规矩交回 Ravel。别的值原样。</summary>
    [RavelFn("Unwrap")]
    public static RuntimeValue Unwrap(RuntimeValue v) => v is DotNetVal dn ? PluginKit.ToRavel(dn.Value) : v;

    // ═══ 帮手 ═══

    private static RuntimeValue CallStatic(Type type, string name, object?[] argv)
    {
        var pick = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == name && m.GetParameters().Length == argv.Length).ToList();
        if (pick.Count == 0) throw NoMember(type, name, argv.Length, static_: true);
        return TryEach(pick, m => m.Invoke(null, argv), type.Name + "." + name);
    }

    private static RuntimeValue CallInstance(Type type, object? target, string name, object?[] argv)
    {
        var pick = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == name && m.GetParameters().Length == argv.Length).ToList();
        if (pick.Count > 0)
            return TryEach(pick, m => m.Invoke(target, argv), type.Name + "." + name);

        // 属性(`s.Length` / `d.Value` 这种最常撞上)
        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop is not null && prop.GetIndexParameters().Length == argv.Length)
        {
            try { return Invoke(() => prop.GetValue(target, argv), type.Name + "." + name); }
            catch (ArgumentException ex) { throw BindFail(type.Name + "." + name, ex); }
        }

        throw NoMember(type, name, argv.Length, static_: false);
    }

    /// <summary>**同名、参数个数一样的重载依次试**:绑不上(`ArgumentException`,那是
    /// `Invoke` 自己抛的)就换下一个 —— `"x".Replace ["a" "b"]` 里 `(char, char)` 排在
    /// `(string, string)` 前面,不试就永远挑错那个。
    ///
    /// **被调方真抛的不吞**(`TargetInvocationException`):那是"这个方法跑了、跑出错了",
    /// 换个重载再接一遍只会把错盖掉。</summary>
    private static RuntimeValue TryEach<T>(List<T> pick, Func<T, object?> go, string what)
        where T : MethodBase
    {
        string? last = null;
        foreach (var m in pick)
        {
            try
            {
                return Invoke(() => go(m), what);
            }
            catch (ArgumentException ex)
            {
                last = ex.Message;
            }
        }
        throw PluginKit.Fail($"{what} 的实参对不上（{pick.Count} 个同参数个数的重载都试过）：{last}", ErrorKind.Type);
    }

    /// <summary>跑一下、把结果化成 Ravel 值。
    ///
    /// **`ArgumentException` 故意放过去**:那是"实参绑不上这个签名"(`Invoke` 自己抛的,
    /// 不是被调方抛的)—— `TryEach` 靠它换下一个重载。**被调方真抛的**
    /// (`TargetInvocationException`)在这儿翻成人话。</summary>
    private static RuntimeValue Invoke(Func<object?> go, string what)
    {
        try { return new DotNetVal(go()); }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException ?? ex;
            throw PluginKit.Fail($"{what} 的时候 .NET 抛了:{inner.GetType().Name}: {inner.Message}", ErrorKind.Value);
        }
    }

    /// <summary>绑不上这个签名时的那句话(没有重载可换的地方自己包一下)。</summary>
    private static RuntimeException BindFail(string what, ArgumentException ex)
        => PluginKit.Fail($"{what} 的实参对不上:{ex.Message}", ErrorKind.Type);

    private static RuntimeException NoMember(Type type, string name, int n, bool static_)
    {
        var scope = static_ ? "静态" : "实例";
        var arities = type.GetMethods(BindingFlags.Public | (static_ ? BindingFlags.Static : BindingFlags.Instance))
            .Where(m => m.Name == name).Select(m => m.GetParameters().Length).ToList();
        var more = arities.Count > 0
            ? $"有这个名字,但参数个数对不上。它有的是:{ArityList(arities)}"
            : $"没有这个名字。用 `Reflect.Type \"…\" |> .Members ()` 或 `t.Members ()` 列一下有什么";
        return PluginKit.Fail($"{type.Name} 上没有 {n} 个参数的{scope}成员 '{name}' —— {more}", ErrorKind.Attribute);
    }

    private static string ArityList(IEnumerable<int> arities)
    {
        var xs = arities.Distinct().OrderBy(x => x).Select(x => x + " 个").ToList();
        return xs.Count == 0 ? "（一个都没有）" : string.Join(" / ", xs);
    }

    private static DotNetTypeVal TypeOf(RuntimeValue v, string what) => v switch
    {
        DotNetTypeVal t => t,
        DotNetVal d => new DotNetTypeVal(d.Value?.GetType() ?? typeof(object)),
        _ => throw PluginKit.Fail($"{what}得是个 `DotNetType`（`Reflect.Type \"…\"` 交回的那种）", ErrorKind.Type)
    };

    /// <summary>参数表:收 `list`;`()` 当空的。</summary>
    private static object?[] Args(RuntimeValue v) => v switch
    {
        ListVal l => [.. l.Elements.Select(PluginKit.ToNet)],
        VoidVal or DefaultVal => [],
        _ => throw PluginKit.Fail($"参数表得写成 `list`（比如 `[\"hi\" 42]`，空的写 `[]`），得到 {v.Type}", ErrorKind.Argument)
    };
}
