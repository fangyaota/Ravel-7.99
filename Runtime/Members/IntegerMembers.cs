namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

internal static class IntegerMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(IntegerMembers), ObjectMembers.Set);

    [ClassCtor]
    private static RuntimeValue Init(RuntimeValue arg) => BuiltinClasses.CastToInt(arg);

    private static RuntimeValue Op(string symbol, RuntimeValue a, RuntimeValue b)
        => (BuiltinClasses.IntegerOperator(symbol)
            ?? throw new InvalidOperationException($"int 声称有运算符 '{symbol}',但那张表里没有"))(a, b);

    [ClassOp("+")] private static RuntimeValue Add(RuntimeValue a, RuntimeValue b) => Op("+", a, b);
    [ClassOp("-")] private static RuntimeValue Sub(RuntimeValue a, RuntimeValue b) => Op("-", a, b);
    [ClassOp("*")] private static RuntimeValue Mul(RuntimeValue a, RuntimeValue b) => Op("*", a, b);
    [ClassOp("/")] private static RuntimeValue Div(RuntimeValue a, RuntimeValue b) => Op("/", a, b);
    [ClassOp("%")] private static RuntimeValue Mod(RuntimeValue a, RuntimeValue b) => Op("%", a, b);

    [ClassOp("<<")] private static RuntimeValue Shl(RuntimeValue a, RuntimeValue b) => Op("<<", a, b);
    [ClassOp(">>")] private static RuntimeValue Shr(RuntimeValue a, RuntimeValue b) => Op(">>", a, b);

    [ClassOp("<<<")] private static RuntimeValue Rotl(RuntimeValue a, RuntimeValue b) => Op("<<<", a, b);
    [ClassOp(">>>")] private static RuntimeValue Rotr(RuntimeValue a, RuntimeValue b) => Op(">>>", a, b);

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

    [ClassMethod("ToString")]
    private static RuntimeValue ToString(RuntimeValue self)
        => new StringVal(self is IntVal i ? i.Value.ToString() : self.ToString()!);
}
