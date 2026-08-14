namespace Ravel.Runtime;

/// <summary>运行时值的抽象基类</summary>
public abstract record RuntimeValue
{
    public abstract RuntimeType Type { get; }
}

public class RuntimeException(string message) : Exception(message);

/// <summary>参数类型不匹配(lambda 参数检查失败,供 | 交替 / 多 init 捕捉)</summary>
public sealed class TypeMismatchException() : RuntimeException("类型不匹配");

/// <summary>exit 专用异常——不被 EvalCall 捕获，直接向上抛出</summary>
public class ExitException(string message) : Exception(message);
