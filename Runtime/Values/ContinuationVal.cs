namespace Ravel.Runtime;

public record ContinuationVal(Func<RuntimeValue, Step> Impl) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.Function;
    public override string ToString() => "<continuation>";
}
