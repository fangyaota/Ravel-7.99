using System.Security.Cryptography;

namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>对称加密与口令派生 —— 收**字节表**、交回**字节表**(和 `HashBytes` / `Encoding`
/// 一个形状:字符串怎么进、盐多长、轮数多少、结果怎么存,都是库(`lib/crypto.rav`)的事)。
///
/// **就三条**:AES-256-GCM 的封与拆,和 PBKDF2-SHA256。
///
/// 为什么是 GCM 而不是 CBC:**它不是只保密,还带认证** —— 密文被人改动过一位,
/// `AesOpen` 当场报错,而不是交回一段看着像明文、其实是垃圾的东西。密码学库里最老的那句
/// 「不要自己搭模式」在这儿就是"别给用户一个"加密但不认证"的选项"。
///
/// **nonce 由调用方给**(库那边拿 `Native.RandomBytes 12` 现取)。引擎里藏一个随机数发生器,
/// 测试就没法拿固定的 nonce 钉住结果 —— 而"解密结果对不对"这件事,只有钉得住才测得出来。
///
/// 它是 `lib/crypto.rav` 的本机半边,住在**官方扩展**(`plugins/Ravel.Extensions.dll`)里,
/// 对外是 `Native.AesSeal` 那几条 —— 从前挂在 `System` 底下,那不是它该在的地方。</summary>
[RavelModule("Native")]
internal static class CryptoNative
{
    /// <summary>AES-256-GCM 加密。交回的字节表是**密文后面接 16 字节认证标签**。
    ///
    /// nonce 复用是这套算法唯一能把自己作死的用法(同一密钥 + 同一 nonce 加密两段,
    /// 密钥就漏了)—— 所以库那边每次都现取一个,这一层只负责收。</summary>
    [RavelFn("AesSeal")]
    public static RuntimeValue AesSeal(RuntimeValue key, RuntimeValue nonce, RuntimeValue data) => Crypt("加密", () =>
    {
        var k = Key(key, "AesSeal");
        var n = Nonce(nonce, "AesSeal");
        var plain = Bytes(data, "AesSeal 的数据");

        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];
        using var aes = new AesGcm(k, TagBytes);
        aes.Encrypt(n, plain, cipher, tag);

        return BytesList([.. cipher, .. tag]);
    });

    /// <summary>AES-256-GCM 解密。**拆不开就报错**,不交回一段垃圾 ——
    /// 认证标签对不上,要么密钥/nonce 不对,要么数据在路上被人动过,这三种对调用方是同一件事。</summary>
    [RavelFn("AesOpen")]
    public static RuntimeValue AesOpen(RuntimeValue key, RuntimeValue nonce, RuntimeValue blob) => Crypt("解密", () =>
    {
        var k = Key(key, "AesOpen");
        var n = Nonce(nonce, "AesOpen");
        var all = Bytes(blob, "AesOpen 的密文");
        if (all.Length < TagBytes)
            throw Fail($"AesOpen: 密文太短（至少要有 {TagBytes} 字节的认证标签，得到 {all.Length}）", ErrorKind.Value);

        var cipher = all[..^TagBytes];
        var tag = all[^TagBytes..];
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(k, TagBytes);
        try
        {
            aes.Decrypt(n, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            throw Fail("AesOpen: 拆不开 —— 密钥或 nonce 不对，或者数据被改过", ErrorKind.Value);
        }

        return BytesList(plain);
    });

    /// <summary>PBKDF2-SHA256:从口令 + 盐推出 32 字节。**故意慢**是它的全部用处 ——
    /// 轮数不够高,一张显卡一秒钟能试几十亿个口令。</summary>
    [RavelFn("Pbkdf2")]
    public static RuntimeValue Pbkdf2(RuntimeValue password, RuntimeValue salt, RuntimeValue rounds) => Crypt("算口令密钥", () =>
    {
        var r = Int(rounds, "Pbkdf2 的迭代次数");
        if (r < 1)
            throw Fail($"Pbkdf2: 迭代次数要是正数，得到 {r}", ErrorKind.Value);

        return BytesList(Rfc2898DeriveBytes.Pbkdf2(
            Bytes(password, "Pbkdf2 的口令"), Bytes(salt, "Pbkdf2 的盐"),
            r, HashAlgorithmName.SHA256, KeyBytes));
    });

    /// <summary>密钥 32 字节(AES-256)、nonce 12 字节、标签 16 字节 —— 都是那套算法的**规格数**,
    /// 不是我们挑的。写在这儿(而不是让库那边传)是因为它们错一位就谁也不认谁的密文。</summary>
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>长度不对**当场说清楚要多少** —— "密钥不对"这四个字在加密这条路上最没用:
    /// 十个里九个是长度给错(拿了个十六位的口令就当密钥使)。</summary>
    private static byte[] Key(RuntimeValue v, string what)
    {
        var k = Bytes(v, $"{what} 的密钥");
        if (k.Length != KeyBytes)
            throw Fail(
                $"{what}: 密钥要 {KeyBytes} 字节（AES-256），得到 {k.Length} —— 从口令派生用 `Crypto.DeriveKey`", ErrorKind.Value);
        return k;
    }

    private static byte[] Nonce(RuntimeValue v, string what)
    {
        var n = Bytes(v, $"{what} 的 nonce");
        if (n.Length != NonceBytes)
            throw Fail($"{what}: nonce 要 {NonceBytes} 字节，得到 {n.Length}", ErrorKind.Value);
        return n;
    }

    /// <summary>密码学那一批的兜底。落到 .NET 上才可能出的岔子(平台不支持、算法不可用)在这儿翻成
    /// Ravel 错误;密钥/密文本身不对的那几条已经各报各的了。</summary>
    private static RuntimeValue Crypt(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or NotSupportedException
                                   or PlatformNotSupportedException)
        {
            throw Fail($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }
}
