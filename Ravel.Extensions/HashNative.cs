namespace Ravel.Extensions;

using System.Security.Cryptography;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>摘要与 HMAC 的原语 —— 收**字节表**、交回**字节表**(和 `Encoding` / `Http` 一个形状,
/// 十六进制怎么写、要多少位,都是库(`lib/hash.rav`)的事)。
///
/// 算法按**名字**选(`sha256` / `sha512` / `sha1` / `md5`):一条原语管四种,加一种只动这里那张表。
/// 名字先归一化(大小写、`-` / `_` 都不计较)—— 网上抄来的摘要值五花八门,不该为连字符踩一脚。
/// 认不出的名字**当场报错**,不静默换个算法算。
///
/// 从前这是引擎里的 `SysHash`(`System.HashBytes`);现在它是**官方扩展**
/// (`plugins/Ravel.Extensions.dll`)里的 `Native.HashBytes` —— 它是 `lib/hash.rav` 的本机半边,
/// 不是语言的一部分。改的只有门牌号,里子一个字没动。</summary>
[RavelModule("Native")]
internal static class HashNative
{
    [RavelFn("HashBytes")]
    public static RuntimeValue HashBytes(RuntimeValue algo, RuntimeValue data) => Guarded("算摘要", () =>
        BytesList(Algorithm(algo).ComputeHash(Bytes(data, "HashBytes 的数据"))));

    /// <summary>文件的摘要 —— **流式**读,多大的文件都不会整个进内存(下载完校验走它)</summary>
    [RavelFn("HashFile")]
    public static RuntimeValue HashFile(RuntimeValue algo, RuntimeValue path) => Guarded("算文件摘要", () =>
    {
        var p = PathOf(path, "HashFile");
        NeedFile(p, "算文件摘要");
        using var file = File.OpenRead(p);
        return BytesList(Algorithm(algo).ComputeHash(file));
    });

    /// <summary>HMAC:带密钥的摘要(签名、校验回调都靠它)</summary>
    [RavelFn("HmacBytes")]
    public static RuntimeValue HmacBytes(RuntimeValue algo, RuntimeValue key, RuntimeValue data) => Guarded("算 HMAC", () =>
    {
        using var mac = Hmac(algo, Bytes(key, "HmacBytes 的密钥"));
        return BytesList(mac.ComputeHash(Bytes(data, "HmacBytes 的数据")));
    });

    /// <summary>算法名 → 那一台。`SHA-256` / `sha256` / `Sha_256` 是一个东西。</summary>
    private static HashAlgorithm Algorithm(RuntimeValue v)
    {
        var name = Text(v, "算法名").Value;
        return Normalize(name) switch
        {
            "sha256" => SHA256.Create(),
            "sha512" => SHA512.Create(),
            "sha1" => SHA1.Create(),
            "md5" => MD5.Create(),
            _ => throw Unknown(name),
        };
    }

    private static HMAC Hmac(RuntimeValue v, byte[] key)
    {
        var name = Text(v, "算法名").Value;
        return Normalize(name) switch
        {
            "sha256" => new HMACSHA256(key),
            "sha512" => new HMACSHA512(key),
            "sha1" => new HMACSHA1(key),
            "md5" => new HMACMD5(key),
            _ => throw Unknown(name),
        };
    }

    private static RuntimeException Unknown(string name)
        => Fail($"不认识的算法 '{name}'（有 sha256 / sha512 / sha1 / md5）");

    private static string Normalize(string name) => name.Replace("-", "").Replace("_", "").ToLowerInvariant();
}
