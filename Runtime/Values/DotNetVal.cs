namespace Ravel.Runtime;

public record DotNetVal : ObjectVal
{
    public object? Value { get; init; }

    public DotNetVal(object? value, Scope? members = null)
        : base(BuiltinClasses.DotNetObject, members ?? new Scope())
        => Value = value;

    public DotNetVal(object? value, ClassVal classType, Scope? members = null)
        : base(classType, members ?? new Scope())
        => Value = value;

    public override string ToString()
        => ShowDepth.Guard(() => Value is null ? "DotNetObject (null)" : "DotNetObject " + NetBridge.Show(Value));

    public object? ToObject(Type target, string what = "ToObject") => NetBridge.To(Value, target, what);
}
