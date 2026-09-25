namespace Ravel.Runtime;

public record StringVal(string Value) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.String;
    public override string ToString() => Value;
}
