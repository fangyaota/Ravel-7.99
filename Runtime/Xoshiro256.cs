namespace Ravel.Runtime;

/// <summary>xoshiro256** —— 一个小、快、质量好的通用伪随机数生成器
/// (David Blackman &amp; Sebastiano Vigna, 2018;**公有领域**,算法与参考实现见
/// https://prng.di.unimi.it/xoshiro256starstar.c)。
///
/// **库为什么要自带一台**:它是**定死**的算法 —— 同一个种子,在哪个 .NET 版本、哪台机器上
/// 都给同一串。.NET 那个 `Random(seed)` 只保证"同一个运行时里可复现",换版本就可能变
/// (见 `lib/random.rav` 里 `Random.Make` 的说明)。要"钉死一串、跨版本跨机器都一样",
/// 用它。
///
/// 状态 256 位(= 周期 2^256−1),每次从 64 位里取走一段喂给 Ravel。
/// **种子**用一个 int(和 `System.NewRandom` 一个口径),再用 splitmix64 把它摊成四个字 ——
/// 这是 xoshiro 官方推荐的播种法(种子只写进第一个字的话,前几个输出会明显偏)。
///
/// 它只在这儿当一个**算法**:往外仍然是"一枚 `() => int` 的函数",和另外两台
/// (`SharedRandom` / `NewRandom`)一个形状,库那边一视同仁(见 `lib/random.rav`)。
/// </summary>
internal sealed class Xoshiro256
{
    private readonly ulong[] _s = new ulong[4];

    public Xoshiro256(int seed)
    {
        // splitmix64:同一个种子摊成四个互不相同的字
        var z = (ulong)(uint)seed;
        for (var i = 0; i < 4; i++) _s[i] = SplitMix64(ref z);
    }

    /// <summary>下一个 64 位。xoshiro256** 那一套:先算输出,再推状态。</summary>
    private ulong Next64()
    {
        var result = Rotl(_s[1] * 5, 7) * 9;

        var t = _s[1] << 17;
        _s[2] ^= _s[0];
        _s[3] ^= _s[1];
        _s[1] ^= _s[2];
        _s[0] ^= _s[3];
        _s[2] ^= t;
        _s[3] = Rotl(_s[3], 45);

        return result;
    }

    /// <summary>给 Ravel 的那一段:取**高** 30 位(范围 0 .. 2^30-1 —— 和另外两台的契约一致)。
    /// 取高位不取低位:`**` 这一路的低位质量最差,高 30 位才是最干净的那一段。</summary>
    public int Next30() => (int)(Next64() >> 34);

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>官方的播种法:一个 64 位状态往外吐一个混合过的值,顺便原地推进。</summary>
    private static ulong SplitMix64(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        var x = z;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}
