namespace Ravel.Runtime;

/// <summary>双端队列:两头都能进能出。用 <c>LinkedList</c> —— 两端 O(1),
/// 而且它本身就是双向链表,`PushFront` 不必搬整张表。枚举顺序是"从前到后"。</summary>
public record DequeVal : ObjectVal
{
    public LinkedList<RuntimeValue> Items { get; init; }

    public DequeVal(IEnumerable<RuntimeValue> items, Scope? members = null)
        : base(PluginKit.ClassOf("Deque"), members ?? new Scope())
        => Items = new LinkedList<RuntimeValue>(items);

    public override string ToString() => PluginKit.Guard(() => "Deque [" + string.Join(" ", Items) + "]");
}
