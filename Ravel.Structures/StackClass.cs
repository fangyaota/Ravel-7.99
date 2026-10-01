namespace Ravel.Runtime;

/// <summary>**栈**(后进先出)。`Push` / `Pop` / `Peek` 都是 O(1)。
///
///     s := Stack ()            # 也可以 Stack [1 2 3] —— 从底往上垒
///     s.Push 1 / s.Pop () / s.Peek ()
///
/// 枚举顺序是**栈顶在前**(= 弹出来的顺序);`ToList ()` 也是那个顺序。
/// 空的 `Pop` / `Peek` **报错**(不是给 `()` ——「没有顶」和「顶上是个空值」不该长得一样)。</summary>
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
    public static RuntimeValue Count(RuntimeValue self) => new IntVal(Val(self).Items.Count);

    /// <summary>栈顶在前(和枚举、和一个个 `Pop` 一个顺序)</summary>
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
