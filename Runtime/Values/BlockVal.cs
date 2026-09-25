namespace Ravel.Runtime;

public record BlockVal : FunctionVal
{
    public override ObjectVal Type => BuiltinClasses.Block;
    public BlockExpr Block { get; init; }

    /// <summary>Body 不会被调用——求值器在 CallInto 里按类型分派,推 BlockExecFrame</summary>
    public BlockVal(BlockExpr block, Scope captureScope)
        : base(null!, (_, _) => VoidVal.Instance)
    {
        Block = block;
        Scope = captureScope;
    }

    public override string ToString() => "<block>";
}
