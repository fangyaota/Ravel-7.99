namespace Ravel.Runtime;

public sealed record ContinuationVal(Frame? Captured, FunctionVal? Jump = null)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("ContinuationVal"))
{
    public static readonly ContinuationVal Default = new((Frame?)null);

    public RuntimeValue? Loading { get; init; }

    public bool IsDefault => Captured is null && Jump is null;

    public override string ToString() => IsDefault ? "<续延 default>" : "<continuation>";

    public override ObjectVal Type => BuiltinClasses.Continuation;
}
