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
                    // 印出来的是**脱糖后**的代码:文件头声明别再脱一遍,否则 `__df…` 会被
                    // 当用户代码再处理一遍(这文件得能原样跑,它现在是"引擎眼里的样子")。
                    text = Comment(title) + "\n" + NoLowerHeader + "\n" + printed + "\n";
                    try
                    {
                        if (HasEngineMadeLiteral(program))
                        {
                            skipped++;
                        }
                        else
                        {
                            var back = Parser.ParseSource(NoLowerHeader + "\n" + printed, file, flow);
                            var was = Canonical(program);
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

    private const string NoLowerHeader = "#program --no-lower=true";

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
        // 合成的 `()`(空块留下的那句)印出来是 VoidLiteral,回读也是它 —— 自验按同一个节点算
        if (node is LiteralExpr { Value: VoidVal } synthesized)
        {
            Render(sb, new VoidLiteral { Line = synthesized.Line, Column = synthesized.Column },
                depth, positions, meta);
            return;
        }

        // 洞的名字是解析时现编的(`_` / `_0` / `_3`):自验按同一个名字算
        if (node is IdentifierExpr { Name: var holeRef } && holeRef.StartsWith("_", StringComparison.Ordinal))
        {
            Indent(sb, depth).Append("IdentifierExpr").Append('\n');
            Indent(sb, depth + 1).Append("Name: ").Append(Scalar("_")).Append('\n');
            return;
        }

        // `f := _ * 2` 是洞的糖:印成 `_ * 2` 读回来又是一层 `(_0) => { _ * 2; }`,
        // 自验按"洞的本体"算,不然每个用洞的地方都报对不上。
        if (node is LambdaExpr { Param: Parameter { Name: var holeName, Pattern: null },
                Body: BlockExpr { Statements: [ExpressionStatement { Expr: var holeBody }] } }
            && holeName.StartsWith("_", StringComparison.Ordinal))
        {
            Render(sb, holeBody, depth, positions, meta);
            return;
        }

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

        // 洞印成 _N,回来是标识符,自验按等价算
        if (node is HoleExpr hole)
        {
            Indent(sb, depth).Append("IdentifierExpr").Append('\n');
            Indent(sb, depth + 1).Append("Name: ").Append(Scalar("_" + hole.Index)).Append('\n');
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
            // `Synthetic` 是脱糖留下的记号(注入的注解检查),印出来就是普通语句
            if (!meta && prop.Name is "Synthetic") continue;
            // `IgnoreType` 同理:它记的是"解析到这里时开关是什么",印出来只剩指令行
            if (!meta && prop.Name is "IgnoreType") continue;
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

    // 预设类体里那几条 LiteralExpr 是 C# 造的回调(没有源码可还原);用户文件里只有 VoidVal
    private static bool HasEngineMadeLiteral(object node)
    {
        if (node is LiteralExpr le) return le.Value is not VoidVal;

        foreach (var prop in Properties(node.GetType()))
        {
            var value = prop.GetValue(node);
            switch (value)
            {
                case AstNode child when HasEngineMadeLiteral(child):
                    return true;
                case System.Collections.IEnumerable list when value is not string:
                    foreach (var item in list)
                        if (item is AstNode itemNode && HasEngineMadeLiteral(itemNode)) return true;
                    break;
            }
        }

        return false;
    }

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
