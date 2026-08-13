namespace Ravel.Runtime;

public record ObjectVal(RuntimeType ClassType, Scope Scope) : RuntimeValue
{
    public override RuntimeType Type => ClassType;
    public override string ToString() => $"<{ClassType.Name}>";
}
