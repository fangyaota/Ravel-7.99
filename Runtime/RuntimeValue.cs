namespace Ravel.Runtime;

/// <summary>运行时值的抽象基类</summary>
public abstract record RuntimeValue
{
    public abstract RuntimeType Type { get; }
}

/// <summary>Ravel 运行时错误。抛出点只管给消息——位置和调用栈由求值器在异常冒泡到
/// `StepOnce` 时补上(见 <c>Interpreter.Locate</c>),所以 90 多处 throw 不用各自操心这些。</summary>
public class RuntimeException(string message) : Exception(message)
{
    /// <summary>出错位置所在的源文件(沿帧链找到最近一个有 Source 的块)</summary>
    public string? File { get; internal set; }
    public int Line { get; internal set; }
    public int Column { get; internal set; }

    /// <summary>Ravel 层的调用栈,最近的在最前,形如 "file:line 在 <块 12:4> 里"</summary>
    public IReadOnlyList<string> Trace { get; internal set; } = [];

    /// <summary>已经补过位置了——内层 StepOnce 补完,外层就不会覆盖成更外侧的位置</summary>
    internal bool Located => Line != 0;
}

/// <summary>参数类型不匹配(lambda 参数检查失败,供 | 交替 / 多 init 捕捉)。
/// 消息由抛出点给全(哪个参数、要什么、得到什么)——从前它固定是「类型不匹配」,
/// 于是 `M { ... }` 这类调用失败时只看到四个字,完全不知道错在哪。</summary>
public sealed class TypeMismatchException(string message) : RuntimeException(message);

/// <summary>exit 专用异常——不被 EvalCall 捕获，直接向上抛出</summary>
public class ExitException(string message) : Exception(message);

/// <summary>错误在源码里的位置。运行时错误和语法错误都要报位置、都要画那行源码和插入符,
/// 所以位置单独成一个值,渲染只有一份实现(见 <see cref="ErrorReport"/>)。</summary>
/// <param name="File">源文件名;null / 空 表示 eval、REPL 这类没有真实文件的片段</param>
public sealed record SourceSpot(string? File, int Line, int Column);

/// <summary>语法/词法错误:源码本身写错了,不是程序跑出来的。
/// 位置来自 token、也没有 Ravel 调用栈可讲——所以它和 <see cref="RuntimeException"/> 分开:
/// 以前它只是个裸的 System.Exception,于是调用方分不清「用户代码写错了」和「解释器有 bug」,
/// 测试运行器更是把两者都当普通的 Error 放行。</summary>
public sealed class SyntaxException(string message, SourceSpot spot) : Exception(message)
{
    public SourceSpot Spot { get; } = spot;
}
