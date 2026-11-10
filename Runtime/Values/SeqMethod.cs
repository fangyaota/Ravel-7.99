namespace Ravel.Runtime;

public enum SeqMode { Each, Map, Where, Fold, All, Any, Find, SortBy }

public sealed record SeqMethod(SeqMode Mode, int Arity)
    : FunctionVal(null!, (_, self) => new ControlFunction(ControlKind.SeqOp, Arity,
        RList<RuntimeValue>.Empty.Add(self).Add(IntVal.Of((int)Mode)))), ISelfBinding
{
    public static string Label(SeqMode m) => m switch
    {
        SeqMode.Each => "Each",
        SeqMode.Map => "Map",
        SeqMode.Where => "Where",
        SeqMode.Fold => "Fold",
        SeqMode.All => "All",
        SeqMode.Any => "Any",
        SeqMode.Find => "Find",
        _ => "SortBy",
    };
}
