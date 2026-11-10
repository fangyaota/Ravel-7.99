namespace Ravel.Runtime;

public sealed record WaitableVal(Task<RuntimeValue> Job) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Waitable;
    public override string ToString() => "<等待句柄>";
}
