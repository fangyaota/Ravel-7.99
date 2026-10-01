namespace Ravel.Runtime;

/// <summary>**双端队列**:两头都能进能出(底下是双向链表,两端 O(1))。
///
///     d := Deque ()
///     d.PushBack 1 / d.PushFront 0 / d.PopFront () / d.PopBack ()
///
/// `Push` / `Pop` 是 `PushBack` / `PopBack` 的简写(当栈使的时候顺手)。
/// 枚举顺序是**从前到后**。</summary>
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
    public static RuntimeValue Count(RuntimeValue self) => new IntVal(Val(self).Items.Count);

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
