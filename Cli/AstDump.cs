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

        // 每个文件带上它**相对当前目录**的路径 —— `--out` 那趟按这个还原目录结构。
        //
        // **不能按"输入的那个根"还原**:`ravel ast --out D lib examples` 里 `lib/html.rav`
        // 和 `examples/html.rav` 各自相对自己的根都是 `html.rav`,落到 `D/html.*` 上就撞了
        // (实测撞过五个)。按当前目录走,两条路径天然分得开。
        // 传进来的东西在当前目录之外(`../x.rav`)才退回光名字 —— 那种情况没法安放那个 `..`。
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

        // **一定要排序**:目录枚举的次序是文件系统说了算的,而这一份的用处是"两次跑逐字节比"。
        // 不排的话 diff 里会冒出一堆纯挪位的噪音,把真正的差别淹掉。
        files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        (string Path, string Rel) Rel(string f)
        {
            var rel = Path.GetRelativePath(cwd, Path.GetFullPath(f));
            return (f, rel.StartsWith("..") ? Path.GetFileName(f) : rel);
        }

        // **输出钉死 UTF-8。** `Console` 默认按控制台的 OEM 码页写(中文 Windows 上是 CP936),
        // 而树里带着源文件的**字符串字面量** —— 撞上码页外的字符会被换成 `?`,于是
        // **两个不一样的树可能印成一模一样**:这一份的全部用处就是"逐字节可比",
        // 那一下正好把它废了。(`────` 那种框线字符同理,CP936 里也没有。)
        //
        // 两头都得管:**控制台码页**拨过去,免得直接 `ravel ast f.rav` 看在眼里是花屏;
        // **真正写的那个 writer** 另开一个钉死 UTF-8,重定向到文件时无论如何都是 UTF-8。
        // (这个 setter 在没有控制台的环境里会抛 —— 那种情况下 writer 那份照样对,别因此不跑。)
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (IOException) { /* 没有控制台,不管它 */ }
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));

        var sb = new StringBuilder();
        var failed = 0;    // 解析不了(语料里本来就有 `# expect-error` 那种故意写坏的)
        var broken = 0;    // 印出来再解析,树对不上
        foreach (var (file, rel) in files)
        {
            // 路径统一成 `/` 分隔:同一个语料在 Windows 和别处跑出来的标题行得一样
            var title = "──── " + file.Replace('\\', '/') + " ────";
            string? dump = null, text = null, error = null;

            try
            {
                // 读整个文件。golden 用例的期望区**本来就全是 `#` 注释**(见
                // `GoldenTestRunner` 抬头那条"写用例时请一律加上 `# `"),所以到这儿
                // 什么都不用切 —— 从前在这儿切过一刀,那是**遮羞布**:文件没注释的话
                // 本该报错(那份用例也就能直接跑了),一切反而静默出一个截断的树。
                var src = File.ReadAllText(file);
                // 和**真正跑它**的时候同一套入口:文件头那条 `#program --more-control-flow`
                // 也得认,不然 `return` / `break` 那些语句在转储里报语法错、在跑的时候却没事。
                var flow = Parser.DeclaresMoreControlFlow(src);
                var program = Parser.ParseSource(src, file, flow);

                // `--out` 是"两份都要";不然看开关给哪一份
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
                    // 标题写成注释 —— 这一趟交出去的得**还是能解析的 .rav**
                    text = Comment(title) + "\n" + printed + "\n";
                    // **自验:印出来的东西再解析一遍,得回到同一棵树。** 不比就交出去是
                    // 赌自己没印错,而这个印子的活儿细得很(括号、`=` 还是 `:=`、`->` 还是 `:`)
                    // —— `CommentStripper` 剥完注释也要再比一遍 token,同一个规矩。
                    // 位置和"来路"不比,见 `Canonical`。
                    try
                    {
                        var again = Parser.ParseSource(printed, file, flow);
                        var was = Canonical(program);
                        var now = Canonical(again);
                        if (was != now)
                        {
                            // **说清差在哪一行** —— 只说"对不上"等于让人从头 diff
                            Console.Error.WriteLine($"{file}: 印出来再解析,树对不上 —— {FirstDifference(was, now)}");
                            broken++;
                        }
                    }
                    // **印出来的东西压根解析不了 —— 那是印子的 bug,不是这个文件的问题。**
                    // 这一格从前被外面那个 catch 兜走了,于是"印不出来"被报成"原件解析不了",
                    // 而 `broken` 那一栏永远是 0 —— 自验看着全绿,其实一直在骗人。
                    catch (Exception ex) when (ex is SyntaxException or RuntimeException)
                    {
                        Console.Error.WriteLine($"{file}: 印出来解析不了 —— {FirstLine(ex)}");
                        broken++;
                    }
                }
            }
            // 语料里本来就有 `# expect-error` 那种**故意写坏**的文件 —— 一个文件坏了不该把
            // 整趟搅了。照报(报在它自己那一格,位置一样是可比对的),最后拿退出码说话。
            catch (Exception ex) when (ex is SyntaxException or RuntimeException)
            {
                error = "!! " + ErrorReport.Format(ex);
                failed++;
            }

            if (outDir is null)
            {
                // 走 stdout,和从前一样:有错就交那一段,没错就是产出的那一份
                if (error is not null)
                    stdout.Write((source ? Comment(title + "\n" + error) : title + "\n" + error) + "\n");
                else
                    stdout.Write(dump ?? text);
                continue;
            }

            // `--out`:一个输入文件**落两个** —— `<名>.ast.txt`(结构转储)与
            // `<名>.src.rav`(还原的源码)。子目录按输入根还原(见上面那把 `Rel`)。
            var stem = Path.Combine(outDir, Path.ChangeExtension(rel, null)!);
            WriteFile(stem + ".ast.txt", error is not null ? title + "\n" + error + "\n" : dump!);
            // 解析都过不了的,不落 `.src.rav` —— 那本来就是一句"这不是能跑的东西",
            // 印成一个空壳反而像是"印出来了"
            if (text is not null) WriteFile(stem + ".src.rav", text);
        }

        // **汇总走 stderr,不进 stdout。** stdout 那一份是**产物本身** —— 而且 `--source`
        // 印出来的要能直接 `> x.rav` 拿去跑:汇总混进去,那文件末尾就多一行不是 Ravel 的字,
        // 解析器当场报「未预期的字符」。转储那一路同一个道理(它也是给人 `> x.txt` 比的)。
        var tail = failed > 0 ? $",其中 {failed} 个报错" : "";
        if (broken > 0) tail += $",{broken} 个往返后树对不上";
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

    // ========================================
    //  渲染
    // ========================================

    private static void Render(StringBuilder sb, object node, int depth, bool positions, bool meta)
    {
        Indent(sb, depth).Append(node.GetType().Name);
        // 位置只有真节点才有 —— `Parameter` / `DictEntry` 没继承 `AstNode`
        if (positions && node is AstNode positioned)
            sb.Append(" @").Append(positioned.Line).Append(':').Append(positioned.Column);
        sb.Append('\n');

        foreach (var prop in Properties(node.GetType()))
        {
            // "来路"那两个只在**自验**那一趟排掉(见 `Canonical`);转储是照印的 ——
            // 那份要的是"一个字段都不少"
            if (!meta && prop.Name is "Source" or "Sugar" or "Pattern" or "BareUnitArg") continue;
            var value = prop.GetValue(node);
            if (value is null) continue;

            Indent(sb, depth + 1).Append(prop.Name).Append(':');
            if (IsNode(value))
            {
                sb.Append('\n');
                Render(sb, value, depth + 2, positions, meta);
            }
            // 列表(语句 / 元素 / 参数 / 模式的那几格)。`IEnumerable<T>` 是协变的,
            // 所以 `List<Statement>` 也落进这一格。**不带下标** —— 带上"第几项"之后,
            // 中间插一项会让后面每一项的下标都变, diff 里全是噪音。
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
        // 其余控制字符也**必须**转 —— 直接写出去就是不可见字节, diff 里连"这儿有东西"都看不出来
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
