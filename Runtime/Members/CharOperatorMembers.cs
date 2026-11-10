namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`char` 这个类给它的实例准备什么** —— 只有运算符这一份(方法在别的成员集里,
/// 建类时用 `MemberSet.Combine` 把几份接成一条)。
///
/// `Char <: IValue`,和数、字符串一个待遇:**能比、能拼、能重复**。比的是字符的码位
/// (`char` 的 `<` 就是比码位,没有区域设置那回事)。拼接和重复的结果都是**字符串**
/// —— `'a' + 'b'` 是 `"ab"`,不是"下一个字符"。
///
/// 声明顺序就是 `Fields ()` 报的顺序(`MemberSet.Scan` 按声明收名字),所以这里
/// **保持原来注册的顺序**:`== != < > <= >=` 六条比较,然后 `+` / `*`。</summary>
internal static class CharOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(CharOperatorMembers), ObjectMembers.Set);

    /// <summary>`'a' == 'a'` —— 按码位比。左边直接转 `(CharVal)a`,右边走 `Operand` 换中文报错。</summary>
    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value == BuiltinClasses.Operand<CharVal>(b, "==").Value);

    /// <summary>`!=` —— 同上,取反。</summary>
    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value != BuiltinClasses.Operand<CharVal>(b, "!=").Value);

    /// <summary>`<` —— 比码位(`char` 自己的序,**没有区域设置**那一套)。</summary>
    [ClassOp("<")]
    private static RuntimeValue Less(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value < BuiltinClasses.Operand<CharVal>(b, "<").Value);

    /// <summary>`>`。</summary>
    [ClassOp(">")]
    private static RuntimeValue Greater(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value > BuiltinClasses.Operand<CharVal>(b, ">").Value);

    /// <summary>`<=`。</summary>
    [ClassOp("<=")]
    private static RuntimeValue LessOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value <= BuiltinClasses.Operand<CharVal>(b, "<=").Value);

    /// <summary>`>=`。</summary>
    [ClassOp(">=")]
    private static RuntimeValue GreaterOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((CharVal)a).Value >= BuiltinClasses.Operand<CharVal>(b, ">=").Value);

    /// <summary>拼接:两个字符拼成**字符串**(`'a' + 'b'` → `"ab"`),字符拼字符串同理
    /// (`AsText` 把 string / char 都换成文本)。右边别的类型报错,不悄悄 ToString。</summary>
    [ClassOp("+")]
    private static RuntimeValue Concat(RuntimeValue a, RuntimeValue b)
        => new StringVal(((CharVal)a).Value + BuiltinClasses.AsText(b, "+"));

    /// <summary>重复:`'-' * 10` → 十个短横。0 或负数给空串(`Math.Max(0, …)`);
    /// 右边不是 int 由 `Operand<IntVal>` 报。</summary>
    [ClassOp("*")]
    private static RuntimeValue Repeat(RuntimeValue a, RuntimeValue b)
        => new StringVal(string.Concat(
            Enumerable.Repeat(((CharVal)a).Value, Math.Max(0, BuiltinClasses.Operand<IntVal>(b, "*").Value))));
}
