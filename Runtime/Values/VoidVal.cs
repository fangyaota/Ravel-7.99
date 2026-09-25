namespace Ravel.Runtime;

public record VoidVal : RuntimeValue
{
    public static readonly VoidVal Instance = new();
    public override ObjectVal Type => BuiltinClasses.Void;
    public override string ToString() => "()";
    private VoidVal() { }
}
