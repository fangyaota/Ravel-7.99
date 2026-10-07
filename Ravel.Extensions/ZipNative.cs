namespace Ravel.Extensions;

using System.IO.Compression;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>ZIP 归档的原语 —— 库(`lib/zip.rav`)管"归档是个文件系统"那套,这里只负责
/// "开一个档、说一句要哪个成员"。
///
/// **归档不进 Ravel 堆**:和 SQLite 那边一个道理 —— `ZipArchive` 是要 Dispose 的本机句柄,
/// 交给 Ravel 的是个 **int 号**,库里那个 `Zip.Archive` 拿着号。
///
/// 两条路各留了一个**流式**口子,免得大文件先在堆里过一遍:
/// `ZipAddFile`(从磁盘加进来)和 `ZipExtract`(落到磁盘去),和 `Http` 那两条一个样子。
///
/// 没做的:`ZipArchiveMode.Update`(往已有档里**改**)。它要把整档读进内存再重写,
/// 加一个成员等于重写一遍整个文件 —— 那不是"追加"该有的代价。要改就打散重打,
/// 或者用别的工具。读、新建这两条够日常了。
///
/// 为什么是扩展:落到 `System.IO.Compression` 上,写不出 Ravel 源码 ——
/// 和 `Hash` / `Http` / `Sqlite` 一个待遇。</summary>
[RavelModule("Native")]
internal static class ZipNative
{
    private static readonly Dictionary<int, ZipArchive> Opened = [];
    private static int _next;

    /// <summary>开一个已有的档(读)。文件不是 zip、或者不在了,当场说人话</summary>
    [RavelFn("ZipOpen")]
    public static RuntimeValue ZipOpen(RuntimeValue path) => Io("开归档", () =>
    {
        var p = PathOf(path, "ZipOpen");
        NeedFile(p, "开归档");
        var id = ++_next;
        Opened[id] = ZipFile.Open(p, ZipArchiveMode.Read);
        return IntVal.Of(id);
    });

    /// <summary>新建一个档(写)。**同名的会被整个覆盖** —— 追加要走的不是这条路。
    ///
    /// 这里**不能**用 `ZipFile.Open (p, Create)`:那个只在文件**不存在**时才行,
    /// 撞上同名就是一句英文的 `IOException`(实测)。自己开 `File.Create`(它就是截断)
    /// 再包一层 —— 归档 Dispose 的时候会把底下那个流一起收掉(默认 `leaveOpen: false`)。
    /// </summary>
    [RavelFn("ZipCreate")]
    public static RuntimeValue ZipCreate(RuntimeValue path) => Io("建归档", () =>
    {
        var p = PathOf(path, "ZipCreate");
        NeedParentDir(p, "建归档");
        var id = ++_next;
        Opened[id] = new ZipArchive(File.Create(p), ZipArchiveMode.Create);
        return IntVal.Of(id);
    });

    /// <summary>档里有哪些成员 —— 一列 dict:`{name size packed isDir}`,**按名字排好**。
    /// 目录也是成员(zip 里叫 "sub/",名字以 `/` 结尾),`isDir` 就是这么判的。</summary>
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

    /// <summary>一个成员的内容 —— **整个读进字节表**。几百 MB 的那种走 `ZipExtract`</summary>
    [RavelFn("ZipRead")]
    public static RuntimeValue ZipRead(RuntimeValue handle, RuntimeValue name) => Io("读成员", () =>
    {
        using var stream = Entry(handle, name, "ZipRead").Open();
        var ms = new MemoryStream();
        stream.CopyTo(ms);
        return BytesList(ms.ToArray());
    });

    /// <summary>写一个成员(新建档那条路上用)。名字以 `/` 结尾就是**目录**。</summary>
    [RavelFn("ZipWrite")]
    public static RuntimeValue ZipWrite(RuntimeValue handle, RuntimeValue name, RuntimeValue data) => Io("写成员", () =>
    {
        var e = Connection(handle, "ZipWrite").CreateEntry(Text(name, "ZipWrite 的名字").Value);
        using var stream = e.Open();
        stream.Write(Bytes(data, "ZipWrite 的内容"));
        return Void;
    });

    /// <summary>把一个**磁盘文件**流式加进来 —— 内容不进 Ravel 堆(压一个大文件走它)</summary>
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

    /// <summary>把一个成员**流式落到磁盘** —— 内容不进 Ravel 堆(解一个大文件走它)</summary>
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

    /// <summary>收工。**写那条路上这一步不能省** —— 不到这儿,归档还没落盘。
    /// 已经关过的当"关过了"(幂等,和 `SqliteClose` 一个规矩)。</summary>
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

    /// <summary>按名字取一个成员。**目录不算成员**("sub/" 那种取不出内容来),
    /// 找不到就说清楚是哪个名字 —— 归档里的名字是 zip 的完整路径,`a/b.txt` 这样。</summary>
    private static ZipArchiveEntry Entry(RuntimeValue h, RuntimeValue name, string what)
    {
        var archive = Connection(h, $"{what}");
        var n = Text(name, $"{what} 的名字").Value;
        if (archive.Mode == ZipArchiveMode.Create)
            throw Fail($"{what}: 这个归档是新建的（写的）,还没落盘 —— 读不了", ErrorKind.Io);

        return archive.GetEntry(n)
               ?? throw Fail($"{what}: 归档里没有 '{n}' 这个成员", ErrorKind.Key);
    }

    /// <summary>zip 里的"目录"没有专门的标记,就是**名字以 `/` 结尾**的成员</summary>
    private static bool IsDir(ZipArchiveEntry e) => e.FullName.EndsWith('/');

    private static string Name(RuntimeValue v) => ((StringVal)((DictVal)v).Entries[new StringVal("name")]).Value;

    /// <summary>长度收窄成 int 或 bigint(和别处一个口径,zip 里可以装下 4 GB 以上的成员)</summary>
    private static RuntimeValue SizeOf(long n) => Narrow(n);

    /// <summary>zip 那一批的兜底:坏档、截断、加密的成员都在这一族里。
    /// `InvalidDataException` 就是"这不是个 zip" / "读到一半坏了"。</summary>
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
