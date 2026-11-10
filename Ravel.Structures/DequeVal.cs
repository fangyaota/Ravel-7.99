namespace Ravel.Runtime;

public record DequeVal : ObjectVal
{
    public LinkedList<RuntimeValue> Items { get; init; }

    public DequeVal(IEnumerable<RuntimeValue> items, Scope? members = null)
        : base(PluginKit.ClassOf("Deque"), members ?? new Scope())
        => Items = new LinkedList<RuntimeValue>(items);

    public override string ToString() => PluginKit.Guard(() => "Deque [" + string.Join(" ", Items) + "]");
}
