namespace Ravel.Runtime;

public record ObjectVal(RuntimeType ClassType, Dictionary<string, RuntimeValue> Fields, ObjectVal? Parent = null, Scope? InstanceScope = null) : RuntimeValue
{
    public override RuntimeType Type => ClassType;
    public override string ToString() => $"<{ClassType.Name}>";
}
