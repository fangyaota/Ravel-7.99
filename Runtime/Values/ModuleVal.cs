namespace Ravel.Runtime;

public record ModuleVal : ObjectVal
{
    public ModuleVal(string label, Scope moduleScope)
        : base(BuiltinClasses.Ravel, moduleScope) => Label = label;

    public static ModuleVal WithOwnTable(string label, Scope parent, Interpreter owner)
    {
        var table = new Scope(parent);
        var mv = new ModuleVal(label, table) { Owner = owner };
        table.TableOf = mv;
        return mv;
    }

    public string Label { get; init; }

    public List<string> ReferencePaths { get; set; } = [];

    internal Interpreter? Owner { get; set; }

    internal override string DisplayName => Label;

    public override string ToString() => $"<module {Label}>";
}
