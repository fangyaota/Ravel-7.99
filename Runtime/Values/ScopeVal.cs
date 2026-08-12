namespace Ravel.Runtime;

public record ScopeVal(Scope Scope) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.ScopeType;
    public override string ToString() => "<scope>";
}
