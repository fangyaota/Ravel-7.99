namespace Ravel.Runtime;

public record ExceptionVal(string Message) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.Exception;
    public override string ToString() => Message;
}
