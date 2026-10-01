namespace Ravel.Runtime;

/// <summary>堆 / 优先队列:每次弹出来的都是**当前最小**(或最大)的那个。
///
/// 一张表 + 二叉堆的上下沉,比大小走 Ravel 自己的 `<`(见 `BuiltinClasses.Less`)——
/// 比不了当场报「比不了 X 与 Y」,而不是让 .NET 的比较器在里面炸。
///
/// **枚举顺序是"表里的存放顺序"**(堆序),不是排好序的:要看排好序的就一个个 `Pop ()`
/// (或者用 `ToList ()`,它就是这么做的)。
///
/// 上下沉写在这儿(值自己身上)而不是注册那层:它是这个结构的**定义**,谁调 `Push` 都一样。</summary>
public record HeapVal : ObjectVal
{
    public List<RuntimeValue> Items { get; init; } = [];

    /// <summary>大顶堆吗(`Heap ()` 是小顶堆,`MaxHeap ()` 才是大的)</summary>
    public bool Max { get; init; }

    public HeapVal(bool max = false, Scope? members = null)
        : base(PluginKit.ClassOf("Heap"), members ?? new Scope())
        => Max = max;

    /// <summary>a 比 b "更该在上面"吗 —— 小顶堆就是更小,大顶堆就是更大</summary>
    private bool Above(RuntimeValue a, RuntimeValue b) => Max ? PluginKit.Compare(b, a) < 0 : PluginKit.Compare(a, b) < 0;

    public void Push(RuntimeValue v)
    {
        Items.Add(v);
        var i = Items.Count - 1;
        while (i > 0)
        {
            var up = (i - 1) / 2;
            if (!Above(Items[i], Items[up])) break;
            (Items[up], Items[i]) = (Items[i], Items[up]);
            i = up;
        }
    }

    public RuntimeValue Pop()
    {
        if (Items.Count == 0) throw PluginKit.Fail("Heap.Pop: 空的，没有可弹的", ErrorKind.Index);
        var top = Items[0];
        var last = Items[^1];
        Items.RemoveAt(Items.Count - 1);
        if (Items.Count == 0) return top;

        Items[0] = last;
        for (var i = 0; ; )
        {
            var left = (2 * i) + 1;
            if (left >= Items.Count) break;
            var pick = left + 1 < Items.Count && Above(Items[left + 1], Items[left]) ? left + 1 : left;
            if (!Above(Items[pick], Items[i])) break;
            (Items[pick], Items[i]) = (Items[i], Items[pick]);
            i = pick;
        }

        return top;
    }

    public RuntimeValue Peek()
        => Items.Count > 0 ? Items[0] : throw PluginKit.Fail("Heap.Peek: 空的，没有顶可看", ErrorKind.Index);

    public override string ToString()
        => PluginKit.Guard(() => (Max ? "MaxHeap [" : "Heap [") + string.Join(" ", Items) + "]");
}
