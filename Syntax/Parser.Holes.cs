namespace Ravel;

using System.Linq;

public partial class Parser
{
    private static IEnumerable<Expression> Children(Expression e) => e switch
    {
        BinaryExpr b => [b.Left, b.Right],
        UnaryExpr u => [u.Operand],
        CallExpr c => [c.Function, c.Argument],
        MemberAccess m => [m.Object],
        PipeExpr p => [p.Left, p.Right],
        ListLiteral l => l.Elements,
        SetLiteral s => s.Elements,
        DictLiteral d => d.Entries.SelectMany(entry => new[] { entry.Key, entry.Value }),
        LambdaExpr { Sugar: true } l when l.Body.Statements is [ExpressionStatement es] => [es.Expr],
        DoExpr => [],
        _ => [],
    };

    private static bool HasHoles(Expression e)
        => e is HoleExpr || Children(e).Any(HasHoles);

    private Expression DesugarHoles(Expression e)
    {
        if (!HasHoles(e)) return e;
        // 收集所有 hole 索引，按出现顺序
        var holes = new List<int>();
        CollectHoles(e, holes);
        // 去重保持顺序
        var uniq = holes.Distinct().ToList();
        var body = ReplaceHoles(e);
        for (int i = uniq.Count - 1; i >= 0; i--)
        {
            var block = new BlockExpr([new ExpressionStatement(body) { Line = body.Line, Column = body.Column }])
                { Line = body.Line, Column = body.Column };
            body = new LambdaExpr(new Parameter("_" + uniq[i], new IdentifierExpr("object") { Line = body.Line, Column = body.Column }), block) { Line = body.Line, Column = body.Column };
        }

        return body;
    }

    private static void CollectHoles(Expression e, List<int> holes)
    {
        if (e is HoleExpr h)
        {
            holes.Add(h.Index);
            return;
        }

        foreach (var child in Children(e)) CollectHoles(child, holes);
    }

    private static Expression ReplaceHoles(Expression e)
    {
        return e switch
        {
            HoleExpr h => new IdentifierExpr("_" + h.Index) { Line = e.Line, Column = e.Column },
            BinaryExpr b => new BinaryExpr(ReplaceHoles(b.Left), b.Op, ReplaceHoles(b.Right))
                { Line = e.Line, Column = e.Column },
            UnaryExpr u => new UnaryExpr(u.Op, ReplaceHoles(u.Operand)) { Line = e.Line, Column = e.Column },
            CallExpr c => new CallExpr(ReplaceHoles(c.Function), ReplaceHoles(c.Argument))
                { Line = e.Line, Column = e.Column },
            MemberAccess m => new MemberAccess(ReplaceHoles(m.Object), m.Member) { Line = e.Line, Column = e.Column },
            PipeExpr p => new PipeExpr(ReplaceHoles(p.Left), ReplaceHoles(p.Right))
                { Line = e.Line, Column = e.Column },
            ListLiteral l => new ListLiteral([.. l.Elements.Select(ReplaceHoles)]) { Line = e.Line, Column = e.Column },
            SetLiteral s => new SetLiteral([.. s.Elements.Select(ReplaceHoles)]) { Line = e.Line, Column = e.Column },
            DictLiteral d => new DictLiteral([.. d.Entries.Select(entry =>
                new DictEntry(ReplaceHoles(entry.Key), ReplaceHoles(entry.Value)))]) { Line = e.Line, Column = e.Column },
            LambdaExpr { Sugar: true } l when l.Body.Statements is [ExpressionStatement es]
                => l with { Body = l.Body with { Statements = [es with { Expr = ReplaceHoles(es.Expr) }] } },
            _ => e
        };
    }
}
