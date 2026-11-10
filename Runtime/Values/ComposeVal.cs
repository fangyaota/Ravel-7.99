namespace Ravel.Runtime;

public sealed record ComposeVal(FunctionVal Original, BlockVal Block, bool IsPrepend)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("ComposeVal"))
{
    public override string ToString()
        => "<function " + Original + (IsPrepend ? " prepend " : " append ") + AstPrinter.Block(Block.Block) + ">";
}
