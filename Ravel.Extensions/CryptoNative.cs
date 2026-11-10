using System.Security.Cryptography;

namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Native")]
internal static class CryptoNative
{
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

    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

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
