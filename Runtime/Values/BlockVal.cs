namespace Ravel.Runtime;

public record BlockVal : FunctionVal
{
    /// <summary>元类是 `Block`(不是 `Function`)。懒回填的理由见 <see cref="FunctionVal.Type"/>。</summary>
    public override ObjectVal Type => ClassType ??= BuiltinClasses.Block;
    public BlockExpr Block { get; init; }

    /// <summary>Body 不会被调用——求值器在 CallInto 里按类型分派,推 BlockExecFrame</summary>
    public BlockVal(BlockExpr block, Scope captureScope)
        : base(null!, (_, _) => FunctionVal.PlaceholderBody("BlockVal"))
    {
        Block = block;
        CaptureScope = captureScope;
        ClassType = BuiltinClasses.Block;   // 基类填的是 Function,block 的元类是 Block
    }

    public override string ToString() => "<block>";
}
