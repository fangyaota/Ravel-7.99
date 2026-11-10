namespace Ravel.Runtime;

[RavelModule("Structures")]
[RavelClass("Stack")]
internal static class StackClass
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg) => new StackVal(Structure.Seed(arg, "Stack"));

    [ClassMethod("Push")]
    public static RuntimeValue Push(RuntimeValue self, RuntimeValue v)
    {
        Val(self).Items.Push(v);
        return VoidVal.Instance;
    }

    [ClassMethod("PushRange")]
    public static RuntimeValue PushRange(RuntimeValue self, RuntimeValue xs)
    {
        var items = Val(self).Items;
        foreach (var x in PluginKit.Elements(xs, "Stack.PushRange")) items.Push(x);
        return VoidVal.Instance;
    }

    [ClassMethod("Pop")]
    public static RuntimeValue Pop(RuntimeValue self)
        => Val(self).Items.Count > 0
            ? Val(self).Items.Pop()
            : throw PluginKit.Fail("Stack.Pop: 空栈没有可弹的", ErrorKind.Index);

    [ClassMethod("Peek")]
    public static RuntimeValue Peek(RuntimeValue self)
        => Val(self).Items.Count > 0
            ? Val(self).Items.Peek()
            : throw PluginKit.Fail("Stack.Peek: 空栈没有顶可看", ErrorKind.Index);

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

    private static StackVal Val(RuntimeValue self) => (StackVal)self;
}
