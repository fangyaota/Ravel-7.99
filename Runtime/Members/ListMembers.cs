namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`List` 这个类给它的实例准备什么。**
///
/// 只有**列表才有**的那些:要么带下标,要么依赖"顺序是有意义的"。
/// 集合/字典没有这条 —— 所以它们只在序列方法那一批里出现(见 `SequenceMembers`)。
///
/// 其余的(`Count` / `First` / `Contains` / `Sum` …)从 `SequenceMembers` 继承而来:
/// 那是 `abstract class`,本类继承它,`MemberSet.Scan` 沿 C# 基类往上扫,
/// 于是三种容器共用**同一份实现**、不必各写一遍。
///
/// 从前这些挤在 `RegisterListMethods` 里,体里用的是本地闭包 `RemoveAt`;
/// 现在那两样成为本类的 `private static` helper —— 没有 `[ClassMethod]` 的静态方法
/// 会被 `MemberSet.Scan` 忽略,所以 helper 可以和成员方法并排放着。</summary>
internal sealed class ListMembers : SequenceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ListMembers), ObjectMembers.Set);

    /// <summary>取下标之前的统一检查:收下索引、卡边界,顺手把列表交回去。
    /// 以前五处各写一遍检查、消息都只有「索引超出范围」四个字——越的是哪个界、
    /// 在几号元素上越的,全看不出来。allowEnd 给 Insert 用(插到末尾是合法的)。</summary>
    private static ListVal Indexed(RuntimeValue s, RuntimeValue a, string what, bool allowEnd = false)
    {
        if (a is not IntVal i) throw new RuntimeException($"{what} 需要 int 参数", ErrorKind.Argument);
        var lst = (ListVal)s;
        var n = lst.Elements.Count;
        if (i.Value < 0 || i.Value > (allowEnd ? n : n - 1))
            throw new RuntimeException($"{what} 的索引 {i.Value} 越界 (列表长度 {n})", ErrorKind.Index);
        return lst;
    }

    /// <summary>「按下标摘掉一个、交出摘掉的那个」—— `Remove` 与 `RemoveAt` 是同一件事,
    /// 只差越界报错时那句名字。</summary>
    private static RuntimeValue RemoveAtImpl(RuntimeValue s, RuntimeValue a, string what)
    {
        var lst = Indexed(s, a, what);
        var idx = ((IntVal)a).Value;
        var v = lst.Elements[idx];
        lst.Elements.RemoveAt(idx);
        return v;
    }

    [ClassMethod("At")]
    private static RuntimeValue At(RuntimeValue self, RuntimeValue arg)
    {
        var lst = Indexed(self, arg, "list.At");
        return lst.Elements[((IntVal)arg).Value];
    }

    [ClassMethod("Add")]
    private static RuntimeValue Add(RuntimeValue self, RuntimeValue arg)
    {
        ((ListVal)self).Elements.Add(arg);
        return VoidVal.Instance;
    }

    [ClassMethod("Remove")]
    private static RuntimeValue Remove(RuntimeValue self, RuntimeValue arg) => RemoveAtImpl(self, arg, "list.Remove");

    [ClassMethod("Insert")]
    private static RuntimeValue Insert(RuntimeValue self, RuntimeValue arg)
    {
        var lst = Indexed(self, arg, "list.Insert", allowEnd: true);
        var idx = ((IntVal)arg).Value;
        return FunctionVal.From(v =>
        {
            lst.Elements.Insert(idx, v);
            return VoidVal.Instance;
        });
    }

    /// <summary>`l.Set i` 交回一个"再收值"的函数:先收下标(这一步就查边界),再收要放进去的值。
    /// C# 那个名字叫 `SetAt` 是**故意避开**本类的 `Set` 字段(成员工厂那一份)——
    /// Ravel 那边看到的仍然是 `Set`(名字由 `[ClassMethod]` 说了算)。</summary>
    [ClassMethod("Set")]
    private static RuntimeValue SetAt(RuntimeValue self, RuntimeValue arg)
    {
        var lst = Indexed(self, arg, "list.Set");
        var idx = ((IntVal)arg).Value;
        return FunctionVal.From(v =>
        {
            lst.Elements[idx] = v;
            return VoidVal.Instance;
        });
    }

    [ClassMethod("IndexOf")]
    private static RuntimeValue IndexOf(RuntimeValue self, RuntimeValue arg) => IntVal.Of(((ListVal)self).Elements.IndexOf(arg));

    /// <summary>`RemoveAt i` 和 `Remove i` 是同一件事:名字对齐 C#(`List.RemoveAt` 按下标、
    /// `List.Remove` 按值),老名字留着 —— 库和好几个用例都在用。体也共用一份(见 `RemoveAtImpl`)。</summary>
    [ClassMethod("RemoveAt")]
    private static RuntimeValue RemoveAt(RuntimeValue self, RuntimeValue arg) => RemoveAtImpl(self, arg, "list.RemoveAt");

    [ClassMethod("AddRange")]
    private static RuntimeValue AddRange(RuntimeValue self, RuntimeValue arg)
    {
        ((ListVal)self).Elements.AddRange(BuiltinClasses.ElementsOf(arg, "AddRange"));
        return VoidVal.Instance;
    }

    [ClassMethod("Clear")]
    private static RuntimeValue Clear(RuntimeValue self)
    {
        ((ListVal)self).Elements.Clear();
        return VoidVal.Instance;
    }

    /// <summary>排序**交回新的**(和 `Map`/`Where` 那批一个规矩),稳定的,比不了当场说人话(见 `SortByKey`)。</summary>
    [ClassMethod("Sort")]
    private static RuntimeValue Sort(RuntimeValue self) => BuiltinClasses.SortByKey(((ListVal)self).Elements, x => x);
}
