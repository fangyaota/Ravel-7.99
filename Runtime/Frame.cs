namespace Ravel.Runtime;

public abstract record Frame
{
    public Frame? Parent { get; internal set; }

    public AstNode? CallSite { get; internal set; }
    public Scope Scope { get; set; } = null!;
    public RList<RuntimeValue> Results { get; internal set; } = RList<RuntimeValue>.Empty;
    public int Count => Results.Count;
    public RuntimeValue Result(int i) => Results.At(i);
    public RuntimeValue Last => Results.Last;

    internal bool Captured;

    public Frame WithResult(RuntimeValue v)
    {
        if (Captured) return this with { Results = Results.Add(v) };
        Results = Results.Add(v);
        return this;
    }
}

public record NodeFrame : Frame
{
    public AstNode Node { get; internal set; }

    public NodeFrame(AstNode node) => Node = node;

    internal NodeFrame Rent(AstNode node, Frame parent)
    {
        Node = node;
        Parent = parent;
        Scope = parent.Scope;
        Results = RList<RuntimeValue>.Empty;
        CallSite = null;
        Captured = false;
        return this;
    }
}

public record BlockExecFrame(BlockExpr Block) : Frame
{
    public int Index { get; internal set; }

    internal BlockExecFrame Advance()
    {
        if (Captured) return this with { Index = Index + 1 };
        Index++;
        return this;
    }

    public bool Curried { get; init; }
}

public record ControlFrame(ControlKind Kind, RList<RuntimeValue> Args, RuntimeValue State) : Frame
{
    public T Arg<T>(int i, string owner) where T : RuntimeValue
    {
        var v = Args.At(i);
        return v as T ?? throw new RuntimeException(
            $"{owner} 的第 {i + 1} 个参数需要{ArgNames.Of(typeof(T))}，得到 {v.Type}", ErrorKind.Type);
    }
}

internal static class ArgNames
{
    public static string Of(Type t) => t switch
    {
        _ when t == typeof(BlockVal) => "代码块",
        _ when t == typeof(StringVal) => " string",
        _ when t == typeof(IntVal) => " int",
        _ when t == typeof(ListVal) => " list",
        _ when t == typeof(DictVal) => " dict",
        _ when t == typeof(ObjectVal) => "对象",
        _ when t == typeof(BoolVal) => " bool",
        _ when t == typeof(ClassVal) => "类",        // 比 FunctionVal 更具体,要排在它前面
        _ when t == typeof(FunctionVal) => "函数",
        _ => "值",
    };
}
