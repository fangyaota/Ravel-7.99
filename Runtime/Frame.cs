namespace Ravel.Runtime;

/// <summary>显式调用栈帧——Parent/Results 不可变(持久化,供 callcc 捕获还原),Scope 可变(ravel 模块切换)</summary>
public abstract record Frame
{
    public Frame? Parent { get; init; }

    /// <summary>发起这个帧的那个 AST 节点。控制帧自己没有位置(它代表"一次内建调用"),
    /// 报错时靠它指回源码里的调用点 —— 否则 `fraction 1 0` 这类错只能报到最外层的块。</summary>
    public AstNode? CallSite { get; init; }
    public Scope Scope { get; set; } = null!;
    public RList<RuntimeValue> Results { get; init; } = RList<RuntimeValue>.Empty;
    public int Count => Results.Count;
    public RuntimeValue Result(int i) => Results.At(i);
    public RuntimeValue Last => Results.Last;
    public Frame WithResult(RuntimeValue v) => this with { Results = Results.Add(v) };
}

/// <summary>求值一个 AST 节点(语句/表达式)</summary>
public record NodeFrame(AstNode Node) : Frame;

/// <summary>执行一个代码块:逐语句求值,Results.Count=已完成的语句数,Results 存最近一条语句的值</summary>
public record BlockExecFrame(BlockExpr Block) : Frame;

/// <summary>控制帧:while/if/with/foreach/callcc/using/eval 的状态机。Args=收集的块/参数,State=循环累积值</summary>
public record ControlFrame(ControlKind Kind, RList<RuntimeValue> Args, RuntimeValue State) : Frame
{
    /// <summary>取第 i 个已收集的参数并收成 T,`owner` 是这一帧属于谁(报错文案用)。
    ///
    /// 控制帧的参数是按位置存的裸 RList,每个 Kind 各参数的含义只写在对应的 Step* 里。
    /// 从前一律写成 `(BlockVal)cf.Args.At(1)` —— 调用方给错类型(`with 5 6`)就抛
    /// C# 的 InvalidCastException,它不是 RuntimeException,Ravel 层的 `Ex.try` 接不住,
    /// 一路漏到顶层把程序打掉。走这里就成了正常的 Ravel 错误。</summary>
    public T Arg<T>(int i, string owner) where T : RuntimeValue
    {
        var v = Args.At(i);
        return v as T ?? throw new RuntimeException(
            $"{owner} 的第 {i + 1} 个参数需要{ArgNames.Of(typeof(T))}，得到 {v.Type}");
    }
}

/// <summary>C# 值类型 → 报错文案里该说的 Ravel 说法</summary>
internal static class ArgNames
{
    public static string Of(Type t) => t switch
    {
        _ when t == typeof(BlockVal) => "代码块",
        _ when t == typeof(StringVal) => " string",
        _ when t == typeof(ObjectVal) => "对象",
        _ when t == typeof(BoolVal) => " bool",
        _ when t == typeof(FunctionVal) => "函数",
        _ => "值",
    };
}
