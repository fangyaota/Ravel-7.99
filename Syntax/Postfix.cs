namespace Ravel;

internal readonly record struct Postfix(bool Args, bool Pipe, bool Block)
{
    public static readonly Postfix Full = new(true, true, false);

    // 只收一个原子，后缀一个都不吃
    public static readonly Postfix Bare = new(false, false, false);

    public static readonly Postfix PipeOnly = new(false, true, false);

    public Postfix NoPipe => this with { Pipe = false };

    public Postfix WithBlocks => this with { Block = true };
}
