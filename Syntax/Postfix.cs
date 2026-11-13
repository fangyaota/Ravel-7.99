namespace Ravel;

//  Comp = 这条式子之后能不能接**裸推导式**(`x for i in xs` 不带容器的那种)。
//  做成参数而不是解析器上的开关:入口有二十多个,谁要谁不要必须当场说清;
//  写成字段就会静默继承(括号里那回就是这么坏的)。
internal readonly record struct Postfix(bool Args, bool Pipe, bool Block, bool Comp)
{
    public static readonly Postfix Full = new(true, true, false, true);

    // 只收一个原子，后缀一个都不吃
    public static readonly Postfix Bare = new(false, false, false, false);

    public static readonly Postfix PipeOnly = new(false, true, false, true);

    // 推导式的"值"与子句:解出一个操作数就停,`for` 留给外层。
    public static readonly Postfix CompHead = new(true, true, false, false);

    public Postfix NoPipe => this with { Pipe = false };

    public Postfix WithBlocks => this with { Block = true };
}
