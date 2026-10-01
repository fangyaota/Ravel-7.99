namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

/// <summary>`System` 的文件系统原语 —— **只做 syscall,不做策略**。
///
/// 失败就报 Ravel 错误(中文、带路径);"要不要先问一句"交给 `FileExists` / `DirExists`
/// 那两个探针,它们**不报错**。路径基准 = 进程当前目录;不做沙箱 —— 和 `using` 找模块
/// 一个待遇,用户自己负责。那些预检查是为了消息说人话;`Fs` 那层兜底是为了不让 C# 异常
/// 漏到顶层(目录不存在、没权限、路径里有非法字符…… 漏出去会绕过 Ravel 的 try 把程序打掉,
/// 和这批原语一条道理)。</summary>
internal static class SysFiles
{
    [Sys("FileExists")]
    public static RuntimeValue FileExists(RuntimeValue a)
        => Fs("FileExists", () => new BoolVal(File.Exists(PathOf(a, "FileExists"))));

    [Sys("DirExists")]
    public static RuntimeValue DirExists(RuntimeValue a)
        => Fs("DirExists", () => new BoolVal(Directory.Exists(PathOf(a, "DirExists"))));

    [Sys("ReadText")]
    public static RuntimeValue ReadText(RuntimeValue a) => Fs("读文件", () =>
    {
        var p = PathOf(a, "ReadText");
        NeedFile(p, "读文件");
        return new StringVal(File.ReadAllText(p));
    });

    [Sys("WriteText")]
    public static RuntimeValue WriteText(RuntimeValue a, RuntimeValue b) => Fs("写文件", () =>
    {
        var p = PathOf(a, "WriteText");
        NeedParentDir(p, "写文件");
        File.WriteAllText(p, As<StringVal>(b, "WriteText 的内容").Value);
        return VoidVal.Instance;
    });

    [Sys("AppendText")]
    public static RuntimeValue AppendText(RuntimeValue a, RuntimeValue b) => Fs("追加文件", () =>
    {
        var p = PathOf(a, "AppendText");
        NeedParentDir(p, "追加文件");
        File.AppendAllText(p, As<StringVal>(b, "AppendText 的内容").Value);
        return VoidVal.Instance;
    });

    /// <summary>字节那两条:和文本那三条对称 —— 字节表就是 `Encoding` / `Random.Bytes` / `Bits`
    /// 用的那串 0..255。加它之前那些字节表只能在自己肚子里转,存不下来也读不回来。</summary>
    [Sys("ReadBytes")]
    public static RuntimeValue ReadBytes(RuntimeValue a) => Fs("读字节", () =>
    {
        var p = PathOf(a, "ReadBytes");
        NeedFile(p, "读字节");
        return BytesList(File.ReadAllBytes(p));
    });

    [Sys("WriteBytes")]
    public static RuntimeValue WriteBytes(RuntimeValue a, RuntimeValue b) => Fs("写字节", () =>
    {
        var p = PathOf(a, "WriteBytes");
        NeedParentDir(p, "写字节");
        File.WriteAllBytes(p, BytesOf(b, "WriteBytes 的内容"));
        return VoidVal.Instance;
    });

    /// <summary>删文件,或删**空**目录 —— 不提供递归删除(那是个危险默认值)</summary>
    [Sys("DeletePath")]
    public static RuntimeValue DeletePath(RuntimeValue a) => Fs("删除", () =>
    {
        var p = PathOf(a, "DeletePath");
        if (Directory.Exists(p))
        {
            if (Directory.EnumerateFileSystemEntries(p).Any())
                throw new RuntimeException($"删除失败: 目录不是空的（不递归删）—— {p}", ErrorKind.Io);
            Directory.Delete(p);
            return VoidVal.Instance;
        }

        if (!File.Exists(p)) throw new RuntimeException($"删除失败: 找不到要删的东西 —— {p}", ErrorKind.Io);
        File.Delete(p);
        return VoidVal.Instance;
    });

    /// <summary>父目录一并建;已存在不算错</summary>
    [Sys("CreateDir")]
    public static RuntimeValue CreateDir(RuntimeValue a) => Fs("建目录", () =>
    {
        Directory.CreateDirectory(PathOf(a, "CreateDir"));
        return VoidVal.Instance;
    });

    [Sys("ListDir")]
    public static RuntimeValue ListDir(RuntimeValue a) => Fs("列目录", () =>
    {
        var p = PathOf(a, "ListDir");
        if (!Directory.Exists(p)) throw new RuntimeException($"列目录失败: 找不到目录 —— {p}", ErrorKind.Io);

        var entries = new Dictionary<RuntimeValue, RuntimeValue>();
        foreach (var d in Directory.EnumerateDirectories(p)) entries[new StringVal(Path.GetFileName(d))] = new BoolVal(true);
        foreach (var f in Directory.EnumerateFiles(p)) entries[new StringVal(Path.GetFileName(f))] = new BoolVal(false);
        return new DictVal(entries);
    });

