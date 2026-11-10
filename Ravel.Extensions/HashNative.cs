namespace Ravel.Extensions;

using System.Security.Cryptography;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Native")]
internal static class HashNative
{
    [RavelFn("HashBytes")]
    public static RuntimeValue HashBytes(RuntimeValue algo, RuntimeValue data) => Guarded("算摘要", () =>
        BytesList(Algorithm(algo).ComputeHash(Bytes(data, "HashBytes 的数据"))));

    [RavelFn("HashFile")]
    public static RuntimeValue HashFile(RuntimeValue algo, RuntimeValue path) => Guarded("算文件摘要", () =>
    {
        var p = PathOf(path, "HashFile");
        NeedFile(p, "算文件摘要");
        using var file = File.OpenRead(p);
        return BytesList(Algorithm(algo).ComputeHash(file));
    });

    [RavelFn("HmacBytes")]
    public static RuntimeValue HmacBytes(RuntimeValue algo, RuntimeValue key, RuntimeValue data) => Guarded("算 HMAC", () =>
    {
        using var mac = Hmac(algo, Bytes(key, "HmacBytes 的密钥"));
        return BytesList(mac.ComputeHash(Bytes(data, "HmacBytes 的数据")));
    });

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
