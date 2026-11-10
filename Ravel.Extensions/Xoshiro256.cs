namespace Ravel.Runtime;

internal sealed class Xoshiro256
{
    private readonly ulong[] _s = new ulong[4];

    public Xoshiro256(int seed)
    {
        // splitmix64:同一个种子摊成四个互不相同的字
        var z = (ulong)(uint)seed;
        for (var i = 0; i < 4; i++) _s[i] = SplitMix64(ref z);
    }

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

    public int Next30() => (int)(Next64() >> 34);

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    private static ulong SplitMix64(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        var x = z;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}
