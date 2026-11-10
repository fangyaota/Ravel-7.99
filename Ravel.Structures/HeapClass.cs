namespace Ravel.Runtime;

[RavelModule("Structures")]
[RavelClass("Heap")]
internal static class HeapClass
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg)
    {
        var h = new HeapVal(arg is BoolVal b && b.Value);
        if (arg is BoolVal or VoidVal) return h;
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

    [ClassMethod("ToList")]
    public static RuntimeValue ToList(RuntimeValue self)
    {
        var copy = new HeapVal(Val(self).Max) { Items = [.. Val(self).Items] };
        var out_ = new List<RuntimeValue>();
        while (copy.Items.Count > 0) out_.Add(copy.Pop());
        return new ListVal(out_);
    }

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
