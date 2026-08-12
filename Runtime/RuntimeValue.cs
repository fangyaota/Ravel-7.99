namespace Ravel.Runtime;

/// <summary>运行时值的抽象基类</summary>
public abstract record RuntimeValue
{
    public abstract RuntimeType Type { get; }
}

public class RuntimeException(string message) : Exception(message);

/// <summary>exit 专用异常——不被 EvalCall 捕获，直接向上抛出</summary>
public class ExitException(string message) : Exception(message);
