namespace Ravel;

using Ravel.Runtime;

internal static class CompileWarnings
{
    public static List<(int Line, int Column)> Report(IReadOnlyList<Statement> statements, string? file)
        => Report(new Program([.. statements]) { Source = file });

    public static List<(int Line, int Column)> Report(Program p)
    {
        // 同一个位置只报一次(和运行期那几条一个规矩)。遍历本身不会重复走到同一枚节点,
        // 但 `Report` 可能被同一个程序叫两遍(REPL 里重放那种),留着保险。
        var seen = new HashSet<(int, int)>();
        var said = new List<(int, int)>();

        foreach (var (msg, at) in Holes(p))
        {
            if (!seen.Add((at.Line, at.Column))) continue;
            said.Add((at.Line, at.Column));
            Console.Error.WriteLine(
                ErrorReport.Warning(msg, new SourceSpot(p.Source, at.Line, at.Column)));
        }
        return said;
    }

    private static IEnumerable<(string Msg, AstNode At)> Holes(Program p)
    {
        var hits = new List<(string Msg, AstNode At)>();

        foreach (var node in AstWalk.Nodes(p))
        {
            // `f () > 1`:实参是一条运算符链、最左端是 `()` 字面量,**而且**那个 `()`
            // 是**空参数表**写下来的。最后那半句不能省 —— 见 `CallExpr.BareUnitArg`。
            // 脱糖之后那条链已经是 `().> 1`(`Lowering.MemberCall`),形状变了、意思没变,
            // 所以两种形状都认。
            if (node is CallExpr { BareUnitArg: true, Argument: var raw }
                && AsOperatorChain(raw) is { } arg && StartsAtVoid(arg))
                hits.Add(("这一格的 `()` 被后面的运算符吃进**实参**里了"
                        + "（`f () > 1` 读成的是 `f (() > 1)`）—— 要拿调用结果去算就给它加括号："
                        + "`(f ()) > 1`", arg));
        }

        // 另外两条都看**语句**、而且**块的最后一条不收** —— 最后一条的值就是块的值,
        // `if { x: int; } { … } { … }`(当条件)、`f := if { c } { t }`(故意存一个半成品)
        // 都靠它。
        foreach (var block in Blocks(p))
            for (var i = 0; i < block.Count - 1; i++)
            {
                if (block[i] is not ExpressionStatement { Expr: { } e } st) continue;

                // `x: int` 单独成句
                if (e is BinaryExpr { Op: ":" })
                    hits.Add(("这一句只是个类型判断，什么都没绑 —— 想定义是不是漏了 '='？", st));

                // **从前这儿还有一条**:`if { c } { t }`(少给一块)。
                // 2026-10-07 把 `if` 那个三参库函数删了,`true` / `false` 直接收两个块
                // (`cond { A } { B }`),那条按"调用链头是 `if`"判的警告就没得判了 ——
                // `c { A }` 的链头是 `c`,可能是任何东西,静态认不出来。
                // **那个坑没消失,只是移到了运行期**:少给一块的值是个 `PartialBool`,
                // 而 `WarnIfForgotCall`(`Interpreter.Stack.cs`)判的正是"值是个函数、
                // 且 `IsPartial`" —— `PartialBool` 构造时 `IsPartial = true` ✓。
            }

        return hits.OrderBy(h => h.At.Line).ThenBy(h => h.At.Column);
    }

    private static (string? Head, int Depth) Chain(Expression e)
    {
        var depth = 0;
        while (e is CallExpr c) { depth++; e = c.Function; }
        return (e is IdentifierExpr id ? id.Name : null, depth);
    }

    private static IEnumerable<IReadOnlyList<Statement>> Blocks(Program p)
    {
        yield return p.Statements;
        foreach (var node in AstWalk.Nodes(p))
            if (node is BlockExpr b) yield return b.Statements;
    }

    private static BinaryExpr? AsOperatorChain(Expression e) => e switch
    {
        BinaryExpr b => b,
        CallExpr { Function: MemberAccess m, Argument: var a } when OperatorSymbols.IsSymbol(m.Member)
            => new BinaryExpr(m.Object, m.Member, a) { Line = m.Line, Column = m.Column },
        _ => null,
    };

    private static bool StartsAtVoid(Expression e) => e switch
    {
        VoidLiteral => true,
        BinaryExpr b => StartsAtVoid(b.Left),
        // 脱糖后的形状:最左端可能还是 `().> 1` 这种链
        _ => AsOperatorChain(e) is { } chain && StartsAtVoid(chain.Left),
    };
}
