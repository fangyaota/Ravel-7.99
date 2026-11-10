namespace Ravel.Runtime;

public sealed record NativeClosure(string ParamName, ObjectVal ParamType, Func<Scope, RuntimeValue, RuntimeValue> Fn)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("NativeClosure"))
{
    public override string ToString()
        => "<function (" + ParamName + ": " + ParamType.DisplayName + ") => <C#>>";
}
