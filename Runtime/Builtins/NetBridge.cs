using System.Collections;
using System.Numerics;

namespace Ravel.Runtime;

/// <summary>**Ravel 值 ↔ .NET 值**。三个方向：
///
/// * **出**（<see cref="ToNet"/>）：Ravel → `object`。认识的那几种转，`DotNetVal` 脱壳，
///   `list` → `object[]`；**Ravel 函数转不了**（那条硬边界，见下面那一支）。
/// * **进**（<see cref="FromNet"/> / <see cref="ToRavel"/>）：`FromNet` **一律包成
///   `DotNetVal`**，一个都不化 —— 化了**身份就掉**（`char[]` 变成 `list` 之后就再也
///   交不回去了）；`ToRavel` 是 `.Unwrap ()` 走的那条，认识的就化。
/// * **按目标类型转**（<see cref="To"/>）：`DotNetVal.ToObject T` 走这条。
///   前两条是"我按自己那套给你"，这条是"**你要什么我给你什么**" ——
///   生成的适配层要的正是后者（一个参数要 `IEnumerable<string>`，交个 `object[]`
///   过去就是运行期炸）。
///
/// 从前这三条都住在 `Ravel.Reflect` 插件里（那个 `Bridge`），2026-10-07 搬进引擎。</summary>
internal static class NetBridge
{
    /// <summary>Ravel → .NET。转不过去抛。</summary>
    public static object? ToNet(RuntimeValue v) => v switch
    {
        DotNetVal dn => dn.Value,
        DotNetTypeVal dt => dt.DotNetType,           // 类型本身也是值（`typeof`）
        IntVal i => i.Value,
        // **`bigint` 也得认** —— `long` 装得下就给 `long`，装不下明说：
        // .NET 那边过了 `long` 就没有"整数"这个形状了。少了这一格的话
        // `Reflect.New (T "System.TimeSpan") [864000000000000]` 那种（tick 数）
        // 会报「这个值转不成 .NET 的值：BigInt」—— 明明 .NET 那边要的就是个 `long`。
        BigIntVal b when b.Value >= long.MinValue && b.Value <= long.MaxValue => (long)b.Value,
        RealVal r => r.Value,
        BoolVal b => b.Value,
        StringVal s => s.Value,
        CharVal c => c.Value,
        VoidVal => null,
        DefaultVal => null,
        ListVal l => l.Elements.Select(ToNet).ToArray(),
        // **Ravel 函数交给 C#：做不到。** 引擎里写着三遍"原生闭包调不了 Ravel 函数
        // （那是帧栈的活）" —— 反过来（.NET 委托 → Ravel 函数）才行。这是那道硬边界
        // 唯一会露面的地方，所以把话说全。
        FunctionVal => throw PluginKit.Fail(
            "把 Ravel 函数交给 .NET 做不到 —— 原生闭包调不了 Ravel 函数（那是帧栈的活）。"
            + "要回调就在 .NET 那侧给一个委托", ErrorKind.Type),
        _ => throw PluginKit.Fail($"这个值转不成 .NET 的值：{v.Type}", ErrorKind.Type),
    };

    /// <summary>**`.NET → Ravel` 一律是 `DotNetObject`** —— 一个都不化。见类注释第二条。</summary>
    public static RuntimeValue FromNet(object? o) => new DotNetVal(o);

    /// <summary>**强制**化成 Ravel 值 —— `.Unwrap ()` 走这条。认识的就化，
    /// 不认识的兜成 `DotNetObject`（原地不动）。数组**也化**（"我要 Ravel 值，给我化掉"）。</summary>
    public static RuntimeValue ToRavel(object? o) => o switch
    {
        null => VoidVal.Instance,
        RuntimeValue rv => rv,                       // 已经是 Ravel 值（插件传回来的）
        bool b => new BoolVal(b),
        string s => new StringVal(s),
        char c => new CharVal(c),
        byte or sbyte or short or ushort or int => IntVal.Of(Convert.ToInt32(o)),
        uint u => IntVal.Of((int)u),
        long l => l is >= int.MinValue and <= int.MaxValue ? IntVal.Of((int)l) : new BigIntVal(l),
        float f => new RealVal(f),
        double d => new RealVal(d),
        decimal m => new RealVal((double)m),
        BigInteger bi => new BigIntVal(bi),
        // **`string` 也是 `IEnumerable`**，所以它得排在上面（已经接住了）
        IEnumerable seq => ListFromNet(seq),
        _ => new DotNetVal(o)
    };

