namespace Ravel.Runtime;

/// <summary>显式调用栈帧——Parent/Results 不可变(持久化,供 callcc 捕获还原),Scope 可变(ravel 模块切换)</summary>
public abstract record Frame
{
    public Frame? Parent { get; init; }
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
public record ControlFrame(ControlKind Kind, RList<RuntimeValue> Args, RuntimeValue State) : Frame;
