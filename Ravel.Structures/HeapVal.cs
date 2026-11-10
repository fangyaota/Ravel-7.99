namespace Ravel.Runtime;

public record HeapVal : ObjectVal
{
    public List<RuntimeValue> Items { get; init; } = [];

    public bool Max { get; init; }

    public HeapVal(bool max = false, Scope? members = null)
        : base(PluginKit.ClassOf("Heap"), members ?? new Scope())
        => Max = max;

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
