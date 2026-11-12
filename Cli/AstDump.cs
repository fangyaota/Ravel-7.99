namespace Ravel.Cli;

using System.Globalization;
using System.Reflection;
using System.Text;
using Ravel.Runtime;

internal static class AstDump
{
    public static bool Run(IReadOnlyList<string> paths, bool source = false, string? outDir = null)
    {
        if (paths.Count == 0)
        {
            Console.WriteLine("用法: ravel ast [--source | --out <目录>] <文件或目录>…");
            return false;
        }

        //
        var cwd = Directory.GetCurrentDirectory();
        var files = new List<(string Path, string Rel)>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                files.AddRange(Directory.GetFiles(path, "*.rav", SearchOption.AllDirectories).Select(Rel));
            else if (File.Exists(path)) files.Add(Rel(path));
            else
            {
                Console.WriteLine($"找不到: {path}");
                return false;
            }
        }

        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        (string Path, string Rel) Rel(string f)
        {
            var rel = Path.GetRelativePath(cwd, Path.GetFullPath(f));
            return (f, rel.StartsWith("..") ? Path.GetFileName(f) : rel);
        }

        //
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (IOException) { /* 没有控制台,不管它 */ }
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));

        var sb = new StringBuilder();
        var failed = 0;
        var broken = 0;    // 印出来再解析,树对不上
        var skipped = 0;   // 含内置占位符,没法往返
        foreach (var (file, rel) in files)
        {
            var title = "──── " + file.Replace('\\', '/') + " ────";
            string? dump = null, text = null, error = null;

            try
            {
                var src = File.ReadAllText(file);
                var flow = Parser.DeclaresMoreControlFlow(src);
                var program = Parser.ParseSource(src, file, flow);

                var wantSource = outDir != null || source;
                if (outDir != null || !source)
                {
                    sb.Clear().Append(title).Append('\n');
                    Render(sb, program, 0, positions: true, meta: true);
                    dump = sb.ToString();
                }

                if (wantSource)
                {
                    var printed = AstPrinter.Program(program, verbatim: true);
                    text = Comment(title) + "\n" + printed + "\n";
                    try
                    {
                        var raw = Parser.ParseSourceUnlowered(src, file, flow);
                        var rawText = AstPrinter.Program(raw, verbatim: true);
                        if (rawText.Contains("<builtin>"))
                        {
                            // 内置值没有源码,跳过这一份
                            skipped++;
                        }
                        else
                        {
                            var back = Parser.ParseSourceUnlowered(rawText, file, flow);
                            var was = Canonical(raw);
                            var now = Canonical(back);
                            if (was != now)
                            {
                                Console.Error.WriteLine($"{file}: 印出来再解析,树对不上 —— {FirstDifference(was, now)}");
                                broken++;
                            }
                        }
                    }
                    catch (Exception ex) when (ex is SyntaxException or RuntimeException)
                    {
                        Console.Error.WriteLine($"{file}: 印出来解析不了 —— {FirstLine(ex)}");
                        broken++;
                    }
                }
            }
            catch (Exception ex) when (ex is SyntaxException or RuntimeException)
            {
                error = "!! " + ErrorReport.Format(ex);
                failed++;
            }

            if (outDir is null)
            {
                if (error is not null)
                    stdout.Write((source ? Comment(title + "\n" + error) : title + "\n" + error) + "\n");
                else
                    stdout.Write(dump ?? text);
                continue;
            }

            var stem = Path.Combine(outDir, Path.ChangeExtension(rel, null)!);
            WriteFile(stem + ".ast.txt", error is not null ? title + "\n" + error + "\n" : dump!);
            // 印成一个空壳反而像是"印出来了"
            if (text is not null) WriteFile(stem + ".src.rav", text);
        }

        var tail = failed > 0 ? $",其中 {failed} 个报错" : "";
        if (broken > 0) tail += $",{broken} 个往返后树对不上";
        if (skipped > 0) tail += $",{skipped} 个含内置占位符跳过自验";
        Console.Error.WriteLine(outDir is null
            ? $"\n{(source ? "印出" : "转储")} {files.Count} 个文件{tail}"
            : $"\n{files.Count} 个文件,每个两份 → {outDir.Replace('\\', '/')}{tail}");
        stdout.Flush();
        return failed == 0 && broken == 0;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        Console.Error.WriteLine("  " + path.Replace('\\', '/'));
    }

    private static string FirstLine(Exception ex) => ErrorReport.Format(ex).Split('\n')[0];

    private static string FirstDifference(string was, string now)
    {
        var a = was.Split('\n');
        var b = now.Split('\n');
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i].Trim() : "（没了）";
            var y = i < b.Length ? b[i].Trim() : "（没了）";
            if (x == y) continue;
            return $"第 {i + 1} 行起：原 {Clip(x)} / 新 {Clip(y)}";
        }
        return "（逐行相同？）";
    }

    private static string Clip(string s) => s.Length <= 60 ? s : s[..60] + "…";

    private static string Comment(string text)
        => string.Join("\n", text.Split('\n').Select(line => "# " + line));

    private static string Canonical(AstNode node)
    {
        var sb = new StringBuilder();
        Render(sb, node, 0, positions: false, meta: false);
        return sb.ToString();
    }

    //  渲染

    private static void Render(StringBuilder sb, object node, int depth, bool positions, bool meta)
    {
        // 印成 a + b 的成员调用,自验按二元式算
        if (node is CallExpr { Function: MemberAccess mem, Argument: var arg }
            && OperatorSymbols.IsSymbol(mem.Member))
        {
            Indent(sb, depth).Append("BinaryExpr").Append('\n');
            Indent(sb, depth + 1).Append("Left:").Append('\n');
            Render(sb, mem.Object, depth + 2, positions, meta);
            Indent(sb, depth + 1).Append("Op: ").Append(Scalar(mem.Member)).Append('\n');
            Indent(sb, depth + 1).Append("Right:").Append('\n');
            Render(sb, arg, depth + 2, positions, meta);
            return;
        }

        Indent(sb, depth).Append(node.GetType().Name);
        if (positions && node is AstNode positioned)
            sb.Append(" @").Append(positioned.Line).Append(':').Append(positioned.Column);
        sb.Append('\n');

        foreach (var prop in Properties(node.GetType()))
        {
            // 那份要的是"一个字段都不少"
            if (!meta && prop.Name is "Source" or "Sugar" or "Pattern" or "BareUnitArg"
                or "Parenthesized") continue;   // 只影响链式摊平
            var value = prop.GetValue(node);
            if (value is null) continue;

            Indent(sb, depth + 1).Append(prop.Name).Append(':');
            if (IsNode(value))
            {
                sb.Append('\n');
                Render(sb, value, depth + 2, positions, meta);
            }
            else if (value is IEnumerable<object> items)
            {
                sb.Append('\n');
                foreach (var item in items)
                    if (IsNode(item)) Render(sb, item, depth + 2, positions, meta);
                    else Indent(sb, depth + 2).Append(Scalar(item)).Append('\n');
            }
            else
            {
                sb.Append(' ').Append(Scalar(value)).Append('\n');
            }
        }
    }

    private static bool IsNode(object value) => value is AstNode or Parameter or DictEntry;

    private static StringBuilder Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

    private static string Scalar(object value) => value switch
    {
        string s => "\"" + new string(s.SelectMany(Escape).ToArray()) + "\"",
        char c => "'" + Escape(c) + "'",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?",
    };

    private static string Escape(char c) => c switch
    {
        '\\' => "\\\\",
        '"' => "\\\"",
        '\'' => "\\'",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        '\0' => "\\0",
        _ when char.IsControl(c) => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
        _ => c.ToString(),
    };

    private static readonly Dictionary<Type, PropertyInfo[]> Cache = [];

    private static readonly HashSet<string> Derived = ["Suffix", "IsOperator", "Curried", "HasAttrs"];

    private static PropertyInfo[] Properties(Type type)
    {
        if (Cache.TryGetValue(type, out var hit)) return hit;
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not ("Line" or "Column") && !Derived.Contains(p.Name))
            .OrderBy(p => p.MetadataToken)
            .ToArray();
        Cache[type] = props;
        return props;
    }
}
