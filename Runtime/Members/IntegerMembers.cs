namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

/// <summary>**`int` 给它的实例准备什么。**
///
/// 一个整数一张成员表(<see cref="ValueTable"/>),里面的格子按名字来这儿取 ——
/// 名字与造法都在这一个类里,和插件(`[ClassMethod]` 那套)、和别的内置类同一份规矩。
/// 从前这是 `IntegerScope` 那个手写子类:名字写在 `AllNames`、造法写在 `Create`,
/// 两处对不上就悄悄少一个成员。现在名字从特性上收,一处就够。
///
/// 运算符那 19 条**真正的实现**仍在 `BuiltinClasses.IntegerOperator` 那张函数表里
/// (按宽度升级、移位、`**` 收窄那一套逻辑和别的数值类型共用),这里只做"名字 → 那一格"
/// 的接线 —— 引擎的数值语义不该抄第二遍。`ToString` 是这条类型自己的,就地写。</summary>
internal static class IntegerMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(IntegerMembers), ObjectMembers.Set);

    private static RuntimeValue Op(string symbol, RuntimeValue a, RuntimeValue b)
        => (BuiltinClasses.IntegerOperator(symbol)
            ?? throw new InvalidOperationException($"int 声称有运算符 '{symbol}',但那张表里没有"))(a, b);

    [ClassOp("+")] private static RuntimeValue Add(RuntimeValue a, RuntimeValue b) => Op("+", a, b);
    [ClassOp("-")] private static RuntimeValue Sub(RuntimeValue a, RuntimeValue b) => Op("-", a, b);
    [ClassOp("*")] private static RuntimeValue Mul(RuntimeValue a, RuntimeValue b) => Op("*", a, b);
    [ClassOp("/")] private static RuntimeValue Div(RuntimeValue a, RuntimeValue b) => Op("/", a, b);
    [ClassOp("%")] private static RuntimeValue Mod(RuntimeValue a, RuntimeValue b) => Op("%", a, b);

    /// <summary>左移 —— 移多少由右边给(负数是反方向,见 `ShiftCount`)。</summary>
    [ClassOp("<<")] private static RuntimeValue Shl(RuntimeValue a, RuntimeValue b) => Op("<<", a, b);
    [ClassOp(">>")] private static RuntimeValue Shr(RuntimeValue a, RuntimeValue b) => Op(">>", a, b);

    /// <summary>循环移位要在**固定宽度**上转,`int` 自己说了算 —— 这条不是笔误。</summary>
    [ClassOp("<<<")] private static RuntimeValue Rotl(RuntimeValue a, RuntimeValue b) => Op("<<<", a, b);
    [ClassOp(">>>")] private static RuntimeValue Rotr(RuntimeValue a, RuntimeValue b) => Op(">>>", a, b);

    /// <summary>幂 —— 结果要放得回 `int`,放不回就当场说(见 `NarrowIntPow`)。</summary>
    [ClassOp("**")] private static RuntimeValue Pow(RuntimeValue a, RuntimeValue b) => Op("**", a, b);

    [ClassOp("==")] private static RuntimeValue Eq(RuntimeValue a, RuntimeValue b) => Op("==", a, b);
    [ClassOp("!=")] private static RuntimeValue Ne(RuntimeValue a, RuntimeValue b) => Op("!=", a, b);
    [ClassOp("<")] private static RuntimeValue Lt(RuntimeValue a, RuntimeValue b) => Op("<", a, b);
    [ClassOp(">")] private static RuntimeValue Gt(RuntimeValue a, RuntimeValue b) => Op(">", a, b);
    [ClassOp("<=")] private static RuntimeValue Le(RuntimeValue a, RuntimeValue b) => Op("<=", a, b);
    [ClassOp(">=")] private static RuntimeValue Ge(RuntimeValue a, RuntimeValue b) => Op(">=", a, b);

    [ClassOp("&")] private static RuntimeValue And(RuntimeValue a, RuntimeValue b) => Op("&", a, b);
    [ClassOp("|")] private static RuntimeValue Or(RuntimeValue a, RuntimeValue b) => Op("|", a, b);
    [ClassOp("^")] private static RuntimeValue Xor(RuntimeValue a, RuntimeValue b) => Op("^", a, b);

    /// <summary>`print (5)` 打的这串 —— 十进制、没有正号、负数带 `-`。</summary>
    [ClassMethod("ToString")]
    private static RuntimeValue ToString(RuntimeValue self)
        => new StringVal(self is IntVal i ? i.Value.ToString() : self.ToString()!);
}
