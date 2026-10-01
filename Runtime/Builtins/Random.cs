using System.Security.Cryptography;

namespace Ravel.Runtime;

/// <summary>随机数的**源头** —— 引擎只造"一枚取数的函数",哪几台、怎么用是 `lib/random.rav` 的事。
///
/// 引擎只造**一枚取数的函数**(`() => int`,范围 0 .. 2^30-1);"哪几台、怎么用"是库的事
/// (lib/random.rav:三台 —— 进程共享 / 带种子可复现 / 加密级 —— 各包一枚它,
/// 掷骰子 / 洗牌 / 抽样那些写成 `IRandom` 的默认实现)。
///
/// 交回**函数**而不是新造一个值类型:和 `Cached` 一个路子("是个函数,不是要实例化的类型"),
/// 引擎面最小,也不必动类型树。范围由这一层保证(原生闭包自己 `Next` 出来的)。</summary>
public partial class Interpreter
{
    [Sys("NewRandom")]
    private static RuntimeValue NewRandom(RuntimeValue a) => Fs("造随机源", () =>
    {
        var seed = As<IntVal>(a, "NewRandom 的种子").Value;
        var rng = new Random(seed);
        return NumberSource(() => rng.Next(1 << 30));
    });

    /// <summary>进程共享那台(`Random.Shared`)</summary>
    [Sys("SharedRandom")]
    private static RuntimeValue SharedRandom(RuntimeValue _)
        => NumberSource(() => Random.Shared.Next(1 << 30));

    /// <summary>xoshiro256** —— **算法定死**的那台:同一个种子在哪个运行时、哪台机器上都给同一串
    /// (`.NET` 的 `Random(seed)` 只保证同一个运行时里可复现)。写法见 `Runtime/Xoshiro256.cs`</summary>
    [Sys("XoshiroRandom")]
    private static RuntimeValue XoshiroRandom(RuntimeValue a) => Fs("造随机源", () =>
    {
        var seed = As<IntVal>(a, "XoshiroRandom 的种子").Value;
        var rng = new Xoshiro256(seed);
        return NumberSource(rng.Next30);
    });

    /// <summary>原始随机字节(0..255):加密那台的原料,也留给"就是要字节"(拿它自己拼整数)的人</summary>
    [Sys("RandomBytes")]
    private static RuntimeValue RandomBytes(RuntimeValue a) => Fs("取随机字节", () =>
    {
        var n = As<IntVal>(a, "RandomBytes 的个数").Value;
        if (n < 0) throw new RuntimeException($"RandomBytes 的个数不能是负的，得到 {n}", ErrorKind.Value);
        var buf = new byte[n];
        RandomNumberGenerator.Fill(buf);
        return new ListVal([.. buf.Select(b => (RuntimeValue)new IntVal(b))]);
    });

    private static FunctionVal NumberSource(Func<int> next)
        => new NativeClosure("_", BuiltinClasses.Any, (_, _) => new IntVal(next()));
}
