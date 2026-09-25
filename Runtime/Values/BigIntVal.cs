namespace Ravel.Runtime;

public record BigIntVal(System.Numerics.BigInteger Value) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.BigInt;
    public override string ToString() => Value.ToString();
}
