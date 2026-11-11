namespace Ravel.Runtime;

public sealed record ComposeVal(FunctionVal Original, FunctionVal Block, bool IsPrepend)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("ComposeVal"))
{
    public override string ToString()
        => "<function " + Original
           + (IsPrepend ? " prepend " : " append ")
           + (Block is BlockVal b ? AstPrinter.Block(b.Block) : Block.ToString())
           + ">";
}