    /// <summary>它长什么样（给 `Reflect.Text` 和报错用）。</summary>
    public static string Show(object? o) => o is null ? "()" : o.ToString() ?? o.GetType().Name;

    // ── 按目标类型转 ──
    //
    // 顺序是有讲究的：**先问注册表**，注册的能盖掉引擎自己那几条；内置那几条
    // 按"具体的先试"排（已经是它 → Nullable → 数组 → 泛型容器 → 枚举 → 数值）。

    /// <summary>目标类型 → 怎么转。**插件在 C# 层加**（`PluginKit.RegisterConverter`）；
    /// 引擎自己认得的那几种在 `To` 里写死。后注册的先问。</summary>
    private static readonly List<(Type Target, Func<object?, Type, object?> Conv)> Rules = [];

    /// <summary>加一条。`target` 可以是**开放泛型**（`typeof (IEnumerable<>)`）——
    /// 那就在"泛型定义对得上"的时候用它。</summary>
    public static void Register(Type target, Func<object?, Type, object?> conv)
        => Rules.Add((target, conv));

    /// <summary>把这个 .NET 值转成 `target`。转不过去**说清是什么、要什么**。</summary>
    public static object? To(object? v, Type target, string what)
    {
        if (v is null) return null;
        if (target == typeof(object)) return v;
        if (target.IsInstanceOfType(v)) return v;

        // 注册的规则：**后注册的先问**
        for (var i = Rules.Count - 1; i >= 0; i--)
        {
            var (t, conv) = Rules[i];
            if (t == target || (t.IsGenericTypeDefinition && target.IsGenericType
                                && target.GetGenericTypeDefinition() == t))
                return conv(v, target);
        }

        // `int?` 那种 —— 拆开当 `int` 转
        var under = Nullable.GetUnderlyingType(target);
        if (under is not null) return To(v, under, what);

        // `T[]` ← `object[]`（**元素逐个递归**，所以 `int[][]` 那种也通）
        if (target.IsArray && target.GetArrayRank() == 1 && v is object?[] arr)
        {
            var elem = target.GetElementType()!;
            var outp = Array.CreateInstance(elem, arr.Length);
            for (var i = 0; i < arr.Length; i++)
                outp.SetValue(To(arr[i], elem, $"{what} 的第 {i + 1} 个元素"), i);
            return outp;
        }

        // 泛型容器 ← `object[]`。`T[]` 本身就满足 `IEnumerable<T>` / `IList<T>` /
        // `ICollection<T>` / `IReadOnlyList<T>` / `IReadOnlyCollection<T>`，
        // 装得下就直接给它；`List<T>` 那几个接口的**具体类**才现造一个。
        if (v is object?[] seq && target.IsGenericType && target.GetGenericArguments().Length == 1)
        {
            var arg = target.GetGenericArguments()[0];
            var arrT = arg.MakeArrayType();
            if (target.IsAssignableFrom(arrT)) return To(seq, arrT, what);
            if (target.IsAssignableFrom(typeof(List<>).MakeGenericType(arg)))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(arg), To(seq, arrT, what));
        }

        // 枚举 ← 字符串 / 整数
        if (target.IsEnum)
        {
            if (v is string es) return Enum.Parse(target, es, ignoreCase: true);
            if (v is IConvertible) return Enum.ToObject(target, v);
        }

        // 数值那几家 —— `int` → `long` / `double` / `decimal` 都走这条
        if (v is IConvertible && typeof(IConvertible).IsAssignableFrom(target))
        {
            try { return Convert.ChangeType(v, target); }
            catch (Exception ex)
            {
                throw PluginKit.Fail($"{what}：{Show(v)} 转不成 {target.Name}（{ex.Message}）", ErrorKind.Type);
            }
        }

        throw PluginKit.Fail(
            $"{what}：{Show(v)}（{v.GetType().Name}）转不成 {target.Name} —— 这一型还没有转法。"
            + "插件可以在 C# 层 `PluginKit.RegisterConverter` 加一条", ErrorKind.Type);
    }

    private static ListVal ListFromNet(IEnumerable seq)
    {
        var items = new List<RuntimeValue>();
        foreach (var x in seq) items.Add(FromNet(x));
        return new ListVal(items);
    }
}
