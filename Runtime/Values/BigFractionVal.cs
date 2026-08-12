namespace Ravel.Runtime;

public record BigFractionVal(System.Numerics.BigInteger Num, System.Numerics.BigInteger Den) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.BigFraction;
    public override string ToString() => $"{Num}/{Den}";
}
