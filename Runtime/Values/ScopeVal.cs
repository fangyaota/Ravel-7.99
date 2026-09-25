namespace Ravel.Runtime;

public record ScopeVal(Scope Scope) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.ScopeType;
    public override string ToString() => "<scope>";
}
