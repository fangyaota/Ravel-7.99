namespace Ravel.Runtime;

/// <summary>内置那一族共用的几件小工具:路径收束、参数收束、报错兜底、字节表换算。
///
/// 它们不属于哪个主题(文件、网络、环境变量都在用),所以单开一格。全 `public static`
/// **不是为了对外的 API** —— 是为了内置那些类能写 `using static Ravel.Runtime.SysKit;`
/// 直接叫名字(`Fs (…)` / `PathOf (…)`),不必层层 `SysKit.` 前缀。</summary>
internal static class SysKit
{
    /// <summary>路径收束:要一个字符串,**目录条目**(`Io.File "a.txt"` 那种,身上有 `Path`)也认。</summary>
    public static string PathOf(RuntimeValue v, string what)
        => v is StringVal s
            ? s.Value
            : throw new RuntimeException($"{what} 需要一个路径字符串，得到 {v.Type}", ErrorKind.Argument);

    public static void NeedFile(string p, string what)
    {
        if (Directory.Exists(p))
            throw new RuntimeException($"{what}: 这是个目录，不是文件 —— {p}", ErrorKind.Io);
        if (!File.Exists(p))
            throw new RuntimeException($"{what}: 找不到文件 —— {p}", ErrorKind.Io);
    }

    public static void NeedParentDir(string p, string what)
    {
        // 父目录**按用户写的那串**切(最后一个分隔符之前),不转绝对路径、也不交给 .NET 归一化 ——
        // 报错里要看到的就是他写的那串:`.io-test/nodir`,而不是被换成反斜杠的版本,
        // 更不是带盘符的完整路径。就一个文件名(没有分隔符)时没有父目录可查,交给下面去管。
        var cut = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
        if (cut <= 0) return;
        var dir = p[..cut];
        if (!Directory.Exists(dir))
            throw new RuntimeException($"{what}: 目录不存在 —— {dir}", ErrorKind.Io);
    }

    public static string EnvName(RuntimeValue v, string what)
    {
        var name = Interpreter.As<StringVal>(v, $"{what} 的名字").Value;
        if (name.Length == 0) throw new RuntimeException($"{what}: 变量名不能是空的", ErrorKind.Value);
        return name;
    }

    /// <summary>把一类 C# 异常兜成 Ravel 错误 —— 文件 / 进程 / 正则那批原语共用。
    ///
    /// **不让 C# 异常漏到顶层**是这批原语一条老规矩:漏出去会绕过 Ravel 的 `try`
    /// 把程序打掉(从前 `randint` 那条注释里说的也是这个)。</summary>
    public static RuntimeValue Fs(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (IsFsFault(ex))
        {
            throw new RuntimeException($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }

    /// <summary>`Fs` 的异步那一半 —— 判据是**同一个** <see cref="IsFsFault"/>,兜的是同一句话。
    /// 于是同一条操作走同步还是走句柄(交回任务那条),失败文案**一个字不差**。</summary>
    public static async Task<RuntimeValue> FsAsync(string what, Func<Task<RuntimeValue>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception ex) when (IsFsFault(ex))
        {
            throw new RuntimeException($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }

    /// <summary>什么算"文件/进程出的岔子" —— 两条路共用这一份,别各抄一份。</summary>
    private static bool IsFsFault(Exception ex)
        => ex is IOException or UnauthorizedAccessException or ArgumentException
           or OverflowException
           or NotSupportedException or System.Security.SecurityException;

    /// <summary>从一个**参数表**(dict)里取一个键,没给(或者给的是 `()`)就是 null。
    /// 「收一个 dict、缺的走默认」那几批原语(网络 / 加密)共用这一条 —— 加字段不用改签名。</summary>
    public static RuntimeValue? Opt(DictVal d, string key)
        => d.Entries.TryGetValue(new StringVal(key), out var v) && v is not VoidVal ? v : null;

    public static string OptText(DictVal d, string key, string dflt) => Opt(d, key) is StringVal s ? s.Value : dflt;
    public static int OptInt(DictVal d, string key, int dflt) => Opt(d, key) is IntVal i ? i.Value : dflt;
    public static bool OptBool(DictVal d, string key, bool dflt) => Opt(d, key) is BoolVal b ? b.Value : dflt;

    /// <summary>字节表(list,0..255)→ byte[]。元素不是字节就当场说清楚是**第几个**不对</summary>
    public static byte[] BytesOf(RuntimeValue v, string what)
    {
        if (v is not ListVal l)
            throw new RuntimeException($"{what}需要一个字节表（list），得到 {v.Type}", ErrorKind.Type);

        var bytes = new byte[l.Elements.Count];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (l.Elements[i] is not IntVal n || n.Value is < 0 or > 255)
                throw new RuntimeException($"{what}的第 {i} 个不是字节（要在 0..255 里，得到 {l.Elements[i]}）", ErrorKind.Value);
            bytes[i] = (byte)n.Value;
        }

        return bytes;
    }

    public static ListVal BytesList(byte[] bytes) => new([.. bytes.Select(b => (RuntimeValue)IntVal.Of(b))]);
}
