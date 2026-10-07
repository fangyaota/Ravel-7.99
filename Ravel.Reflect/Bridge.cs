using System.Collections;
using System.Numerics;

namespace Ravel.Runtime;

/// <summary>**Ravel 值 ↔ .NET 值**。最基本的那一版,只认调方法时最常撞上的几种:
///
/// * Ravel → .NET:`int` / `real` / `bool` / `string` / `char` / `()` / `default` / `list`(→ `object[]`)
/// * .NET → Ravel:上面那几种 + 任何一串(→ `list`);**别的兜成 `DotNetObject`**,不报错
///
/// **不做的**:泛型序列按元素类型逐个转、字典、按目标类型探路挑重载、委托方向
/// (Ravel 函数交给 C# —— 那条是硬边界,见下面 `FunctionVal` 那一支)。
/// 缺的这几种不会静默:转不过去会明说"给的是什么、要的是什么"。</summary>
internal static class Bridge
{
    /// <summary>Ravel → .NET。转不过去抛。</summary>
    public static object? ToNet(RuntimeValue v) => v switch
    {
        DotNetVal dn => dn.Value,
        DotNetTypeVal dt => dt.DotNetType,           // 类型本身也是值(`typeof`)
        IntVal i => i.Value,
        RealVal r => r.Value,
        BoolVal b => b.Value,
        StringVal s => s.Value,
        CharVal c => c.Value,
        VoidVal => null,
        DefaultVal => null,
        ListVal l => l.Elements.Select(ToNet).ToArray(),
        // **Ravel 函数交给 C#:做不到。** 引擎里写着三遍"原生闭包调不了 Ravel 函数
        // (那是帧栈的活)" —— 反过来(.NET 委托 → Ravel 函数)才行。这是那道硬边界
        // 唯一会露面的地方,所以把话说全。
        FunctionVal => throw PluginKit.Fail(
            "把 Ravel 函数交给 .NET 做不到 —— 原生闭包调不了 Ravel 函数（那是帧栈的活）。"
            + "要回调就在 .NET 那侧给一个委托", ErrorKind.Type),
        _ => throw PluginKit.Fail($"这个值转不成 .NET 的值:{v.Type}", ErrorKind.Type),
    };

    /// <summary>.NET → Ravel。**不认识的不报错**,兜成 `DotNetObject`。</summary>
    public static RuntimeValue FromNet(object? o) => o switch
    {
        null => VoidVal.Instance,
        RuntimeValue rv => rv,                       // 已经是 Ravel 值(插件传回来的)
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
        // **`string` 也是 `IEnumerable`**,所以它得排在上面(已经接住了)
        IEnumerable seq => ListFromNet(seq),
        _ => new DotNetVal(o)
    };

    /// <summary>它长什么样(给 `Reflect.Text` 和报错用)。</summary>
    public static string Show(object? o) => o is null ? "()" : o.ToString() ?? o.GetType().Name;

    private static ListVal ListFromNet(IEnumerable seq)
    {
        var items = new List<RuntimeValue>();
        foreach (var x in seq) items.Add(FromNet(x));
        return new ListVal(items);
    }
}
