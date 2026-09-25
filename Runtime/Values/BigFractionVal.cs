namespace Ravel.Runtime;

/// <summary>大数分数。和 <see cref="FractionVal"/> 一样**构造时就约分**、负号归分子
/// (理由见那边:显示一致 + 结构相等和 `==` 不能两套答案)。
/// 分子分母是 BigInteger,不会溢出,所以这里不用管 int 那套收窄。</summary>
public record BigFractionVal : RuntimeValue
{
    public System.Numerics.BigInteger Num { get; }
    public System.Numerics.BigInteger Den { get; }
    public override RuntimeType Type => RuntimeType.BigFraction;

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
