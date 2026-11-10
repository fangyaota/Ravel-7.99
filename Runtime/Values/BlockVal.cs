namespace Ravel.Runtime;

public record BlockVal : FunctionVal
{
    public override ObjectVal Type => ClassType ??= BuiltinClasses.Block;
    public BlockExpr Block { get; init; }

    public BlockVal(BlockExpr block, Scope captureScope)
        : base(null!, (_, _) => FunctionVal.PlaceholderBody("BlockVal"))
    {
        Block = block;
        CaptureScope = captureScope;
        ClassType = BuiltinClasses.Block;   // 基类填的是 Function,block 的元类是 Block
    }

    public override string ToString() => "<block>";
}
