namespace Ravel.Runtime;

public enum ControlKind { With, CallCC, Using, Eval, ClassInit, ImplMake, SeqOp, Compose, Then, TraitOp, CallAssign, CallReturn, CtorApply }

public sealed record ControlFunction(ControlKind Kind, int Arity, RList<RuntimeValue> Args)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("ControlFunction"))
{
    public IReadOnlyList<(string Name, ObjectVal Type)?>? Declared { get; init; }

    public ControlFunction Accumulate(RuntimeValue a) => this with { Args = Args.Add(a) };

    public bool IsFinalAfter(int incoming) => Args.Count + incoming >= Arity;

    public override string ToString() => Kind == ControlKind.Then && Args.Count == 2
        ? $"<function {Args.At(0)} >> {Args.At(1)}>"
        : $"<builtin {Kind} {Args.Count}/{Arity}>";
}
