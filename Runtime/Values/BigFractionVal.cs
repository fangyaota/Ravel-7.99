namespace Ravel.Runtime;

public record BigFractionVal : RuntimeValue
{
    public System.Numerics.BigInteger Num { get; }
    public System.Numerics.BigInteger Den { get; }
    public override ObjectVal Type => BuiltinClasses.BigFraction;

    public BigFractionVal(System.Numerics.BigInteger num, System.Numerics.BigInteger den)
    {
        if (den.IsZero)
        {
            Num = num;
            Den = den;
            return;
        }

        if (den.Sign < 0)
        {
            num = -num;
            den = -den;
        }

        // GreatestCommonDivisor 不接受负数,而分子可能为负
        var g = System.Numerics.BigInteger.GreatestCommonDivisor(System.Numerics.BigInteger.Abs(num), den);
        Num = num / g;
        Den = den / g;
    }

    public override string ToString() => $"{Num}/{Den}";
}
