namespace Ravel;

using Ravel.Runtime;

/// <summary>**编译期**那几条警告(`--warn`)。判据只看树的**形状**,不看值 ——
/// 所以能在开跑**之前**一次说完,而不是等某一行执行到才吭声(那一行要是在没走到的
/// 分支里,就永远不吭声 —— 而"没走到的那一支"正是最容易写错的地方)。
///
/// 现在两条:
/// * `x: int` **单独成句**(`x: int` 只判断、什么都没绑,九成是漏了 `=`);
/// * `f () > 1` **零参调用后面跟运算符**(实参吃到运算符为止,读成的是 `f (() > 1)`)。
///
/// 走 <see cref="AstWalk"/> 的反射遍历,不手写每个节点 —— 理由见那边。
///
/// **「是不是忘了调用」不在这一趟**:它问的是"这个值是不是半成品",而 `if` 是个库函数、
/// 元数只有跑到才知道 —— 编不出来,照旧留在运行期(见 `Interpreter.Stack.WarnIfForgotCall`)。
///
/// 覆盖范围是**主脚本 + `eval` 的片段**:`using` 进来的库文件不查 —— 标准库自己的响声
/// 是噪音,而且 `predefined.rav` 每个进程只解析一遍(缓存着的),查它等于每建一个解释器
/// 白走一遍大树。**库里真有这类形状时不会漏报**:它跟着 `using` 进来到处跑,只是没人说。</summary>
internal static class CompileWarnings
{
    /// <summary>一个程序的全部语句(顶层那一块)。</summary>
    public static void Report(IReadOnlyList<Statement> statements, string? file)
        => Report(new Program([.. statements]) { Source = file });

    public static void Report(Program p)
    {
        // 同一个位置只报一次(和运行期那几条一个规矩)。遍历本身不会重复走到同一枚节点,
        // 但 `Report` 可能被同一个程序叫两遍(REPL 里重放那种),留着保险。
        var seen = new HashSet<(int, int)>();

        foreach (var (msg, at) in Holes(p))
        {
            if (!seen.Add((at.Line, at.Column))) continue;
            Console.Error.WriteLine(
                ErrorReport.Warning(msg, new SourceSpot(p.Source, at.Line, at.Column)));
        }
    }

    /// <summary>扫出来的那几处,**按位置排好** —— 输出次序要跟源码走,不能跟遍历走
    /// (遍历是反射来的,次序只保证"先左后右",跨层就不一定了)。</summary>
    private static IEnumerable<(string Msg, AstNode At)> Holes(Program p)
    {
        var hits = new List<(string Msg, AstNode At)>();

        foreach (var node in AstWalk.Nodes(p))
        {
            // `f () > 1`:实参是一条运算符链、最左端是 `()` 字面量,**而且**那个 `()`
            // 是**空参数表**写下来的。最后那半句不能省 —— 见 `CallExpr.BareUnitArg`。
            if (node is CallExpr { BareUnitArg: true, Argument: BinaryExpr arg } && StartsAtVoid(arg))
                hits.Add(("这一格的 `()` 被后面的运算符吃进**实参**里了"
                        + "（`f () > 1` 读成的是 `f (() > 1)`）—— 要拿调用结果去算就给它加括号："
                        + "`(f ()) > 1`", arg));
        }

        // `x: int` 单独成句。**块的最后一条不收** —— 最后一条的值就是块的值,
        // `if { x: int; } { … } { … }` 正是靠它。
        foreach (var block in Blocks(p))
            for (var i = 0; i < block.Count - 1; i++)
                if (block[i] is ExpressionStatement { Expr: BinaryExpr { Op: ":" } } st)
                    hits.Add(("这一句只是个类型判断，什么都没绑 —— 想定义是不是漏了 '='？", st));

        return hits.OrderBy(h => h.At.Line).ThenBy(h => h.At.Column);
    }

    /// <summary>所有块:顶层那一块 + 树里每个 <see cref="BlockExpr"/>。
    /// (语句自己的体是块,所以一条语句在哪个块里是**树**说的,不用另记父指针。)</summary>
    private static IEnumerable<IReadOnlyList<Statement>> Blocks(Program p)
    {
        yield return p.Statements;
        foreach (var node in AstWalk.Nodes(p))
            if (node is BlockExpr b) yield return b.Statements;
    }

    /// <summary>一条算式最左端是不是那个 `()` 字面量。顺着 `Left` 一路下去 ——
    /// `f () + 2 * 3` 的实参是 `(() + (2 * 3))`,最左那格还是它。</summary>
    private static bool StartsAtVoid(Expression e) => e switch
    {
        VoidLiteral => true,
        BinaryExpr b => StartsAtVoid(b.Left),
        _ => false,
    };
}
