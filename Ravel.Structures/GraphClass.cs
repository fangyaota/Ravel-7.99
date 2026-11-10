namespace Ravel.Runtime;

internal class GraphMethods
{
    // ── 构造 ──

    protected static GraphVal Make(string kind, bool directed, bool weighted, RuntimeValue arg)
    {
        var g = new GraphVal(kind, directed, weighted);
        foreach (var v in Seeded(arg, kind, weighted)) g.AddVertex(v);
        return g;
    }

    private static List<RuntimeValue> Seeded(RuntimeValue arg, string kind, bool options)
        => arg switch
        {
            VoidVal => [],
            DictVal when options => [],                 // `Weighted {"directed": true}` —— 那是选项,不是顶点
            _ => Structure.Seed(arg, kind),
        };

    // ── 顶点 ──

    [ClassMethod("AddVertex")]
    public static RuntimeValue AddVertex(RuntimeValue self, RuntimeValue v)
    {
        Val(self).AddVertex(v);
        return PluginKit.Void;
    }

    [ClassMethod("RemoveVertex")]
    public static RuntimeValue RemoveVertex(RuntimeValue self, RuntimeValue v)
        => new BoolVal(Val(self).RemoveVertex(PluginKit.Key(v, "RemoveVertex 的顶点")));

    [ClassMethod("HasVertex")]
    public static RuntimeValue HasVertex(RuntimeValue self, RuntimeValue v)
        => new BoolVal(Val(self).Has(PluginKit.Key(v, "HasVertex 的顶点")));

    [ClassMethod("Vertices")]
    public static RuntimeValue Vertices(RuntimeValue self) => new ListVal([.. Val(self).Order]);

    [ClassMethod("Count")]
    public static RuntimeValue Count(RuntimeValue self) => IntVal.Of(Val(self).Order.Count);

    [ClassMethod("Clear")]
    public static RuntimeValue Clear(RuntimeValue self)
    {
        var g = Val(self);
        g.Order.Clear();
        g.Adj.Clear();
        return PluginKit.Void;
    }

    // ── 边 ──

    [ClassMethod("AddEdge")]
    public static RuntimeValue AddEdge(RuntimeValue self, RuntimeValue a, RuntimeValue b)
    {
        var g = Val(self);
        var from = PluginKit.Key(a, $"{g.Kind}.AddEdge 的起点");
        var to = PluginKit.Key(b, $"{g.Kind}.AddEdge 的终点");
        return g.Weighted
            ? PluginKit.Closure("weight", w => { Link(g, from, to, w); return PluginKit.Void; })
            : Link(g, from, to, PluginKit.Void);
    }

    [ClassMethod("RemoveEdge")]
    public static RuntimeValue RemoveEdge(RuntimeValue self, RuntimeValue a, RuntimeValue b)
    {
        var g = Val(self);
        return new BoolVal(g.Unlink(PluginKit.Key(a, "RemoveEdge 的起点"), PluginKit.Key(b, "RemoveEdge 的终点")));
    }

    [ClassMethod("HasEdge")]
    public static RuntimeValue HasEdge(RuntimeValue self, RuntimeValue a, RuntimeValue b)
    {
        var g = Val(self);
        return new BoolVal(g.FindEdge(PluginKit.Key(a, "HasEdge 的起点"), PluginKit.Key(b, "HasEdge 的终点")) is not null);
    }

    [ClassMethod("Weight")]
    public static RuntimeValue Weight(RuntimeValue self, RuntimeValue a, RuntimeValue b)
    {
        var g = Val(self);
        if (!g.Weighted) throw PluginKit.Fail($"{g.Kind} 没有权重 —— 要权重用 Weighted", ErrorKind.Type);
        var from = g.Need(PluginKit.Key(a, "Weight 的起点"), "Weight");
        var to = PluginKit.Key(b, "Weight 的终点");
        return g.FindEdge(from, to) is { } e
            ? GraphVal.Number(e.Weight)
            : throw PluginKit.Fail($"Weight: {from} 和 {to} 之间没有边", ErrorKind.Key);
    }

