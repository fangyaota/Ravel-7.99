namespace Ravel.Runtime;

/// <summary>栈(LIFO)。形状和 <see cref="ListVal"/> 一样:`members` 喂基类,不占数据字段。
///
/// 里面用 .NET 的 <c>Stack</c>:Push/Pop/Peek 都是 O(1);**枚举顺序是"栈顶在前"** ——
/// 也就是**弹出来的顺序**,`foreach` 一个栈就是"从上往下看"。</summary>
public record StackVal : ObjectVal
{
    public Stack<RuntimeValue> Items { get; init; }

    public StackVal(IEnumerable<RuntimeValue> items, Scope? members = null)
        : base(PluginKit.ClassOf("Stack"), members ?? new Scope())
        => Items = new Stack<RuntimeValue>(items);

    public override string ToString() => PluginKit.Guard(() => "Stack [" + string.Join(" ", Items) + "]");
}
