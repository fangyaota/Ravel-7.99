namespace Ravel.Runtime;

/// <summary>数据结构那一族共用的几件小工具:从容器播种、键规范、排序用的比较器。
///
/// 都转发到引擎本来就有的那几处 —— 元素清单走 `ElementsOf`(和 `foreach` / `Concat` 一个口径)、
/// 键规范走 `KeyArg`(和 `dict` 一个规矩:键得是值类型)、比大小走 `Less`(Ravel 的 `<`)。
/// 于是这一族结构和语言里别的东西**同一套脾气**,不是各写一份。</summary>
internal static class Structure
{
    /// <summary>构造器的参数:不给东西(`()` / `default`)就建空的;给一个容器就照它播种。</summary>
    public static List<RuntimeValue> Seed(RuntimeValue arg, string what) => arg switch
    {
        DefaultVal or VoidVal => [],
        _ => Elements(arg, $"{what} 的构造参数"),
    };

    public static List<RuntimeValue> Elements(RuntimeValue xs, string what) => BuiltinClasses.ElementsOf(xs, what);

    /// <summary>键:值类型(数 / 字符串),和 `dict` 一个规矩</summary>
    public static RuntimeValue Key(RuntimeValue k, string what) => BuiltinClasses.KeyArg(k, what);

    /// <summary>排序就照 Ravel 的 `<`(见 `Less`)—— 比不了当场报「比不了 X 与 Y」,
    /// 而不是让 .NET 的比较器在里面炸成 `InvalidOperationException`(那玩意儿 Ravel 的
    /// `try` 接不住)。</summary>
    private static readonly IComparer<RuntimeValue> Order = Comparer<RuntimeValue>.Create(
        (a, b) => BuiltinClasses.Less(a, b) ? -1 : BuiltinClasses.Less(b, a) ? 1 : 0);

    public static SortedDictionary<RuntimeValue, RuntimeValue> NewSortedDict() => new(Order);

    public static SortedSet<RuntimeValue> NewSortedSet() => new(Order);
}
