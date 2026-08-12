namespace Ravel.Runtime;

public record SetVal(HashSet<RuntimeValue> Elements) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.Set;
    public override string ToString() => "{" + string.Join(" ", Elements) + "}";
}
