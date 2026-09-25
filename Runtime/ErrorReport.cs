namespace Ravel.Runtime;

/// <summary>把运行时错误渲染成带源码行和插入符的报告:
/// <code>
/// 未定义的变量 'x'
///   --> tests/foo.rav:3:12
///   3 |     print (x + 1)
///     |            ^
///   调用栈 (2 层):
///     在 tests/foo.rav:1:1
///     在 tests/foo.rav:8:1
/// </code>
/// 读不到源文件(被删了、来自 eval 字符串)就只报位置,不硬凑。</summary>
public static class ErrorReport
{
    /// <summary>单行版本,CLI 和测试都先用它取"标题行"</summary>
    public static string OneLine(RuntimeException ex) => ex.Message;

    public static string Format(RuntimeException ex)
        => Render(ex.Message, new SourceSpot(ex.File, ex.Line, ex.Column), ex.Trace);

    /// <summary>语法错误走同一份渲染。它没有 Ravel 调用栈——源码本身就没解析成功。</summary>
    public static string Format(SyntaxException ex) => Render(ex.Message, ex.Spot, []);

    /// <summary>位置 + 源码行 + 插入符 + 调用栈。两个 Format 都收到这里,免得画两遍。</summary>
    private static string Render(string message, SourceSpot spot, IReadOnlyList<string> trace)
    {
        var sb = new System.Text.StringBuilder(message);

        if (spot.Line > 0)
        {
            var where = spot.File is { Length: > 0 } f
                ? $"{ShortPath(f)}:{spot.Line}:{spot.Column}"
                : $"{spot.Line}:{spot.Column}";
            sb.Append("\n  --> ").Append(where);

            if (SourceLine(spot) is { } src)
            {
                var num = spot.Line.ToString();
                sb.Append("\n  ").Append(num).Append(" | ").Append(src);
                sb.Append("\n  ").Append(new string(' ', num.Length)).Append(" | ")
                  .Append(new string(' ', Math.Max(0, spot.Column - 1))).Append('^');
            }
        }

        if (trace.Count > 0)
        {
            sb.Append("\n  调用栈 (").Append(trace.Count).Append(" 层):");
            foreach (var t in trace) sb.Append("\n    在 ").Append(t);
        }

        return sb.ToString();
    }

    /// <summary>尽量用相对 cwd 的路径——报告短,而且测试可以拿它做精确比对。
    /// 分隔符统一成 `/`,免得期望输出跟着平台变。</summary>
    internal static string ShortPath(string path)
    {
        if (path.Contains('<')) return path;   // <repl>/<eval> 这类虚拟名,不是真路径

        try
        {
            var rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
            if (!rel.StartsWith("..")) return rel.Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            // 路径非法就原样显示
        }

        return path.Replace('\\', '/');
    }

    /// <summary>取出出错那一行的源码。文件不在/Eval 片段取不到就返回 null</summary>
    private static string? SourceLine(SourceSpot spot)
    {
        if (spot.File is not { Length: > 0 } path || spot.Line < 1) return null;
        try
        {
            if (!File.Exists(path)) return null;
            using var reader = new StreamReader(path);
            for (int i = 1; i <= spot.Line; i++)
            {
                var line = reader.ReadLine();
                if (line == null) return null;
                if (i == spot.Line) return line.Length > 200 ? line[..200] : line;
            }
        }
        catch (IOException)
        {
            // 读不到就不显示源码行,不影响报错误本身
        }

        return null;
    }
}
