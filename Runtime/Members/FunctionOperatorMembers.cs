namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`Function` 这个类给它的实例准备什么** —— 只有运算符这一份(方法在别的成员集里,
/// 建类时用 `MemberSet.Combine` 把几份接成一条)。
///
/// 两条运算符都**交回一个控制帧** `ControlFunction`:内置运算符的体是纯 C#,拿不到解释器,
/// 要调 Ravel 函数(那得推帧)只能这么走。语义在 `Interpreter.Control` 里。
///
/// 两个符号都和"别处的同名运算符"分工清楚,落在**不同的类型**上:`8 >> 1` 还是算术右移
/// (走 `Integer` 那张表),`true | false` 是逻辑或(走 `Bool` 那张表)—— 这里是函数那一档。
///
/// 声明顺序就是 `Fields ()` 报的顺序(`MemberSet.Scan` 按声明收名字),所以这里
/// **保持原来注册的顺序**:`|` 然后 `>>`。</summary>
internal static class FunctionOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(FunctionOperatorMembers), ObjectMembers.Set);

    /// <summary>函数交替 `|`(前一个不收这个参数就试下一个)→ `Alternate` 控制帧。
    ///
    /// 左边已经是交替时**摊平**成一个分支列表:`x | y | z` 解析成 `(x | y) | z`,
    /// 嵌套着写的话内层是在外层 try 之外才抛 `TypeMismatchException` 的
    /// (`CallInto` 只推帧,不调用),外层那个 catch 早返回了 —— 第三个分支永远试不到。</summary>
    [ClassOp("|")]
    private static RuntimeValue Alternate(RuntimeValue a, RuntimeValue b)
    {
        var branches = a is ControlFunction { Kind: ControlKind.Alternate } chain
            ? chain.Args.Add(b)
            : RList<RuntimeValue>.Empty.Add(a).Add(b);
        return new ControlFunction(ControlKind.Alternate, 1, branches);
    }

    /// <summary>函数组合 `>>`(和移位同名,但落在 `Function` 上,见类注释)。
    ///
    /// 方向**照着 `|>` 来**:`f >> g` = 先 f 后 g,于是 `x |> (f >> g)` ≡ `x |> f |> g`。
    /// 交回 `Then` 控制帧,语义见 `Interpreter.Control` 的 `StepThen`。</summary>
    [ClassOp(">>")]
    private static RuntimeValue Then(RuntimeValue a, RuntimeValue b)
        => new ControlFunction(ControlKind.Then, 1, RList<RuntimeValue>.Empty.Add(a).Add(b));
}
