namespace Ravel.Runtime;

/// <summary>数据结构那一族共用的几件小工具:从容器播种、键规范、排序用的比较器。
///
/// 都转发到 `PluginKit`(它背后就是引擎里那几处)—— 元素清单走 `foreach` / `Concat` 那个口径、
/// 键规范走 `dict` 那个规矩、比大小走 Ravel 的 `<`。于是这一族和语言里别的东西**同一套脾气**。</summary>
internal static class Structure
{
    /// <summary>构造器的参数:不给东西(`()` / `default`)就建空的;给一个容器就照它播种。</summary>
    public static List<RuntimeValue> Seed(RuntimeValue arg, string what) => arg switch
    {
        DefaultVal or VoidVal => [],
        _ => Elements(arg, $"{what} 的构造参数"),
    };

    public static List<RuntimeValue> Elements(RuntimeValue xs, string what) => PluginKit.Elements(xs, what);

    /// <summary>键:值类型(数 / 字符串),和 `dict` 一个规矩</summary>
    public static RuntimeValue Key(RuntimeValue k, string what) => PluginKit.Key(k, what);

    /// <summary>排序就照 Ravel 的 `<`(见 `Less`)—— 比不了当场报「比不了 X 与 Y」,
    /// 而不是让 .NET 的比较器在里面炸成 `InvalidOperationException`(那玩意儿 Ravel 的
    /// `try` 接不住)。</summary>
    private static readonly IComparer<RuntimeValue> Order = Comparer<RuntimeValue>.Create(
        (a, b) => PluginKit.Compare(a, b));    // `Compare` 本来就交 -1/0/1

    public static SortedDictionary<RuntimeValue, RuntimeValue> NewSortedDict() => new(Order);

    public static SortedSet<RuntimeValue> NewSortedSet() => new(Order);
}
