namespace Ravel.Runtime;

using System.Linq;

internal static partial class BuiltinClasses
{
    internal static List<RuntimeValue> ElementsOf(RuntimeValue v, string what) => v switch
    {
        ListVal l => [.. l.Elements],
        SetVal s => [.. s.Elements],
        // dict 当序列 = 一串 `[键 值]`(与 `ToList` / `Foreach` / `ToDict` 同一个口径)
        DictVal d => [.. d.Entries.Select(e => (RuntimeValue)new ListVal([e.Key, e.Value]))],
        // **插件带来的容器**(`using "x.dll"` 那些)引擎不认识它们的类型 —— 走**按名字**那条:
        // 谁有 `ToList ()` 谁就是容器。`Impl` 是同步的可以直接调(插件登记的方法就是它)。
        _ when v.MemberScope.LookupField("ToList")?.Value is BuiltinMethodVal m
              && m.Impl(v, VoidVal.Instance) is ListVal l => [.. l.Elements],
        _ => throw new RuntimeException($"{what} 需要一个容器（list / set / dict / 栈 队列 堆…），得到 {v.Type}", ErrorKind.Argument),
    };

    internal static ListVal SortByKey(IEnumerable<RuntimeValue> xs, Func<RuntimeValue, RuntimeValue> key)
    {
        var list = xs.ToList();
        if (list.Count > 0)
            foreach (var x in list)
                Less(key(x), key(list[0]));      // 先把"比不了"挑出来

        var cmp = Comparer<RuntimeValue>.Create((a, b) =>
            Less(key(a), key(b)) ? -1 : Less(key(b), key(a)) ? 1 : 0);
        try
        {
            return new ListVal([.. list.OrderBy(x => x, cmp)]);
        }
        catch (InvalidOperationException)
        {
            throw new RuntimeException("排序时比不了:元素之间类型不一致", ErrorKind.Type);
        }
    }

    internal static int IntArg(RuntimeValue a, string what)
        => a is IntVal i
            ? i.Value
            : throw new RuntimeException($"{what} 需要 int 参数，得到 {a.Type}", ErrorKind.Argument);

    internal static bool Less(RuntimeValue a, RuntimeValue b)
    {
        if (a.MemberScope.LookupField("<")?.Value is BuiltinMethodVal m)
            try
            {
                if (m.Impl(a, b) is BoolVal r) return r.Value;
            }
            catch (RuntimeException)
            {
                // 落到下面统一报"比不了 X 与 Y"(运算符自己的那句话只说了一半)
            }

        throw new RuntimeException($"比不了 {a.Type} 与 {b.Type}", ErrorKind.Type);
    }
}
