namespace Ravel.Runtime;

/// <summary>**堆 / 优先队列**:每次弹出来的都是当前最小(或最大)的那个。
///
///     h := Heap ()                 # 小顶堆;MaxHeap () 是大顶堆(见 lib/heap.rav)
///     h.Push 3 / h.Push 1 / h.Pop ()   # → 1
///
/// 「谁在上」按 Ravel 自己的 `<` 算(见 `Less`)—— 里面装的得能互相比大小,
/// 比不了当场报「比不了 X 与 Y」。空的 `Pop` / `Peek` 报错。
/// `ToList ()` 是**一个个弹出来的顺序**(排好序的),不是表里的存放顺序。</summary>
[RavelModule("Structures")]
[RavelClass("Heap")]
internal static class HeapClass
{
    /// <summary>`Heap ()` 是小顶堆;`Heap true` 是大顶堆(库里的 `MaxHeap ()` 就是这么写的);
    /// 给一个容器就照它播种(`Heap [3 1 2]`)。</summary>
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg)
    {
        var h = new HeapVal(arg is BoolVal b && b.Value);
        if (arg is BoolVal or VoidVal or DefaultVal) return h;
        foreach (var x in PluginKit.Elements(arg, "Heap 的构造参数")) h.Push(x);
        return h;
    }

    [ClassMethod("Push")]
    public static RuntimeValue Push(RuntimeValue self, RuntimeValue v)
    {
        Val(self).Push(v);
        return VoidVal.Instance;
    }

    [ClassMethod("PushRange")]
    public static RuntimeValue PushRange(RuntimeValue self, RuntimeValue xs)
    {
        var h = Val(self);
        foreach (var x in PluginKit.Elements(xs, "Heap.PushRange")) h.Push(x);
        return VoidVal.Instance;
    }

    [ClassMethod("Pop")]
    public static RuntimeValue Pop(RuntimeValue self) => Val(self).Pop();

    [ClassMethod("Peek")]
    public static RuntimeValue Peek(RuntimeValue self) => Val(self).Peek();

    [ClassMethod("IsEmpty")]
    public static RuntimeValue IsEmpty(RuntimeValue self) => new BoolVal(Val(self).Items.Count == 0);

    [ClassMethod("Count")]
    public static RuntimeValue Count(RuntimeValue self) => IntVal.Of(Val(self).Items.Count);

    /// <summary>一个个弹出来的顺序 = 排好序的那一串(弹的是**一份拷贝**,原堆不动)</summary>
    [ClassMethod("ToList")]
    public static RuntimeValue ToList(RuntimeValue self)
    {
        var copy = new HeapVal(Val(self).Max) { Items = [.. Val(self).Items] };
        var out_ = new List<RuntimeValue>();
        while (copy.Items.Count > 0) out_.Add(copy.Pop());
        return new ListVal(out_);
    }

    /// <summary>它是大顶堆吗</summary>
    [ClassMethod("Max")]
    public static RuntimeValue Max(RuntimeValue self) => new BoolVal(Val(self).Max);

    [ClassMethod("Clear")]
    public static RuntimeValue Clear(RuntimeValue self)
    {
        Val(self).Items.Clear();
        return VoidVal.Instance;
    }

    private static HeapVal Val(RuntimeValue self) => (HeapVal)self;
}
