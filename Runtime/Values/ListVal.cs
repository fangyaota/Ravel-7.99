namespace Ravel.Runtime;

public record ListVal(List<RuntimeValue> Elements) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.List;
    public override string ToString() => ShowDepth.Guard(() => "[" + string.Join(" ", Elements) + "]");
}
