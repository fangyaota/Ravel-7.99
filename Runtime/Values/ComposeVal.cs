namespace Ravel.Runtime;

/// <summary>prepend/append 合成的函数:先跑块再调原函数(或反过来)。调用时推 Compose 控制帧</summary>
public sealed record ComposeVal(FunctionVal Original, BlockVal Block, bool IsPrepend)
    : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    /// <summary>盖掉 record 的自动 dump:写成 `prepend`/`append` 的读法 ——
    /// `print (f.prepend { 1; })` → `<function &lt;f&gt; prepend { 1; }&gt;`。</summary>
    public override string ToString()
        => "<function " + Original + (IsPrepend ? " prepend " : " append ") + AstPrinter.Block(Block.Block) + ">";
}
