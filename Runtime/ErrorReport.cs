namespace Ravel.Runtime;

public static class ErrorReport
{
    public static string Format(Exception ex) => ex switch
    {
        RuntimeException r => Render(r.Message, new SourceSpot(r.File, r.Line, r.Column), r.Trace),
        // 语法错误没有 Ravel 调用栈——源码本身就没解析成功
        SyntaxException s => Render(s.Message, s.Spot, []),
        _ => ex.Message,
    };

    public static string Warning(string message, SourceSpot spot) => "警告: " + Render(message, spot, []);

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
