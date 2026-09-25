namespace Ravel;

using System.Linq;

/// <summary>`_` 占位符的消糖:解析完一个表达式后跑一趟 AST 改写,
/// 把 `_ + 1` 变成 `(x: object) => { x + 1 }`。多个 `_` 按出现顺序变成嵌套 lambda 的参数。</summary>
public partial class Parser
{
    /// <summary>表达式里是否含占位符(不含就别做改写,省一趟遍历)</summary>
    private static bool HasHoles(Expression e) => e switch
    {
        HoleExpr => true,
        BinaryExpr b => HasHoles(b.Left) || HasHoles(b.Right),
        UnaryExpr u => HasHoles(u.Operand),
        CallExpr c => HasHoles(c.Function) || c.Arguments.Any(HasHoles),
        MemberAccess m => HasHoles(m.Object),
        PipeExpr p => HasHoles(p.Left) || HasHoles(p.Right),
        ListLiteral l => l.Elements.Any(HasHoles),
        _ => false
    };

    private Expression DesugarHoles(Expression e)
    {
        if (!HasHoles(e)) return e;
        // 收集所有 hole 索引，按出现顺序
        var holes = new List<int>();
        CollectHoles(e, holes);
        // 去重保持顺序
        var uniq = holes.Distinct().ToList();
        // 替换 HoleExpr → IdentifierExpr
        var body = ReplaceHoles(e);
        // 嵌套 lambda：最外层参数对应第一个 hole
        for (int i = uniq.Count - 1; i >= 0; i--)
        {
            var block = new BlockExpr([new ExpressionStatement(body) { Line = body.Line, Column = body.Column }])
                { Line = body.Line, Column = body.Column };
            body = new LambdaExpr(new Parameter("_" + i, "object"), block) { Line = body.Line, Column = body.Column };
        }

        return body;
    }

    private static void CollectHoles(Expression e, List<int> holes)
    {
        switch (e)
        {
            case HoleExpr h: holes.Add(h.Index); break;
            case BinaryExpr b:
                CollectHoles(b.Left, holes);
                CollectHoles(b.Right, holes);
                break;
            case UnaryExpr u: CollectHoles(u.Operand, holes); break;
            case CallExpr c:
                CollectHoles(c.Function, holes);
                foreach (var a in c.Arguments) CollectHoles(a, holes);
                break;
            case MemberAccess m: CollectHoles(m.Object, holes); break;
            case PipeExpr p:
                CollectHoles(p.Left, holes);
                CollectHoles(p.Right, holes);
                break;
            case ListLiteral l:
                foreach (var el in l.Elements) CollectHoles(el, holes);
                break;
        }
    }

    private static Expression ReplaceHoles(Expression e)
    {
        return e switch
        {
            HoleExpr h => new IdentifierExpr("_" + h.Index) { Line = e.Line, Column = e.Column },
            BinaryExpr b => new BinaryExpr(ReplaceHoles(b.Left), b.Op, ReplaceHoles(b.Right))
                { Line = e.Line, Column = e.Column },
            UnaryExpr u => new UnaryExpr(u.Op, ReplaceHoles(u.Operand)) { Line = e.Line, Column = e.Column },
            CallExpr c => new CallExpr(ReplaceHoles(c.Function), [.. c.Arguments.Select(ReplaceHoles)])
                { Line = e.Line, Column = e.Column },
            MemberAccess m => new MemberAccess(ReplaceHoles(m.Object), m.Member) { Line = e.Line, Column = e.Column },
            PipeExpr p => new PipeExpr(ReplaceHoles(p.Left), ReplaceHoles(p.Right))
                { Line = e.Line, Column = e.Column },
            ListLiteral l => new ListLiteral([.. l.Elements.Select(ReplaceHoles)]) { Line = e.Line, Column = e.Column },
            _ => e
        };
    }
}
