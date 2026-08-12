namespace Ravel.Runtime;

public record BigIntVal(System.Numerics.BigInteger Value) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.BigInt;
    public override string ToString() => Value.ToString();
}
