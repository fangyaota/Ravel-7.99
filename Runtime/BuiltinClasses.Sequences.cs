namespace Ravel.Runtime;

using System.Linq;

/// <summary>容器上的**序列方法** —— 三种容器共用的一份,也就是将来 `IEnumerable` 那一层。
///
/// 为什么单独一份:这些都是"只跟**元素顺序**有关、跟容器本身是什么无关"的方法
/// (个数、查找、切片、聚合、变换)。写一份注册给三种容器,以后加 `IEnumerable` 时
/// 把注册点从三处挪到一处就行,**方法体一个字不用改**。
///
/// 现在这个文件只剩**C# 这一侧要用的帮手**(取元素 / 排序键 / 取整 / 比大小) ——
/// 方法本体已经搬进 `Runtime/Members/` 的成员工厂:
/// - 不收函数的(`Count` / `IsEmpty` / `Any` / `Contains` / `First` / …)在 `SequenceMembers` 里,
///   `[ClassMethod]`,C# 当场算;
/// - 收函数的(`Foreach` / `Map` / `Where` / `Fold` / `All` / `Any` / `Find` / `SortBy`)
///   同在那个类里,但值是 `SeqMethod`(`[ClassValue]`),得走控制帧 ——
///   原生闭包不能同步调 Ravel 函数(那是帧栈的活)。
///
/// 名字照 C# 的集合 / Linq API 起(`Select` → `Map`、`Aggregate` → `Fold`、`ToHashSet` → `ToSet`),
/// 语义也对齐:
/// - **变换和查询交回新的**,原容器不动;交回来的是 **list** —— 今天"一串值"就是 list,
///   要 Set 自己 `ToSet ()`(和 Linq 交回 `IEnumerable`、要什么再 `ToXxx` 一个道理);
/// - 顺序类的(`First` / `Last` / `Take` / `Skip` / `Reverse`)按**枚举顺序** ——
///   Set / Dict 的顺序是它们枚举器的顺序,别当插入顺序用;
/// - 比大小一律走 `<` 那个内置运算符(`Min` / `Max` / `Sort` 同一个口径):
///   数值之间能混着比、字符串按序比,别的类型**当场报错**,不静默给个 false。
internal static partial class BuiltinClasses
{
    /// <summary>任何容器的元素(按枚举顺序)。`Concat` 收别的容器时用它 ——
    /// 这是"C# 里接受 `IEnumerable`"这一步的临时形状,等接口出来了就换成接口。</summary>
    internal static List<RuntimeValue> ElementsOf(RuntimeValue v, string what) => v switch
    {
        ListVal l => [.. l.Elements],
        SetVal s => [.. s.Elements],
        DictVal d => [.. d.Entries.Values],
        // **插件带来的容器**(`using "x.dll"` 那些)引擎不认识它们的类型 —— 走**按名字**那条:
        // 谁有 `ToList ()` 谁就是容器。`Impl` 是同步的可以直接调(插件登记的方法就是它)。
        _ when v.MemberScope.LookupField("ToList")?.Value is BuiltinMethodVal m
              && m.Impl(v, VoidVal.Instance) is ListVal l => [.. l.Elements],
        _ => throw new RuntimeException($"{what} 需要一个容器（list / set / dict / 栈 队列 堆…），得到 {v.Type}", ErrorKind.Argument),
    };

    /// <summary>按键排序(**稳定** —— 相等的元素保持原来的先后,`OrderBy` 就是稳定的)。
    /// 排之前拿第一个当尺子量一遍:`Min`/`Max`/`Sort` 比不了时要说人话
    /// (`[1 "a"].Sort ()` → 「比不了 Integer 与 String」),而不是让比较器在 .NET 里炸 ——
    /// 那边会把异常包成 `InvalidOperationException("Failed to compare two elements…")`,
    /// 那不是 RuntimeException,Ravel 层的 `try` 接不住,一路漏到顶层打成"解释器内部错误"。
    ///
    /// `key` 是取排序键(`Sort` 就是元素自己,`SortBy` 是用户那个函数交回来的)。</summary>
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

    /// <summary>第 n 个元素,越界报错(空的 First/Last 是**错误**,不是 `()` ——
    /// 「没有第一个」和「第一个是空」不该长得一样)。</summary>
    internal static int IntArg(RuntimeValue a, string what)
        => a is IntVal i
            ? i.Value
            : throw new RuntimeException($"{what} 需要 int 参数，得到 {a.Type}", ErrorKind.Argument);

    /// <summary>比大小:走 `<` 那个内置运算符 —— `Min` / `Max` / `Sort` / `SortBy` 同一个口径。
    /// 比不了就**报错**(不是给个 false、也不是给个元素),而且**两边都说出来**:
    /// 底下的运算符只会说"我不支持 X 操作数",而调用方问的是"这两个能不能比",
    /// `[1 "a"].Max ()` 得一眼看出是 Integer 和 String 撞上了。</summary>
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
