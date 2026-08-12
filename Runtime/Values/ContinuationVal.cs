namespace Ravel.Runtime;

public record ContinuationVal(Func<RuntimeValue, Step> Impl)
    : FunctionVal(null!, (_, args) =>
    {
        if (args.Length != 1) return new Error("续延需要 1 个参数");
        return Impl(args[0]);
    })
{
    public override string ToString() => "<continuation>";
}
