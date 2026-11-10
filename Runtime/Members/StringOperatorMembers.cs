namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`string` 这个类给它的实例准备什么** —— 运算符那一份(方法在别的成员集里,
/// 建类时用 `MemberSet.Combine` 把几份接成一条)。
///
/// 字符串的相等和序都**不跟当前区域设置走**:相等按 `==` 比字符序列,序走
/// `string.CompareOrdinal`(序数)。否则同一段程序换台机器就换个结果。
///
/// 声明顺序就是 `Fields ()` 报的顺序(`MemberSet.Scan` 按声明收名字),所以这里
/// **保持原来注册的顺序**:先 `==` / `!=`,再 `+` / `*`,最后那四条序。</summary>
internal static class StringOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(StringOperatorMembers), ObjectMembers.Set);

    /// <summary>`"ab" == "ab"` —— **按值比**(字符串的"值"就是那串字符)。
    /// 左边直接转 `(StringVal)a`(方法表保证它是 string),右边走 `Operand` 换中文报错。</summary>
    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((StringVal)a).Value == BuiltinClasses.Operand<StringVal>(b, "==").Value);

    /// <summary>`!=` —— 同上,取反。</summary>
    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(((StringVal)a).Value != BuiltinClasses.Operand<StringVal>(b, "!=").Value);

    /// <summary>拼接,**也收字符**:`"abc" + 'd'` 和 `'d' + "abc"` 都是字符串(`AsText` 把
    /// string / char 都换成文本)。别的类型不认 —— 不做"悄悄 ToString"。</summary>
    [ClassOp("+")]
    private static RuntimeValue Concat(RuntimeValue a, RuntimeValue b)
        => new StringVal(((StringVal)a).Value + BuiltinClasses.AsText(b, "+"));

    /// <summary>重复:`"-" * 10` → 十个短横。0 或负数给空串(`Math.Max(0, …)` 那一下);
    /// 负数由 `Operand<IntVal>` 先把类型卡住(右边不是 int 就报,不是静默)。</summary>
    [ClassOp("*")]
    private static RuntimeValue Repeat(RuntimeValue a, RuntimeValue b)
        => new StringVal(string.Concat(
            Enumerable.Repeat(((StringVal)a).Value, Math.Max(0, BuiltinClasses.Operand<IntVal>(b, "*").Value))));

    /// <summary>`<` —— 字符串的序**按序数比**(ordinal,和 C# 的 `string.CompareOrdinal`
    /// 一个口径),不跟当前区域设置走 —— 后者会让同一段程序换台机器就换个结果。
    /// 有了它,`Min` / `Max` / `Sort` 那批序列方法对字符串也成立。</summary>
    [ClassOp("<")]
    private static RuntimeValue Less(RuntimeValue a, RuntimeValue b)
        => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, BuiltinClasses.Operand<StringVal>(b, "<").Value) < 0);

    /// <summary>`>` —— 序数比,方向反过来。</summary>
    [ClassOp(">")]
    private static RuntimeValue Greater(RuntimeValue a, RuntimeValue b)
        => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, BuiltinClasses.Operand<StringVal>(b, ">").Value) > 0);

    /// <summary>`<=`。</summary>
    [ClassOp("<=")]
    private static RuntimeValue LessOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, BuiltinClasses.Operand<StringVal>(b, "<=").Value) <= 0);

    /// <summary>`>=`。</summary>
    [ClassOp(">=")]
    private static RuntimeValue GreaterOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(string.CompareOrdinal(((StringVal)a).Value, BuiltinClasses.Operand<StringVal>(b, ">=").Value) >= 0);
}
