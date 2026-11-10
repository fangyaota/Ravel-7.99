namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`bool` 这个类给它的实例准备什么** —— 只有运算符这一份(方法在别的成员集里,
/// 建类时用 `MemberSet.Combine` 把几份接成一条)。
///
/// 左边一律直接转 `(BoolVal)a`:能走到这一格的,左操作数一定是 bool(方法表按类型挑的)
/// —— 只有**右边**是真·实参,才需要用 `Operand<BoolVal>` 换成中文报错。
///
/// `&` / `|` 是**按值**算的,两边都要先求出来;**短路**那两条是 `&amp;&amp;` / `||`,
/// 归求值器特判(`Interpreter.Binary` 的 `StepLogic`),类型这一层插不了手。
///
/// 声明顺序就是 `Fields ()` 报的顺序(`MemberSet.Scan` 按声明收名字),所以这里
/// **保持原来注册的顺序**:先 `==` / `!=`,再 `& | ^`。</summary>
internal static class BoolMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(BoolMembers), ObjectMembers.Set);

    /// <summary>值类型的 `init` —— "从别的值造一个"(`bool 0` 之类)。
    /// 从前它由 `BuiltinCtor(..., exposeInit: true)` 往注册表写一格,现在就在这儿:
    /// `MemberSet.Scan` 收成 `Ctor`,`VirtualScope` 那条"值类型 init"把它交出去(可见性也是那儿定的)。</summary>
    [ClassCtor]
    private static RuntimeValue Init(RuntimeValue arg) => BuiltinClasses.CastToBool(arg);

    /// <summary>`true == true` —— **按值比**,不是按引用:`1 == 1` 算出来的 true 和字面量
    /// `true` 必须是同一个答案(值相等,不是那一枚对象相同)。</summary>
    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((BoolVal)a).Value == BuiltinClasses.Operand<BoolVal>(b, "==").Value);

    /// <summary>`!=` —— 同上,取反。</summary>
    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((BoolVal)a).Value != BuiltinClasses.Operand<BoolVal>(b, "!=").Value);

    /// <summary>`a &amp; b` —— 逻辑与,**不短路**:右边已经在实参里求好了(`false & f ()`
    /// 照样会调 `f`)。要短路写 `&amp;&amp;`。</summary>
    [ClassOp("&")]
    private static RuntimeValue And(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((BoolVal)a).Value && BuiltinClasses.Operand<BoolVal>(b, "&").Value);

    /// <summary>`a | b` —— 逻辑或,**不短路**,同 `&`(短路版是 `||`)。</summary>
    [ClassOp("|")]
    private static RuntimeValue Or(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((BoolVal)a).Value || BuiltinClasses.Operand<BoolVal>(b, "|").Value);

    /// <summary>`a ^ b` —— 异或:两个不一样才是 true。</summary>
    [ClassOp("^")]
    private static RuntimeValue Xor(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((BoolVal)a).Value ^ BuiltinClasses.Operand<BoolVal>(b, "^").Value);
}