    [ClassMethod("Edges")]
    public static RuntimeValue Edges(RuntimeValue self)
    {
        var g = Val(self);
        var rows = new List<RuntimeValue>();
        foreach (var (from, to, w) in g.AllEdges())
            rows.Add(new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
            {
                [new StringVal("from")] = from,
                [new StringVal("to")] = to,
                [new StringVal("weight")] = GraphVal.Number(w),
            }));
        return new ListVal(rows);
    }

    // ── 邻居 / 度 ──

    // 有向图里 `Neighbors` 是**出边**那头、`InNeighbors` 是入边那头;无向图两者同一条
    // (邻接表两边各存一份,所以 `In` 直接给 `Out` 就是答案 —— 见 GraphVal.In)。
    [ClassMethod("Neighbors")]
    public static RuntimeValue Neighbors(RuntimeValue self, RuntimeValue v)
    {
        var g = Val(self);
        var k = g.Need(PluginKit.Key(v, "Neighbors 的顶点"), "Neighbors");
        return new ListVal([.. g.Out(k).Select(e => e.To)]);
    }

    [ClassMethod("InNeighbors")]
    public static RuntimeValue InNeighbors(RuntimeValue self, RuntimeValue v)
    {
        var g = Val(self);
        var k = g.Need(PluginKit.Key(v, "InNeighbors 的顶点"), "InNeighbors");
        return new ListVal([.. g.In(k).Select(e => e.To)]);
    }

    [ClassMethod("Degree")]
    public static RuntimeValue Degree(RuntimeValue self, RuntimeValue v)
    {
        var g = Val(self);
        return IntVal.Of(g.Out(g.Need(PluginKit.Key(v, "Degree 的顶点"), "Degree")).Count);
    }

    [ClassMethod("InDegree")]
    public static RuntimeValue InDegree(RuntimeValue self, RuntimeValue v)
    {
        var g = Val(self);
        return IntVal.Of(g.In(g.Need(PluginKit.Key(v, "InDegree 的顶点"), "InDegree")).Count);
    }

    // ── 走一遍 ──

    [ClassMethod("Bfs")]
    public static RuntimeValue Bfs(RuntimeValue self, RuntimeValue from)
    {
        var g = Val(self);
        var start = g.Need(PluginKit.Key(from, "Bfs 的起点"), "Bfs");
        var seen = new HashSet<RuntimeValue> { start };
        var order = new List<RuntimeValue>();
        var queue = new Queue<RuntimeValue>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            order.Add(v);
            foreach (var e in g.Out(v))
                if (seen.Add(e.To)) queue.Enqueue(e.To);
        }
        return new ListVal(order);
    }

    [ClassMethod("Dfs")]
    public static RuntimeValue Dfs(RuntimeValue self, RuntimeValue from)
    {
        var g = Val(self);
        var start = g.Need(PluginKit.Key(from, "Dfs 的起点"), "Dfs");
        var seen = new HashSet<RuntimeValue> { start };
        var order = new List<RuntimeValue>();
        var stack = new Stack<RuntimeValue>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var v = stack.Pop();
            order.Add(v);
            var outs = g.Out(v);
            for (var i = outs.Count - 1; i >= 0; i--)      // 倒着压 —— 弹出来的顺序就照加进来的先后
                if (seen.Add(outs[i].To)) stack.Push(outs[i].To);
        }
        return new ListVal(order);
    }

    [ClassMethod("TopoSort")]
    public static RuntimeValue TopoSort(RuntimeValue self)
    {
        var g = Val(self);
        g.RequireDirected("TopoSort");
        var indeg = new Dictionary<RuntimeValue, int>();
        foreach (var v in g.Order) indeg[v] = 0;
        foreach (var v in g.Order)
            foreach (var e in g.Out(v)) indeg[e.To] += 1;

        var queue = new Queue<RuntimeValue>();
        foreach (var v in g.Order)
            if (indeg[v] == 0) queue.Enqueue(v);

        var order = new List<RuntimeValue>();
        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            order.Add(v);
            foreach (var e in g.Out(v))
                if (--indeg[e.To] == 0) queue.Enqueue(e.To);
        }

        if (order.Count != g.Order.Count)
            throw PluginKit.Fail("TopoSort: 图里有环，拓扑排序不存在", ErrorKind.Value);
        return new ListVal(order);
    }

    [ClassMethod("Components")]
    public static RuntimeValue Components(RuntimeValue self)
    {
        var g = Val(self);
        var seen = new HashSet<RuntimeValue>();
        var parts = new List<RuntimeValue>();
        foreach (var start in g.Order)
        {
            if (!seen.Add(start)) continue;
            var part = new List<RuntimeValue>();
            var queue = new Queue<RuntimeValue>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                part.Add(v);
                foreach (var e in g.Out(v))
                    if (seen.Add(e.To)) queue.Enqueue(e.To);
                if (!g.Directed) continue;                  // 无向图两个方向是同一批,看一遍就够
                foreach (var e in g.In(v))
                    if (seen.Add(e.To)) queue.Enqueue(e.To);
            }
            parts.Add(new ListVal(part));
        }
        return new ListVal(parts);
    }

    // ── 最短路 ──

    [ClassMethod("Path")]
    public static RuntimeValue Path(RuntimeValue self, RuntimeValue a, RuntimeValue b)
    {
        var g = Val(self);
        var from = g.Need(PluginKit.Key(a, "Path 的起点"), "Path");
        var to = g.Need(PluginKit.Key(b, "Path 的终点"), "Path");
        var (dist, prev) = Search(g, from);
        if (!dist.ContainsKey(to)) throw PluginKit.Fail($"Path: {from} 到 {to} 不连通", ErrorKind.Key);

        var route = new List<RuntimeValue> { to };
        for (var v = to; !v.Equals(from);) route.Add(v = prev[v]);
        route.Reverse();
        return new ListVal(route);
    }

    [ClassMethod("Distance")]
    public static RuntimeValue Distance(RuntimeValue self, RuntimeValue a, RuntimeValue b)
    {
        var g = Val(self);
        var from = g.Need(PluginKit.Key(a, "Distance 的起点"), "Distance");
        var to = g.Need(PluginKit.Key(b, "Distance 的终点"), "Distance");
        var (dist, _) = Search(g, from);
        return dist.TryGetValue(to, out var d)
            ? GraphVal.Number(d)
            : throw PluginKit.Fail($"Distance: {from} 到 {to} 不连通", ErrorKind.Key);
    }

    [ClassMethod("HasPath")]
    public static RuntimeValue HasPath(RuntimeValue self, RuntimeValue a, RuntimeValue b)
    {
        var g = Val(self);
        var from = g.Need(PluginKit.Key(a, "HasPath 的起点"), "HasPath");
        var to = g.Need(PluginKit.Key(b, "HasPath 的终点"), "HasPath");
        var (dist, _) = Search(g, from);
        return new BoolVal(dist.ContainsKey(to));
    }

    [ClassMethod("Distances")]
    public static RuntimeValue Distances(RuntimeValue self, RuntimeValue from)
    {
        var g = Val(self);
        var start = g.Need(PluginKit.Key(from, "Distances 的起点"), "Distances");
        var (dist, _) = Search(g, start);
        return new DictVal(dist.ToDictionary(kv => kv.Key, kv => GraphVal.Number(kv.Value)));
    }

    private static (Dictionary<RuntimeValue, double> Dist, Dictionary<RuntimeValue, RuntimeValue> Prev)
        Search(GraphVal g, RuntimeValue from)
    {
        var dist = new Dictionary<RuntimeValue, double> { [from] = 0 };
        var prev = new Dictionary<RuntimeValue, RuntimeValue>();

        if (!g.Weighted)
        {
            var queue = new Queue<RuntimeValue>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                foreach (var e in g.Out(v))
                    if (!dist.ContainsKey(e.To))
                    {
                        dist[e.To] = dist[v] + 1;
                        prev[e.To] = v;
                        queue.Enqueue(e.To);
                    }
            }
            return (dist, prev);
        }

        var pq = new PriorityQueue<RuntimeValue, double>();
        pq.Enqueue(from, 0);
        while (pq.TryDequeue(out var v, out var d))
        {
            if (d > dist[v]) continue;                     // 早先入队的那条,已经过期
            foreach (var e in g.Out(v))
            {
                var nd = d + e.Weight;
                if (dist.TryGetValue(e.To, out var old) && old <= nd) continue;
                dist[e.To] = nd;
                prev[e.To] = v;
                pq.Enqueue(e.To, nd);
            }
        }
        return (dist, prev);
    }

    private static RuntimeValue Link(GraphVal g, RuntimeValue from, RuntimeValue to, RuntimeValue w)
    {
        var weight = w is VoidVal ? 1.0 : WeightOf(w, g.Kind);
        g.Link(from, to, weight);
        return PluginKit.Void;
    }

    private static double WeightOf(RuntimeValue w, string kind)
    {
        var d = PluginKit.Num(w, $"{kind}.AddEdge 的权重");
        return d >= 0
            ? d
            : throw PluginKit.Fail($"{kind}.AddEdge: 权重不能是负数（最短路走 Dijkstra，负权它不管）", ErrorKind.Value);
    }

    private static GraphVal Val(RuntimeValue self) => (GraphVal)self;
}

[RavelModule("Structures")]
[RavelClass("Graph")]
internal sealed class GraphClass : GraphMethods
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg) => Make("Graph", directed: false, weighted: false, arg);
}

[RavelModule("Structures")]
[RavelClass("Digraph")]
internal sealed class DigraphClass : GraphMethods
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg) => Make("Digraph", directed: true, weighted: false, arg);
}

[RavelModule("Structures")]
[RavelClass("Weighted")]
internal sealed class WeightedClass : GraphMethods
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue arg)
        => Make("Weighted", arg is DictVal d && PluginKit.OptBool(d, "directed", false), weighted: true, arg);
}
