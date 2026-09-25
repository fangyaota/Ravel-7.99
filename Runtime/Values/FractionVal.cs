namespace Ravel.Runtime;

/// <summary>分数。**构造时就约分**,负号统一归到分子。
///
/// 不约分有两个后果:
/// - 显示:`fraction 2 4` 打印成 "2/4"、`fraction 1 2 + fraction 1 2` 打印成 "4/4";
/// - 更要命的是相等性。record 的结构相等认为 `1/2` 和 `2/4` 是**两个不同的值**,
///   而 `==` 走的是数值比较说它们相等——Set/Dict 用结构相等,
///   于是 `{fraction 1 2, fraction 2 4}` 有 2 个元素。同一件事两套答案。
///
/// 分母恒为正:`fraction 1 -2` 是 -1/2。</summary>
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
