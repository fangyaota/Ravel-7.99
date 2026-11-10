using System.Security.Cryptography;

namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Native")]
internal static class RandomNative
{
    [RavelFn("NewRandom")]
    public static RuntimeValue NewRandom(RuntimeValue a) => Guarded("造随机源", () =>
    {
        var seed = Int(a, "NewRandom 的种子");
        var rng = new Random(seed);
        return NumberSource(() => rng.Next(1 << 30));
    });

    [RavelFn("SharedRandom")]
    public static RuntimeValue SharedRandom(RuntimeValue _)
        => NumberSource(() => Random.Shared.Next(1 << 30));

    [RavelFn("XoshiroRandom")]
    public static RuntimeValue XoshiroRandom(RuntimeValue a) => Guarded("造随机源", () =>
    {
        var seed = Int(a, "XoshiroRandom 的种子");
        var rng = new Xoshiro256(seed);
        return NumberSource(rng.Next30);
    });

    [RavelFn("RandomBytes")]
    public static RuntimeValue RandomBytes(RuntimeValue a) => Guarded("取随机字节", () =>
    {
        var n = Int(a, "RandomBytes 的个数");
        if (n < 0) throw Fail($"RandomBytes 的个数不能是负的，得到 {n}", ErrorKind.Value);
        var buf = new byte[n];
        RandomNumberGenerator.Fill(buf);
        return BytesList(buf);
    });

    public static FunctionVal NumberSource(Func<int> next) => Closure("_", _ => IntVal.Of(next()));
}
