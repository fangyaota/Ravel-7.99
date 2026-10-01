namespace Ravel.Runtime;

using System.Linq;

/// <summary>容器上的**序列方法** —— 三种容器共用的一份,也就是将来 `IEnumerable` 那一层。
///
/// 为什么单独一份:这些都是"只跟**元素顺序**有关、跟容器本身是什么无关"的方法
/// (个数、查找、切片、聚合、变换)。写一份注册给三种容器,以后加 `IEnumerable` 时
/// 把注册点从三处挪到一处就行,**方法体一个字不用改**。
///
/// 分两批:
/// - **不收函数**的(`Count` / `IsEmpty` / `Any` / `Contains` / `First` / `Last` / `Sum` /
///   `Min` / `Max` / `Join` / `ToList` / `ToSet` / `Distinct` / `Reverse` / `Take` / `Skip` /
///   `Concat`)在这个文件里,C# 当场算;
/// - **收函数**的(`Each` / `Map` / `Where` / `Fold` / `All` / `Find` / `Sort p`)**不在这儿** ——
///   原生闭包不能同步调 Ravel 函数(那是帧栈的活),它们得走控制帧,见 BuiltinClasses.HigherOrder.cs。
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
    /// <summary>把这一批方法注册到一种容器上。`items` 就是将来 `IEnumerable` 要接口实现
    /// 回答的那个问题:「按枚举顺序把元素给我」。</summary>
    private static void RegisterSequenceMethods(ObjectVal type, Func<RuntimeValue, List<RuntimeValue>> items)
    {
        // `Count` / `IsEmpty` / `Any` —— 三种容器本来各有一份 `Count`,现在并到这儿
        type.DefineMethod("Count", (s, _) => new IntVal(items(s).Count));
        type.DefineMethod("IsEmpty", (s, _) => new BoolVal(items(s).Count == 0));
        type.DefineMethod("First", (s, _) => Nth(items(s), 0, "First"));
        type.DefineMethod("Last", (s, _) => Nth(items(s), items(s).Count - 1, "Last"));
        // 元素里有没有它:原子值按值比、容器按身份比(和 Set 本身的判据一致)。
        // 字典的"元素"是**值**(见 items),按键找用 `Has k`。
        type.DefineMethod("Contains", (s, a) => new BoolVal(items(s).Contains(a)));

        type.DefineMethod("ToList", (s, _) => new ListVal(items(s)));
        type.DefineMethod("ToSet", (s, _) => new SetVal([.. items(s)]));
        type.DefineMethod("Distinct", (s, _) =>
        {
            var seen = new HashSet<RuntimeValue>();
            var kept = new List<RuntimeValue>();
            foreach (var x in items(s))
                if (seen.Add(x))
                    kept.Add(x);
            return new ListVal(kept);
        });
        type.DefineMethod("Reverse", (s, _) =>
        {
            var xs = items(s);
            xs.Reverse();
            return new ListVal(xs);
        });
        type.DefineMethod("Take", (s, a) => new ListVal(items(s).Take(IntArg(a, "Take")).ToList()));
        type.DefineMethod("Skip", (s, a) => new ListVal(items(s).Skip(IntArg(a, "Skip")).ToList()));
        type.DefineMethod("Concat", (s, a) => new ListVal([.. items(s), .. ElementsOf(a, "Concat")]));

        type.DefineMethod("Join", (s, a) =>
        {
            if (a is not StringVal sep) throw new RuntimeException("Join 需要 string 分隔符", ErrorKind.Argument);
            return new StringVal(string.Join(sep.Value, items(s).Select(x => x.ToString())));
        });
        type.DefineMethod("Sum", (s, _) =>
        {
            RuntimeValue acc = new IntVal(0);          // 空集合求和给 0(C# 的 Sum() 也是这样)
            foreach (var x in items(s)) acc = ApplyOp("+", acc, x);
            return acc;
        });
        type.DefineMethod("Min", (s, _) => Extreme(items(s), "Min", keepLess: true));
        type.DefineMethod("Max", (s, _) => Extreme(items(s), "Max", keepLess: false));
    }

    /// <summary>高阶那批:收 Ravel 函数的那些。它们的成员值不是普通内置方法,而是
    /// <see cref="SeqMethod"/> —— 读出来绑好接收者,交出去的是个**控制帧**
    /// (原生闭包调不了 Ravel 函数),见 Interpreter.Control.cs 的 `StepSeqOp`。
    ///
    /// 名字照 Linq:`Select` → `Map`、`Aggregate(seed, f)` → `Fold`、`OrderBy` → `SortBy`、
    /// `Any(pred)` → `Any p`(和 `Any ()` 共用一个名字)、`First(pred)` → `Find`。</summary>
    private static void RegisterHigherOrderMethods(ObjectVal type)
    {
        DefineSeq(type, "Each", SeqMode.Each, 3);
        DefineSeq(type, "Map", SeqMode.Map, 3);
        DefineSeq(type, "Where", SeqMode.Where, 3);
        DefineSeq(type, "All", SeqMode.All, 3);
        DefineSeq(type, "Any", SeqMode.Any, 3);
        DefineSeq(type, "Find", SeqMode.Find, 3);
        DefineSeq(type, "SortBy", SeqMode.SortBy, 3);
        DefineSeq(type, "Fold", SeqMode.Fold, 4);      // 收两个:初值 + 函数(和 Aggregate(seed, f) 一致)
    }

    /// <summary>高阶方法也是**引擎给这一类挂的成员**:走 <see cref="EngineMember"/> 落到
    /// 实例表(`SeqMethod` 不是 `BuiltinMethodVal`,所以没走 DefineMethod 那条路)。</summary>
    private static void DefineSeq(ObjectVal type, string name, SeqMode mode, int arity)
        => EngineMember(type, name, new SeqMethod(mode, arity) { Name = name });

    /// <summary>任何容器的元素(按枚举顺序)。`Concat` 收别的容器时用它 ——
    /// 这是"C# 里接受 `IEnumerable`"这一步的临时形状,等接口出来了就换成接口。</summary>
    internal static List<RuntimeValue> ElementsOf(RuntimeValue v, string what) => v switch
    {
        ListVal l => [.. l.Elements],
        SetVal s => [.. s.Elements],
        DictVal d => [.. d.Entries.Values],
        _ => throw new RuntimeException($"{what} 需要 list / set / dict，得到 {v.Type}", ErrorKind.Argument),
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
    private static RuntimeValue Nth(List<RuntimeValue> xs, int i, string what)
        => i >= 0 && i < xs.Count
            ? xs[i]
            : throw new RuntimeException($"{what}: 元素不够（一共 {xs.Count} 个）", ErrorKind.Index);

    internal static int IntArg(RuntimeValue a, string what)
        => a is IntVal i
            ? i.Value
            : throw new RuntimeException($"{what} 需要 int 参数，得到 {a.Type}", ErrorKind.Argument);

    /// <summary>把内置运算符当普通函数用(只在 C# 这一侧)。成员表里那格是
    /// <see cref="BuiltinMethodVal"/>,它的体是同步的可以直接调;查不到、或者被类运算符
    /// 换成了要推帧的东西(用户类自己写的 `+`)就当场报错 —— 别静默给个错答案。</summary>
    private static RuntimeValue ApplyOp(string op, RuntimeValue a, RuntimeValue b)
        => a.MemberScope.LookupField(op)?.Value is BuiltinMethodVal m
            ? m.Impl(a, b)
            : throw new RuntimeException($"'{op}' 不支持 {a.Type}（{a.Type} 与 {b.Type} 之间）", ErrorKind.Type);

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

    /// <summary>最小 / 最大。相等时留**先出现**的那个(稳定)。</summary>
    private static RuntimeValue Extreme(List<RuntimeValue> xs, string what, bool keepLess)
    {
        if (xs.Count == 0) throw new RuntimeException($"{what}: 空集合没有{what}", ErrorKind.Index);
        var best = xs[0];
        foreach (var x in xs.Skip(1))
            if (keepLess ? Less(x, best) : Less(best, x))
                best = x;
        return best;
    }
}
