namespace Ravel.Runtime;

[RavelModule("Structures")]
[RavelClass("Deque")]
internal static class DequeClass
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg) => new DequeVal(Structure.Seed(arg, "Deque"));

    [ClassMethod("PushFront")]
    public static RuntimeValue PushFront(RuntimeValue self, RuntimeValue v)
    {
        Val(self).Items.AddFirst(v);
        return VoidVal.Instance;
    }

    [ClassMethod("PushBack")]
    public static RuntimeValue PushBack(RuntimeValue self, RuntimeValue v)
    {
        Val(self).Items.AddLast(v);
        return VoidVal.Instance;
    }

    [ClassMethod("Push")]
    public static RuntimeValue Push(RuntimeValue self, RuntimeValue v) => PushBack(self, v);

    [ClassMethod("PopFront")]
    public static RuntimeValue PopFront(RuntimeValue self)
    {
        var items = Val(self).Items;
        if (items.Count == 0) throw PluginKit.Fail("Deque.PopFront: 空的双端队列没有可弹的", ErrorKind.Index);
        var v = items.First!.Value;
        items.RemoveFirst();
        return v;
    }

    [ClassMethod("PopBack")]
    public static RuntimeValue PopBack(RuntimeValue self)
    {
        var items = Val(self).Items;
        if (items.Count == 0) throw PluginKit.Fail("Deque.PopBack: 空的双端队列没有可弹的", ErrorKind.Index);
        var v = items.Last!.Value;
        items.RemoveLast();
        return v;
    }

    [ClassMethod("Pop")]
    public static RuntimeValue Pop(RuntimeValue self) => PopBack(self);

    [ClassMethod("PeekFront")]
    public static RuntimeValue PeekFront(RuntimeValue self)
        => Val(self).Items.First is { } f
            ? f.Value
            : throw PluginKit.Fail("Deque.PeekFront: 空的双端队列没有头可看", ErrorKind.Index);

    [ClassMethod("PeekBack")]
    public static RuntimeValue PeekBack(RuntimeValue self)
        => Val(self).Items.Last is { } l
            ? l.Value
            : throw PluginKit.Fail("Deque.PeekBack: 空的双端队列没有尾可看", ErrorKind.Index);

    [ClassMethod("IsEmpty")]
    public static RuntimeValue IsEmpty(RuntimeValue self) => new BoolVal(Val(self).Items.Count == 0);

    [ClassMethod("Count")]
    public static RuntimeValue Count(RuntimeValue self) => IntVal.Of(Val(self).Items.Count);

    [ClassMethod("ToList")]
    public static RuntimeValue ToList(RuntimeValue self) => new ListVal([.. Val(self).Items]);

    [ClassMethod("Clear")]
    public static RuntimeValue Clear(RuntimeValue self)
    {
        Val(self).Items.Clear();
        return VoidVal.Instance;
    }

    private static DequeVal Val(RuntimeValue self) => (DequeVal)self;
}
