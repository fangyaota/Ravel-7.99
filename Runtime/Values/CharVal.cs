namespace Ravel.Runtime;

public record CharVal(char Value) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Char;

    public override string ToString() => Value.ToString();
}
