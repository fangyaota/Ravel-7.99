namespace Ravel.Runtime;

/// <summary>队列(FIFO)。枚举顺序 = 出队顺序(先进先出)。</summary>
public record QueueVal : ObjectVal
{
    public Queue<RuntimeValue> Items { get; init; }

    public QueueVal(IEnumerable<RuntimeValue> items, Scope? members = null)
        : base(PluginKit.ClassOf("Queue"), members ?? new Scope())
        => Items = new Queue<RuntimeValue>(items);

    public override string ToString() => PluginKit.Guard(() => "Queue [" + string.Join(" ", Items) + "]");
}
