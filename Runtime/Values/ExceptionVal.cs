namespace Ravel.Runtime;

public record ExceptionVal(string Message) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Exception;
    public override string ToString() => Message;
}
