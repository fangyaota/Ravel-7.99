namespace Ravel.Runtime;

public sealed record PartialCtor : FunctionVal
{
    public ObjectVal Target { get; init; }
    public FunctionVal Partial { get; init; }

    public PartialCtor(ObjectVal target, FunctionVal partial)
        : base(null!, (_, _) => FunctionVal.PlaceholderBody("PartialCtor"))
    {
        Target = target;
        Partial = partial;
        // 它**就是**半成品 —— init 还有参数没收(见 FunctionVal.IsPartial)
        IsPartial = true;
    }

    public override string ToString()
        => Partial is LambdaVal lam
            ? "<function " + Target.Type.DisplayName + " " + lam.Describe() + ">"
            : "<function " + Target.Type.DisplayName + " " + Partial + ">";
}
