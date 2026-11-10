namespace Ravel.Runtime;

public record StackVal : ObjectVal
{
    public Stack<RuntimeValue> Items { get; init; }

    public StackVal(IEnumerable<RuntimeValue> items, Scope? members = null)
        : base(PluginKit.ClassOf("Stack"), members ?? new Scope())
        => Items = new Stack<RuntimeValue>(items);

    public override string ToString() => PluginKit.Guard(() => "Stack [" + string.Join(" ", Items) + "]");
}
