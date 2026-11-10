namespace Ravel.Runtime;

public sealed record BuiltinMethodVal(Func<RuntimeValue, RuntimeValue, RuntimeValue> Impl)
    : FunctionVal(null!, (_, self) => FunctionVal.From(arg => Impl(self, arg))), ISelfBinding
{
    public Func<Interpreter, RuntimeValue, RuntimeValue, RuntimeValue>? WithInterp { get; init; }

    internal RuntimeValue Invoke(Interpreter interp, RuntimeValue a, RuntimeValue b)
        => WithInterp is { } w ? w(interp, a, b) : Impl(a, b);
}

public sealed record BoundTraitOp(ObjectVal Self, string OpName) : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("BoundTraitOp"))
{
    public override string ToString() => "<function " + OpName + ">";
}

public enum TraitQueryKind { Implements, Implementors }

public sealed record TraitQuery(TraitQueryKind Kind)
    : FunctionVal(null!, (_, self) => new BoundTraitQuery((ObjectVal)self, Kind)), ISelfBinding
{
    public TraitQuery(string name, TraitQueryKind kind) : this(kind) => Name = name;
}

public sealed record BoundTraitQuery(ObjectVal Self, TraitQueryKind Kind)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("BoundTraitQuery"))
{
    public override string ToString()
        => Kind == TraitQueryKind.Implements ? "<function GetImplements>" : "<function GetImplementors>";
}
