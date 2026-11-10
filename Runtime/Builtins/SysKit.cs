namespace Ravel.Runtime;

internal static class SysKit
{
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
        // **别的 .NET 异常也得兜** —— 引擎那条"别让 C# 异常漏到顶层"的规矩。
        // 从前只翻上面那六族,于是被调方抛个 `FormatException` 之类的会**绕过 Ravel 的
        // `try`**、一路打成"解释器内部错误"(实测:生成的适配层调 `String.Format`
        // 占位越界,整个脚本当场没了)。生成的适配层会调**任意** .NET,这个兜不能少。
        //
        // **引擎自己那几个当控制流使的放行** —— 它们不是"被调方抛的",重新包一层
        // 会把 `exit`(`ExitException`)和模式拒收(`RejectedException` ← `RuntimeException`)
        // 吃掉,那是把控制流嚼了。
        catch (Exception ex) when (ex is not RuntimeException and not ExitException and not SyntaxException)
        {
            throw new RuntimeException(
                $"{what}的时候 .NET 抛了 {ex.GetType().Name}: {ex.Message}", ErrorKind.Value);
        }
    }

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
        // **别的 .NET 异常也得兜** —— 引擎那条"别让 C# 异常漏到顶层"的规矩。
        // 从前只翻上面那六族,于是被调方抛个 `FormatException` 之类的会**绕过 Ravel 的
        // `try`**、一路打成"解释器内部错误"(实测:生成的适配层调 `String.Format`
        // 占位越界,整个脚本当场没了)。生成的适配层会调**任意** .NET,这个兜不能少。
        //
        // **引擎自己那几个当控制流使的放行** —— 它们不是"被调方抛的",重新包一层
        // 会把 `exit`(`ExitException`)和模式拒收(`RejectedException` ← `RuntimeException`)
        // 吃掉,那是把控制流嚼了。
        catch (Exception ex) when (ex is not RuntimeException and not ExitException and not SyntaxException)
        {
            throw new RuntimeException(
                $"{what}的时候 .NET 抛了 {ex.GetType().Name}: {ex.Message}", ErrorKind.Value);
        }
    }

    private static bool IsFsFault(Exception ex)
        => ex is IOException or UnauthorizedAccessException or ArgumentException
           or OverflowException
           or NotSupportedException or System.Security.SecurityException;

    public static RuntimeValue? Opt(DictVal d, string key)
        => d.Entries.TryGetValue(new StringVal(key), out var v) && v is not VoidVal ? v : null;

    public static string OptText(DictVal d, string key, string dflt) => Opt(d, key) is StringVal s ? s.Value : dflt;
    public static int OptInt(DictVal d, string key, int dflt) => Opt(d, key) is IntVal i ? i.Value : dflt;
    public static bool OptBool(DictVal d, string key, bool dflt) => Opt(d, key) is BoolVal b ? b.Value : dflt;

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
