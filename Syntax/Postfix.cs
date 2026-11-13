namespace Ravel;

//  Comp = 这条式子之后能不能接**裸推导式**(`x for i in xs` 不带容器的那种)。
//  做成参数而不是解析器上的开关:入口有二十多个,谁要谁不要必须当场说清;
//  写成字段就会静默继承(括号里那回就是这么坏的)。
//
//  Ternary 同理:三元 `a if c else b` 里那个 `if` 和推导式的 `if` 子句同名,
//  滤波条件那一格必须关掉三元(`[x for i in xs if a if b]` 是两个滤波器,不是一个三元),
//  关法和 Comp 一样按入口说清,不靠继承。
//  Arrow 同理:字典字面量的**键**那一格要把 `->` 当分隔符(`{k -> v}`),不能当运算符;
//  别的位置(`[a -> b]`、`(a -> b)`、`{a -> b;}` 里的语句)都要它。
internal readonly record struct Postfix(bool Args, bool Pipe, bool Block, bool Comp, bool Ternary = true, bool Arrow = true)
{
    public static readonly Postfix Full = new(true, true, false, true);

    // 只收一个原子，后缀一个都不吃
    public static readonly Postfix Bare = new(false, false, false, false);

    public static readonly Postfix PipeOnly = new(false, true, false, true);

    // 推导式的"值"与子句:解出一个操作数就停,`for` 留给外层。
    public static readonly Postfix CompHead = new(true, true, false, false);

    // 字典字面量的键:只吃一个原子,`->` 留给 ParseDict 当分隔符。
    public static readonly Postfix DictKey = new(false, false, false, false, true, false);

    public Postfix NoPipe => this with { Pipe = false };

    public Postfix WithBlocks => this with { Block = true };
}