    // ── 问一个路径的事 ──

    [Sys("PathSize")]
    public static RuntimeValue PathSize(RuntimeValue a) => Fs("看文件大小", () =>
    {
        var p = PathOf(a, "PathSize");
        NeedFile(p, "看文件大小");
        return new IntVal((int)new FileInfo(p).Length);
    });

    [Sys("PathTime")]
    public static RuntimeValue PathTime(RuntimeValue a) => Fs("看修改时间", () =>
    {
        var p = PathOf(a, "PathTime");
        NeedFile(p, "看修改时间");
        return new StringVal(File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss"));
    });

    [Sys("CopyPath")]
    public static RuntimeValue CopyPath(RuntimeValue a, RuntimeValue b) => Fs("复制", () =>
    {
        var src = PathOf(a, "CopyPath");
        var dst = PathOf(b, "CopyPath");
        NeedFile(src, "复制");
        if (Directory.Exists(dst)) throw new RuntimeException($"复制失败: 目标是目录 —— {dst}", ErrorKind.Io);
        NeedParentDir(dst, "复制");
        File.Copy(src, dst, overwrite: true);
        return VoidVal.Instance;
    });

    [Sys("MovePath")]
    public static RuntimeValue MovePath(RuntimeValue a, RuntimeValue b) => Fs("移动", () =>
    {
        var src = PathOf(a, "MovePath");
        var dst = PathOf(b, "MovePath");
        NeedFile(src, "移动");
        NeedParentDir(dst, "移动");
        File.Move(src, dst, overwrite: true);
        return VoidVal.Instance;
    });

    [Sys("CurrentDir")]
    public static RuntimeValue CurrentDir(RuntimeValue _)
        => Fs("读当前目录", () => new StringVal(Directory.GetCurrentDirectory()));

    [Sys("ChDir")]
    public static RuntimeValue ChDir(RuntimeValue a) => Fs("切目录", () =>
    {
        var p = PathOf(a, "ChDir");
        if (!Directory.Exists(p)) throw new RuntimeException($"切目录失败: 找不到目录 —— {p}", ErrorKind.Io);
        Directory.SetCurrentDirectory(p);
        return VoidVal.Instance;
    });

    /// <summary>按行切开。Ravel 的字符串是不透明的(只有 Length 和拼接),这事只能在这层做:
    /// 按换行符切,顺手去掉每行末尾那个回车(Windows 的换行是回车+换行);
    /// 末尾的空行不产出(文件最后有个换行是常态,不该多出一行空的),中间的空行保留。</summary>
    [Sys("SplitLines")]
    public static RuntimeValue SplitLines(RuntimeValue a) => Fs("按行切", () =>
    {
        var text = As<StringVal>(a, "SplitLines 的内容").Value;
        var lines = new List<RuntimeValue>();
        if (text.Length > 0)
        {
            var parts = text.Split('\n');
            var last = parts.Length - 1;
            while (last > 0 && parts[last].Length == 0) last--;      // 末尾的空行不产出
            for (var i = 0; i <= last; i++)
                lines.Add(new StringVal(parts[i].TrimEnd('\r')));
        }

        return new ListVal(lines);
    });

    // ── 路径函数 ──
    // 交给 .NET 的 Path(分隔符、盘符、`..` 这些自己写容易错)。

    /// <summary>规整路径:去掉末尾的分隔符(`sub/` 和 `sub` 当同一个)。**不转绝对路径** ——
    /// 组件里存的是用户给的那个,打印出来才是人看的(代价是中途 ChDir 会让早先的条目走样)。
    /// `TrimEndingDirectorySeparator` 会保住根(`/`、`C:/`)。</summary>
    [Sys("PathClean")]
    public static RuntimeValue PathClean(RuntimeValue a) => Fs("规整路径", () =>
        new StringVal(Path.TrimEndingDirectorySeparator(PathOf(a, "PathClean"))));

    [Sys("PathJoin")]
    public static RuntimeValue PathJoin(RuntimeValue a, RuntimeValue b) => Fs("拼路径", () =>
        new StringVal(Path.Combine(PathOf(a, "PathJoin"), PathOf(b, "PathJoin"))));

    [Sys("PathDir")]
    public static RuntimeValue PathDir(RuntimeValue a) => Fs("取目录名", () =>
        new StringVal(Path.GetDirectoryName(PathOf(a, "PathDir")) ?? ""));

    [Sys("PathBase")]
    public static RuntimeValue PathBase(RuntimeValue a) => Fs("取文件名", () =>
        new StringVal(Path.GetFileName(PathOf(a, "PathBase"))));

    [Sys("PathExt")]
    public static RuntimeValue PathExt(RuntimeValue a) => Fs("取扩展名", () =>
        new StringVal(Path.GetExtension(PathOf(a, "PathExt"))));
}
