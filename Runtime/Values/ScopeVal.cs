namespace Ravel.Runtime;

public record ScopeVal : ObjectVal
{
    public Scope Inner { get; init; }

    public ScopeVal(Scope inner, Scope? members = null)
        : base(BuiltinClasses.ScopeType, members)
        => Inner = inner;

    public override string ToString() => "<scope>";
}
