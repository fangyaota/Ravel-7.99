namespace Ravel;

using System.Linq;

/// <summary>`_` 占位符的消糖:解析完一个表达式后跑一趟 AST 改写,
/// 把 `_ + 1` 变成 `(x: object) => { x + 1 }`。多个 `_` 按出现顺序变成嵌套 lambda 的参数。</summary>
public partial class Parser
{
    /// <summary>这个节点的**直接**子表达式。「有没有洞」「洞在哪」两趟共用这一份——
    /// 从前它们各写一个 switch,补新节点类型时容易只补一处:
    /// `_` 落在 Set/Dict 字面量里就没被认出来(HoleExpr 一路带到求值器,
    /// 用户看到的是「无法求值的节点类型: HoleExpr」)。改写那趟按类型重建节点,
    /// 没法共用,只能自己列。
    ///
    /// **不列 LambdaExpr / BlockExpr**:那是闭包边界,里面的 `_` 属于内层,
    /// 不该被外层这趟收走。</summary>
    private static IEnumerable<Expression> Children(Expression e) => e switch
    {
        BinaryExpr b => [b.Left, b.Right],
        UnaryExpr u => [u.Operand],
        CallExpr c => [c.Function, c.Argument],
        MemberAccess m => [m.Object],
        PipeExpr p => [p.Left, p.Right],
        ListLiteral l => l.Elements,
        SetLiteral s => s.Elements,
        // 键也是表达式了(`{"a": 1}`),所以两栏都要列 —— 漏了键的话 `{_: v}` 里的洞
        // 会一路带到求值器(正是这个 switch 从前漏掉 Set/Dict 的那个毛病)
        DictLiteral d => d.Entries.SelectMany(entry => new[] { entry.Key, entry.Value }),
        _ => [],
    };

    /// <summary>表达式里是否含占位符(不含就别做改写,省一趟遍历)</summary>
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
        // 替换 HoleExpr → IdentifierExpr
        var body = ReplaceHoles(e);
        // 嵌套 lambda：最外层参数对应第一个 hole。
        // 参数名用的是 hole 的**全局序号**(`uniq[i]`,和 ReplaceHoles 换出来的标识符同一个数),
        // 不是"第几个 hole"(`i`)—— `_` 的序号是解析期一个单调计数器(每语句归零),
        // 同一条语句里第二个洞里,`i` 与序号对不上就会生成 `(_0) => { _1 is bool; }` 这种
        // 「未定义的变量 '_1'」(同一个函数里的名字还得唯一:嵌套时同名会互相遮蔽)。
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
            _ => e
        };
    }
}
