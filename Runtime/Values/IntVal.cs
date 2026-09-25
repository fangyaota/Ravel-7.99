namespace Ravel.Runtime;

public record IntVal(int Value) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Int;
    public override string ToString() => Value.ToString();
}
