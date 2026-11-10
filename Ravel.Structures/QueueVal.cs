namespace Ravel.Runtime;

public record QueueVal : ObjectVal
{
    public Queue<RuntimeValue> Items { get; init; }

    public QueueVal(IEnumerable<RuntimeValue> items, Scope? members = null)
        : base(PluginKit.ClassOf("Queue"), members ?? new Scope())
        => Items = new Queue<RuntimeValue>(items);

    public override string ToString() => PluginKit.Guard(() => "Queue [" + string.Join(" ", Items) + "]");
}
