namespace Ravel.Runtime;

public record BlockVal : FunctionVal
{
    public override RuntimeType Type => RuntimeType.Block;
    public BlockExpr Block { get; init; }

    public BlockVal(BlockExpr block, Scope captureScope)
        : base(captureScope, (_, _) => new Error("BlockVal 未被正确初始化"))
    {
        Block = block;
        Scope = captureScope;
        Trampolined = _ => Invoke();
    }

    public Step Invoke()
    {
        var interp = Interpreter.Current!;
        var saved = interp.CurrentScope;
        interp.CurrentScope = Scope;
        return Interpreter.Finally(interp.EvalBlockExec(Block), () => interp.CurrentScope = saved);
    }

    public override string ToString() => "<block>";
}
