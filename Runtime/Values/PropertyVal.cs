namespace Ravel.Runtime;

public record PropertyVal : ObjectVal
{
    public FunctionVal Getter { get; init; }
    public FunctionVal Setter { get; init; }

    public Variable? Var { get; init; }

    public PropertyVal(FunctionVal getter, FunctionVal setter, Variable? var = null, Scope? members = null)
        : base(BuiltinClasses.Property, members)
    {
        Getter = getter;
        Setter = setter;
        Var = var;
    }

    public override string ToString() => "<property>";
}
