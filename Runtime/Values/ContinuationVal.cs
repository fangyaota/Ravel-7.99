namespace Ravel.Runtime;

public record ContinuationVal(Func<RuntimeValue, Step> Impl)
    : FunctionVal(null!, (_, a) => Impl(a))
{
    public override string ToString() => "<continuation>";
}
