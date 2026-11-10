namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`real` 这个类给它的实例准备什么** —— 只有运算符这一份(方法在别的成员集里,
/// 建类时用 `MemberSet.Combine` 把几份接成一条)。
///
/// **算术全程 `double`**。从前这里走 `AsReal`(先转成 32 位 real 再算),于是
/// `Math.pi * 180` 得 565.4866943359375(float32 的 π 乘出来的),而 `180 * Math.pi` 得
/// 565.4866776461628 —— 同一个数换个顺序两个答案,还和比较运算符(那边一直是 `double`)
/// 也对不上。real 值本身就是 `double`,没必要经过 32 位。
///
/// 声明顺序就是 `Fields ()` 报的顺序(`MemberSet.Scan` 按声明收名字),所以这里
/// **保持原来注册的顺序**:先 `+ - * / %`,再 `**`,最后那六条比较。</summary>
internal static class RealMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(RealMembers), ObjectMembers.Set);

    /// <summary>`real + real` —— 全程 `double`(理由见类注释)。右操作数交给 `AsDouble`
    /// 收:左边由方法表保证是 real,右边掺个 int / bigint 也照样升上来。</summary>
    [ClassOp("+")]
    private static RuntimeValue Add(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "+") + BuiltinClasses.AsDouble(b, "+"));

    /// <summary>`real - real` —— 全程 `double`。</summary>
    [ClassOp("-")]
    private static RuntimeValue Subtract(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "-") - BuiltinClasses.AsDouble(b, "-"));

    /// <summary>`real * real` —— 全程 `double`。</summary>
    [ClassOp("*")]
    private static RuntimeValue Multiply(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "*") * BuiltinClasses.AsDouble(b, "*"));

    /// <summary>`real / real` —— 全程 `double`。除零**不报错**:IEEE 的 `Inf` / `NaN`
    /// 就是 real 自己的答案(int 那条才报「除数为零」,它装不下无穷)。</summary>
    [ClassOp("/")]
    private static RuntimeValue Divide(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "/") / BuiltinClasses.AsDouble(b, "/"));

    /// <summary>`real % real` —— 全程 `double`(C# 的 `%` 对 `double` 就是浮点取余)。</summary>
    [ClassOp("%")]
    private static RuntimeValue Modulo(RuntimeValue a, RuntimeValue b)
        => new RealVal(BuiltinClasses.AsDouble(a, "%") % BuiltinClasses.AsDouble(b, "%"));

    /// <summary>`real ** n` —— **乘方那两条总规矩写在这儿,别的类型照这一份**:
    ///
    /// **左边是什么类型就在那个类型里算** —— `int ** int` 还是 int(装不下照旧报
    /// 「大数用 bigint」,和 `*` 一样,不悄悄换成 bigint);掺了 real 就全程 `double`。
    /// **整数的负次幂交回 real**(`2 ** -1` = 0.5)。整数装不下 1/2,而"报错让人
    /// 自己去转 real"太不划算 —— Python 也是这么干的。**分数不受这条影响**:
    /// 负次幂只是上下颠倒,分数本来就装得下,所以还是分数。
    ///
    /// 优先级和结合性在解析器那头(`Parser.Expressions` 的 `ParsePower`):右结合、
    /// 而且比一元负号紧 —— `-2 ** 2` 是 `-(2 ** 2)` = -4。</summary>
    [ClassOp("**")]
    private static RuntimeValue Pow(RuntimeValue a, RuntimeValue b)
        => new RealVal(Math.Pow(BuiltinClasses.AsDouble(a, "**"), BuiltinClasses.AsDouble(b, "**")));

    /// <summary>`==`:比大小走 `CompareNumeric` —— **精确那一族(int / bigint / fraction /
    /// bigfraction)不经过 `double`**,两边通成 分子/分母 交叉相乘再比;掺了 real 才落回
    /// `double`。`NaN` 和谁都没法比,那边交 `null`,这里读成 false —— 正是 IEEE 要的那组答案。</summary>
    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "==") == 0);

    /// <summary>`!=` —— 同 `==`,取反(`null` 那条读成 true)。</summary>
    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "!=") != 0);

    /// <summary>`<` —— 精确那一族交叉相乘比,`NaN` 那条给 false。</summary>
    [ClassOp("<")]
    private static RuntimeValue Less(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "<") < 0);

    /// <summary>`>` —— 同上,方向反过来。</summary>
    [ClassOp(">")]
    private static RuntimeValue Greater(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, ">") > 0);

    /// <summary>`<=`。</summary>
    [ClassOp("<=")]
    private static RuntimeValue LessOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, "<=") <= 0);

    /// <summary>`>=`。</summary>
    [ClassOp(">=")]
    private static RuntimeValue GreaterOrEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.CompareNumeric(a, b, ">=") >= 0);
}
