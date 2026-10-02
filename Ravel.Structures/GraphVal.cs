namespace Ravel.Runtime;

/// <summary>图。**一张值类型管三种类**(`Graph` / `Digraph` / `Weighted`)——
/// 和 <see cref="StackVal"/> 里那句 `PluginKit.ClassOf("Stack")` 一个做法:
/// 类对象由构造时传进来的**名字**决定,所以 `typeof` / `is` 照样分得开。
///
/// 数据就两样:
/// * <see cref="Order"/> —— 顶点**按加进来的先后**(枚举、`Vertices ()`、`Edges ()` 都照这个序,
///   不用 `Dictionary` 的枚举顺序:.NET 不保证那个);
/// * <see cref="Adj"/> —— 邻接表。**有向图只存出边**;无向图两个方向各存一条(所以 `In*` 那两条
///   在无向图上和出边是同一批)。
///
/// **权重一律存 `double`**:`PluginKit.Num` 收口(五种数值类型全吃),交回时整数给 `int`、
/// 否则给 `float` —— 和 Ravel 自己 `1 + 2` 是 int、`1 + 2.5` 是 float 一个口径。
/// 负数在 `AddEdge` 就被挡下(最短路走的是 Dijkstra,负权它不管;与其给个错的答案,
/// 不如当场说清楚)。
///
/// **顶点是值类型**(数 / 字符串),和 `dict` 的键一个规矩 —— 把关的是 `PluginKit.Key`。</summary>
public record GraphVal : ObjectVal
{
    /// <summary>一条出边:到谁 + 权重(无权图恒为 1)</summary>
    public readonly record struct Edge(RuntimeValue To, double Weight);

    /// <summary>三个类名之一:`"Graph"` / `"Digraph"` / `"Weighted"`(报错和 `print` 都用它)</summary>
    public string Kind { get; }

    /// <summary>有向?(边只朝一个方向走)</summary>
    public bool Directed { get; }

    /// <summary>带权?(只有它才接得住 `Weight` / `AddEdge a b w`)</summary>
    public bool Weighted { get; }

    /// <summary>顶点,按加进来的先后</summary>
    public List<RuntimeValue> Order { get; } = [];

    /// <summary>邻接表。有向图只存出边;无向图两个方向各存一条</summary>
    public Dictionary<RuntimeValue, List<Edge>> Adj { get; } = [];

    public GraphVal(string kind, bool directed, bool weighted, Scope? members = null)
        : base(PluginKit.ClassOf(kind), members ?? new Scope())
    {
        Kind = kind;
        Directed = directed;
        Weighted = weighted;
    }

    // ── 长相 ──

    /// <summary>`Graph [a b c] {a-b b-c}` / `Digraph [a b] {a->b}` / `Weighted [a b] {a->b(2.5)}`</summary>
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

    /// <summary>"这个顶点在场" —— 不在就报错(要问在不在用 `Has`)。顶点没挂过 => 报错</summary>
    public RuntimeValue Need(RuntimeValue v, string what)
    {
        var k = PluginKit.Key(v, what + " 的顶点");
        return Has(k) ? k : throw PluginKit.Fail($"{what}: 图里没有顶点 {k}", ErrorKind.Key);
    }

    /// <summary>挂一个顶点(已经有就什么都不做)</summary>
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

    /// <summary>连一条边(两头都挂上顶点;已经有了就**改权重**而不是再来一条)。
    /// 无向图反着也存一条,自环只存一条。</summary>
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

    /// <summary>a → b 那条边,没有就是 null</summary>
    public Edge? FindEdge(RuntimeValue a, RuntimeValue b)
    {
        if (!Adj.TryGetValue(a, out var list)) return null;
        foreach (var e in list)
            if (e.To.Equals(b)) return e;
        return null;
    }

    /// <summary>出边(按加进来的先后)</summary>
    public List<Edge> Out(RuntimeValue v) => Adj.TryGetValue(v, out var list) ? list : [];

    /// <summary>入边 —— 有向图得**扫一遍**;无向图 <see cref="Out"/> 就是答案</summary>
    public List<Edge> In(RuntimeValue v)
    {
        if (!Directed) return Out(v);
        var found = new List<Edge>();
        foreach (var kv in Adj)
            foreach (var e in kv.Value)
                if (e.To.Equals(v)) found.Add(new Edge(kv.Key, e.Weight));
        return found;
    }

    /// <summary>全部的边。**无向图每条只出一次**(按两头谁先挂进来定方向),自环一次</summary>
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

    /// <summary>权重也可以是 int —— 和 Ravel `1 + 2` / `1 + 2.5` 一个读法</summary>
    public static RuntimeValue Number(double d)
        => d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 9e15
            ? PluginKit.Narrow((long)d, "图的权重")
            : new FloatVal(d);

    /// <summary>有向图才有的那几条(拓扑排序)先问一句</summary>
    public void RequireDirected(string what)
    {
        if (!Directed) throw PluginKit.Fail($"{what} 要有向图 —— 这是 {Kind}", ErrorKind.Type);
    }
}
