namespace Ravel.Runtime;

internal static class Structure
{
    public static List<RuntimeValue> Seed(RuntimeValue arg, string what) => arg switch
    {
        VoidVal => [],
        _ => Elements(arg, $"{what} 的构造参数"),
    };

    public static List<RuntimeValue> Elements(RuntimeValue xs, string what) => PluginKit.Elements(xs, what);

    public static RuntimeValue Key(RuntimeValue k, string what) => PluginKit.Key(k, what);

    private static readonly IComparer<RuntimeValue> Order = Comparer<RuntimeValue>.Create(
        (a, b) => PluginKit.Compare(a, b));    // `Compare` 本来就交 -1/0/1

    public static SortedDictionary<RuntimeValue, RuntimeValue> NewSortedDict() => new(Order);

    public static SortedSet<RuntimeValue> NewSortedSet() => new(Order);
}
