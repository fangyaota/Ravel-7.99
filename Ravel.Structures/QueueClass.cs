namespace Ravel.Runtime;

/// <summary>**队列**(先进先出)。`Enqueue` / `Dequeue` / `Peek` 都是 O(1)。
///
///     q := Queue ()
///     q.Enqueue 1 / q.Dequeue () / q.Peek ()
///
/// 枚举顺序 = 出队顺序(先进先出)。空队列的 `Dequeue` / `Peek` 报错。</summary>
[RavelModule("Structures")]
[RavelClass("Queue")]
internal static class QueueClass
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg) => new QueueVal(Structure.Seed(arg, "Queue"));

    [ClassMethod("Enqueue")]
    public static RuntimeValue Enqueue(RuntimeValue self, RuntimeValue v)
    {
        Val(self).Items.Enqueue(v);
        return VoidVal.Instance;
    }

    [ClassMethod("EnqueueRange")]
    public static RuntimeValue EnqueueRange(RuntimeValue self, RuntimeValue xs)
    {
        var items = Val(self).Items;
        foreach (var x in PluginKit.Elements(xs, "Queue.EnqueueRange")) items.Enqueue(x);
        return VoidVal.Instance;
    }

    [ClassMethod("Dequeue")]
    public static RuntimeValue Dequeue(RuntimeValue self)
        => Val(self).Items.Count > 0
            ? Val(self).Items.Dequeue()
            : throw PluginKit.Fail("Queue.Dequeue: 空队列没有可出的", ErrorKind.Index);

    [ClassMethod("Peek")]
    public static RuntimeValue Peek(RuntimeValue self)
        => Val(self).Items.Count > 0
            ? Val(self).Items.Peek()
            : throw PluginKit.Fail("Queue.Peek: 空队列没有头可看", ErrorKind.Index);

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

    private static QueueVal Val(RuntimeValue self) => (QueueVal)self;
}
