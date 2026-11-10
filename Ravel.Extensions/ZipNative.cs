namespace Ravel.Extensions;

using System.IO.Compression;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Native")]
internal static class ZipNative
{
    private static readonly Dictionary<int, ZipArchive> Opened = [];
    private static int _next;

    [RavelFn("ZipOpen")]
    public static RuntimeValue ZipOpen(RuntimeValue path) => Io("开归档", () =>
    {
        var p = PathOf(path, "ZipOpen");
        NeedFile(p, "开归档");
        var id = ++_next;
        Opened[id] = ZipFile.Open(p, ZipArchiveMode.Read);
        return IntVal.Of(id);
    });

    [RavelFn("ZipCreate")]
    public static RuntimeValue ZipCreate(RuntimeValue path) => Io("建归档", () =>
    {
        var p = PathOf(path, "ZipCreate");
        NeedParentDir(p, "建归档");
        var id = ++_next;
        Opened[id] = new ZipArchive(File.Create(p), ZipArchiveMode.Create);
        return IntVal.Of(id);
    });

    [RavelFn("ZipList")]
    public static RuntimeValue ZipList(RuntimeValue handle) => Io("列归档", () =>
    {
        var names = new List<RuntimeValue>();
        foreach (var e in Connection(handle, "ZipList").Entries)
            names.Add(new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
            {
                [new StringVal("name")] = new StringVal(e.FullName),
                [new StringVal("size")] = SizeOf(e.Length),
                [new StringVal("packed")] = SizeOf(e.CompressedLength),
                [new StringVal("isDir")] = new BoolVal(IsDir(e)),
            }));

        // 按名字排:库那边要按这一层切树,顺序稳不稳定是另一回事,但先给稳的
        names.Sort((a, b) => string.CompareOrdinal(Name(a), Name(b)));
        return new ListVal(names);
    });

    [RavelFn("ZipRead")]
    public static RuntimeValue ZipRead(RuntimeValue handle, RuntimeValue name) => Io("读成员", () =>
    {
        using var stream = Entry(handle, name, "ZipRead").Open();
        var ms = new MemoryStream();
        stream.CopyTo(ms);
        return BytesList(ms.ToArray());
    });

    [RavelFn("ZipWrite")]
    public static RuntimeValue ZipWrite(RuntimeValue handle, RuntimeValue name, RuntimeValue data) => Io("写成员", () =>
    {
        var e = Connection(handle, "ZipWrite").CreateEntry(Text(name, "ZipWrite 的名字").Value);
        using var stream = e.Open();
        stream.Write(Bytes(data, "ZipWrite 的内容"));
        return Void;
    });

    [RavelFn("ZipAddFile")]
    public static RuntimeValue ZipAddFile(RuntimeValue handle, RuntimeValue name, RuntimeValue path) => Io("加文件", () =>
    {
        var p = PathOf(path, "ZipAddFile");
        NeedFile(p, "加文件");
        var e = Connection(handle, "ZipAddFile").CreateEntry(Text(name, "ZipAddFile 的名字").Value);
        using var stream = e.Open();
        using var src = File.OpenRead(p);
        src.CopyTo(stream);
        return Void;
    });

    [RavelFn("ZipExtract")]
    public static RuntimeValue ZipExtract(RuntimeValue handle, RuntimeValue name, RuntimeValue path) => Io("解成员", () =>
    {
        var p = PathOf(path, "ZipExtract");
        NeedParentDir(p, "解成员");
        using var stream = Entry(handle, name, "ZipExtract").Open();
        using var dst = File.Create(p);
        stream.CopyTo(dst);
        return Void;
    });

    [RavelFn("ZipClose")]
    public static RuntimeValue ZipClose(RuntimeValue handle) => Io("关归档", () =>
    {
        if (Opened.Remove(Int(handle, "ZipClose 的句柄"), out var archive)) archive.Dispose();
        return Void;
    });

    // ── 下面是自己人 ──

    private static ZipArchive Connection(RuntimeValue h, string what)
    {
        var id = Int(h, $"{what} 的句柄");
        return Opened.TryGetValue(id, out var a)
            ? a
            : throw Fail($"这个归档已经关了（Close () 过,或者压根没开成）：句柄 {id}");
    }

    private static ZipArchiveEntry Entry(RuntimeValue h, RuntimeValue name, string what)
    {
        var archive = Connection(h, $"{what}");
        var n = Text(name, $"{what} 的名字").Value;
        if (archive.Mode == ZipArchiveMode.Create)
            throw Fail($"{what}: 这个归档是新建的（写的）,还没落盘 —— 读不了", ErrorKind.Io);

        return archive.GetEntry(n)
               ?? throw Fail($"{what}: 归档里没有 '{n}' 这个成员", ErrorKind.Key);
    }

    private static bool IsDir(ZipArchiveEntry e) => e.FullName.EndsWith('/');

    private static string Name(RuntimeValue v) => ((StringVal)((DictVal)v).Entries[new StringVal("name")]).Value;

    private static RuntimeValue SizeOf(long n) => Narrow(n);

    private static RuntimeValue Io(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                   or NotSupportedException or ArgumentException)
        {
            throw Fail($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }
}
