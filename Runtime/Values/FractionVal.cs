namespace Ravel.Runtime;

public record FractionVal(int Num, int Den) : RuntimeValue
{
    public override RuntimeType Type => RuntimeType.Fraction;
    public override string ToString() => $"{Num}/{Den}";
}
