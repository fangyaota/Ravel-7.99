namespace Ravel.Runtime;

public record BlockVal : FunctionVal
{
    public override RuntimeType Type => RuntimeType.Block;
    public BlockExpr Block { get; init; }

    public BlockVal(BlockExpr block, Scope captureScope)
        : base(null!, (_, _) => new Done(VoidVal.Instance))
    {
        Block = block;
        Scope = captureScope;
    }

    public override string ToString() => "<block>";
}
