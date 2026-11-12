namespace Ravel;

// 后缀收法：吃不吃并列实参 / 管道 / 块
internal readonly record struct Postfix(bool Args, bool Pipe, bool Block)
{
    public static readonly Postfix Full = new(true, true, false);

    // 只收一个原子，后缀一个都不吃
    public static readonly Postfix Bare = new(false, false, false);

    // 不吃并列实参，但 `|>` 照收（守卫那格）
    public static readonly Postfix PipeOnly = new(false, true, false);

    public Postfix NoPipe => this with { Pipe = false };

    public Postfix WithBlocks => this with { Block = true };
}
