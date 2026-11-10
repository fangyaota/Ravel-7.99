namespace Ravel.Runtime;

public record GraphVal : ObjectVal
{
    public readonly record struct Edge(RuntimeValue To, double Weight);

    public string Kind { get; }

    public bool Directed { get; }

    public bool Weighted { get; }

    public List<RuntimeValue> Order { get; } = [];

    public Dictionary<RuntimeValue, List<Edge>> Adj { get; } = [];

    public GraphVal(string kind, bool directed, bool weighted, Scope? members = null)
        : base(PluginKit.ClassOf(kind), members ?? new Scope())
    {
        Kind = kind;
        Directed = directed;
        Weighted = weighted;
    }

    // ── 长相 ──

    public override string ToString() => PluginKit.Guard(() =>
    {
        var edges = new List<string>();
        foreach (var (from, to, w) in AllEdges())
        {
            var arrow = Directed ? "->" : "-";
            edges.Add(Weighted ? $"{from}{arrow}{to}({Number(w)})" : $"{from}{arrow}{to}");
        }
        return $"{Kind} [{string.Join(" ", Order)}] {{{string.Join(" ", edges)}}}";
    });

    // ── 顶点 ──

    public bool Has(RuntimeValue v) => Adj.ContainsKey(v);

    public RuntimeValue Need(RuntimeValue v, string what)
    {
        var k = PluginKit.Key(v, what + " 的顶点");
        return Has(k) ? k : throw PluginKit.Fail($"{what}: 图里没有顶点 {k}", ErrorKind.Key);
    }

    public RuntimeValue AddVertex(RuntimeValue v)
    {
        var k = PluginKit.Key(v, $"{Kind}.AddVertex 的顶点");
        Ensure(k);
        return k;
    }

    private void Ensure(RuntimeValue v)
    {
        if (Adj.ContainsKey(v)) return;
        Order.Add(v);
        Adj[v] = [];
    }

    public bool RemoveVertex(RuntimeValue v)
    {
        if (!Adj.Remove(v)) return false;
        Order.Remove(v);
        foreach (var list in Adj.Values) list.RemoveAll(e => e.To.Equals(v));
        return true;
    }

    // ── 边 ──

    public void Link(RuntimeValue a, RuntimeValue b, double w)
    {
        Ensure(a);
        Ensure(b);

        var list = Adj[a];
        var at = list.FindIndex(e => e.To.Equals(b));
        if (at >= 0) list[at] = new Edge(b, w);
        else list.Add(new Edge(b, w));

        if (!Directed && !b.Equals(a))
        {
            var back = Adj[b];
            var bt = back.FindIndex(e => e.To.Equals(a));
            if (bt >= 0) back[bt] = new Edge(a, w);
            else back.Add(new Edge(a, w));
        }
    }

    public bool Unlink(RuntimeValue a, RuntimeValue b)
    {
        if (!Has(a) || !Has(b)) return false;
        var gone = Adj[a].RemoveAll(e => e.To.Equals(b)) > 0;
        if (!Directed && !b.Equals(a)) Adj[b].RemoveAll(e => e.To.Equals(a));
        return gone;
    }

    public Edge? FindEdge(RuntimeValue a, RuntimeValue b)
    {
        if (!Adj.TryGetValue(a, out var list)) return null;
        foreach (var e in list)
            if (e.To.Equals(b)) return e;
        return null;
    }

    public List<Edge> Out(RuntimeValue v) => Adj.TryGetValue(v, out var list) ? list : [];

    public List<Edge> In(RuntimeValue v)
    {
        if (!Directed) return Out(v);
        var found = new List<Edge>();
        foreach (var kv in Adj)
            foreach (var e in kv.Value)
                if (e.To.Equals(v)) found.Add(new Edge(kv.Key, e.Weight));
        return found;
    }

    public List<(RuntimeValue From, RuntimeValue To, double Weight)> AllEdges()
    {
        var seen = new Dictionary<RuntimeValue, int>();     // 顶点 → 它在 Order 里的位置
        for (var i = 0; i < Order.Count; i++) seen[Order[i]] = i;

        var edges = new List<(RuntimeValue, RuntimeValue, double)>();
        foreach (var from in Order)
            foreach (var e in Out(from))
            {
                if (!Directed && seen[from] > seen[e.To]) continue;   // 反着那条已经出过了
                edges.Add((from, e.To, e.Weight));
            }
        return edges;
    }

    public static RuntimeValue Number(double d)
        => d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 9e15
            ? PluginKit.Narrow((long)d)
            : new RealVal(d);

    public void RequireDirected(string what)
    {
        if (!Directed) throw PluginKit.Fail($"{what} 要有向图 —— 这是 {Kind}", ErrorKind.Type);
    }
}
