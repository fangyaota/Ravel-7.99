namespace Ravel.Runtime;

public record BlockVal : FunctionVal
{
    public BlockExpr Block { get; init; }
    public Scope CaptureScope { get; init; }
    public Interpreter Interp { get; init; }

    public BlockVal(BlockExpr block, Scope captureScope, Interpreter interp)
        : base(captureScope, (_, _) => new Error("BlockVal 未被正确初始化"))
    {
        Block = block;
        CaptureScope = captureScope;
        Interp = interp;
        Trampolined = _ => Invoke();
    }

    public Step Invoke()
    {
        var saved = Interp.CurrentScope;
        Interp.CurrentScope = CaptureScope;
        return new More(() =>
        {
            var r = Interp.EvalBlockExec(Block);
            return new More(() => { Interp.CurrentScope = saved; return r; });
        });
    }

    public override string ToString() => "<block>";
}
