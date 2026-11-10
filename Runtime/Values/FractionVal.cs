namespace Ravel.Runtime;

public record FractionVal : RuntimeValue
{
    public int Num { get; }
    public int Den { get; }
    public override ObjectVal Type => BuiltinClasses.Fraction;

    public FractionVal(int num, int den)
    {
        // 除零由调用方报(它们知道自己在算什么运算),这里只求别在 gcd 上炸
        if (den == 0)
        {
            Num = num;
            Den = den;
            return;
        }

        // 全程在 long 里做:-(long)int.MinValue 是安全的,Math.Abs(int.MinValue) 会抛
        long n = num, d = den;
        if (d < 0) { n = -n; d = -d; }
        var g = Gcd(Math.Abs(n), d);
        Num = (int)(n / g);
        Den = (int)(d / g);
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;   // a == 0 时,b 已经是 0,返回的是上面那一轮的 d,正好是 g = d
    }

    public override string ToString() => $"{Num}/{Den}";
}
