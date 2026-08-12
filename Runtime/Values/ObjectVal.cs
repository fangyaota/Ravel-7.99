namespace Ravel.Runtime;

public record ObjectVal(RuntimeType ClassType, Scope Scope, ObjectVal? Parent = null) : RuntimeValue
{
    public override RuntimeType Type => ClassType;
    public override string ToString() => $"<{ClassType.Name}>";
}
