namespace Ravel.Cli;

using System.Globalization;
using System.Reflection;
using System.Text;
using Ravel.Runtime;
using Ravel.Testing;

/// <summary>把 AST **结构转储**成文本 —— 一个节点一行、缩进表层级、深度优先。
///
/// **它和 `Runtime/AstPrinter.cs` 是两件事,别混**:
///
/// * `AstPrinter` 是印给**人**看的(一个函数在 `print` 和报错里长什么样)。它只求读得懂:
///   超 72 字符截断、认不得的节点退化成 `"..."`、按保守规则加括号。
/// * 这一份是印给 **diff** 看的。要的是"一个节点都不漏、一个字段都不少、两次跑逐字节一样"。
///   所以它走**反射** —— 将来 AST 加了新节点种类,不用回来补分支,自然就跟着出现。
///   (`AstPrinter` 那个 `_ => "..."` 在验证用途里是**致命**的:改掉的正好是那一块的话,
///   新旧两版会印出一模一样的省略号,diff 一片空白,而你以为验过了。)
///
/// **印出来的是 `Lowering` 之后的树** —— `do` / `??` / `~` / 模式 / `|>` 都在
/// `Parser.Parse` 出树之前就折完了(见 `Syntax/Lowering.cs` 抬头)。所以
/// `DoExpr` / `BindStatement` / `Destructure` 这三种在这一份里**见不着**:看到的是它们
/// 折成的 `Bind` 调用和普通 `:=`。要"用户写下的那个形状"得另开一条路(现在没有)。
///
/// 正经用法 —— **动解析器前后各跑一遍,逐字节 diff**:
///
///     ravel ast lib tests examples > 旧.txt     # 改之前(拿旧那份 dll)
///     ravel ast lib tests examples > 新.txt     # 改之后
///     diff 旧.txt 新.txt                        # 空 = 树没变
///
/// 用例全绿**不等于** AST 没变 —— 绿只说明这套语料没踩到那个差别。</summary>
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
                // golden 用例文件后面还跟着 `# --- expected ---` 和一段**裸的期望输出**
                // (不是注释) —— 那是用例格式,不是源码。切掉它,切法见 `SourcePart`。
                var src = GoldenTestRunner.SourcePart(File.ReadAllText(file));
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
                    var printed = AstPrinter.Program(program);
                    // **自验:印出来的东西再解析一遍,得回到同一棵树。** 不比就交出去是
                    // 赌自己没印错,而这个印子的活儿细得很(括号、`=` 还是 `:=`、`->` 还是 `:`)
                    // —— `CommentStripper` 剥完注释也要再比一遍 token,同一个规矩。
                    // 位置不比:行重排过了。
                    var again = Parser.ParseSource(printed, file, flow);
                    if (Canonical(again) != Canonical(program))
                    {
                        Console.Error.WriteLine($"{file}: 印出来再解析,树对不上");
                        broken++;
                    }
                    // 标题写成注释 —— 这一趟交出去的得**还是能解析的 .rav**
                    text = Comment(title) + "\n" + printed + "\n";
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

    /// <summary>写一份产物:目录没有就建,文件钉死 UTF-8(不带 BOM),写过的名字报到 stderr
    /// ——stdout 那一格是**产物本身**,这张清单不是产物。</summary>
    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        Console.Error.WriteLine("  " + path.Replace('\\', '/'));
    }

    /// <summary>整段按注释写出去 —— `--source` 出来的文件得**还是能解析的 `.rav`**,
    /// 所以标题行、报错都得是 `#` 注释,不然拿它去 `ravel` 会当场报语法错。</summary>
    private static string Comment(string text)
        => string.Join("\n", text.Split('\n').Select(line => "# " + line));

    /// <summary>只比较**树本身** —— 位置和"来路"都不进去。
    ///
    /// * **位置**(`@行:列`):印出来的源码重排过,行列当然全变。
    /// * **来路**(`Source` / `Sugar`):源码文本里**根本表达不出来**。
    ///   `Source` 是"这个块来自哪个文件"(`Lowering` 自己造的那些块没有);
    ///   `Sugar` 是"这枚 lambda 是语法糖生成的、不是用户写的"—— 印成源码之后,它当然
    ///   是一枚**写出来的** lambda 了。两个都不影响求值那一侧(`Sugar` 只管解析期
    ///   收 `_` 洞时的闭包边界),重解析的树自己会把它算对。
    ///
    /// 留着它们的唯一后果是**每个文件都"对不上"**,那自验就等于没有。</summary>
    private static string Canonical(AstNode node)
    {
        var sb = new StringBuilder();
        Render(sb, node, 0, positions: false, meta: false);
        return sb.ToString();
    }

    // ========================================
    //  渲染
    // ========================================

    /// <summary>印一个节点:自己那一行写类型名 + 位置,然后把每个**有值**的属性缩进一层摊开。
    ///
    /// 位置(`@3:14`)印在节点**自己那一行**上,不当成两个字段摊开 —— 它本来就是"哪个节点"
    /// 的坐标,摊开能把转储撑大一倍。**而它照旧参与比对**:解析器挪了位置,这儿就会红。
    ///
    /// 值为 `null` 的属性**不印**(没那一行 = 是 null)—— 省地方,而且"从无到有"在 diff 里
    /// 表现为**多一行**,看得出是哪儿变的。</summary>
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
            if (!meta && prop.Name is "Source" or "Sugar") continue;
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

    /// <summary>该**摊开**的东西:绝大多数是 <see cref="AstNode"/>,另有两个**没继承它**的
    /// 辅助 record —— <see cref="Parameter"/> 和 <see cref="DictEntry"/>。漏掉它们就会掉进
    /// `Scalar` 那一格,印成 C# 的 `record ToString()`,里面的子表达式一个都没摊开
    /// (字典字面量整条就长这样:`DictEntry { Key = …, Value = … }`)。
    ///
    /// 属性那一格和列表那一格**都得走这个判断** —— `DictLiteral.Entries` 是个
    /// `List<DictEntry>`,只在属性那边认一次的话,列表里的照样漏。</summary>
    private static bool IsNode(object value) => value is AstNode or Parameter or DictEntry;

    private static StringBuilder Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

    /// <summary>叶子值:字符串带引号转义、字符带单引号,别的照 `ToString`(`bool` 走小写 ——
    /// 那是 Ravel 里的写法,不是 C# 的 `True`)。</summary>
    private static string Scalar(object value) => value switch
    {
        string s => "\"" + new string(s.SelectMany(Escape).ToArray()) + "\"",
        char c => "'" + Escape(c) + "'",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?",
    };

    /// <summary>转义一个字符。**必须转** —— 字符串字面量里带换行、带引号是常事
    /// (原始字符串尤其),不转的话一个值能横跨好几行, diff 就按行对不上了。</summary>
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

    /// <summary>每个类型问一遍反射就够 —— 语料几千个节点,不然每印一行查一次属性表。</summary>
    private static readonly Dictionary<Type, PropertyInfo[]> Cache = [];

    /// <summary>**算出来的**属性,不印 —— 它们是树的函数,不是树上的东西:印出来只是把
    /// 同一个事实说两遍,还把 diff 撑大一倍(`Suffix` 更是会把没后缀的印成一个裸的不可见字符)。
    ///
    /// **照名字排除是安全的**:名字改了它就不再被排掉,于是**多印出来**(看得见),
    /// 而不是"少印一件"(那样才危险 —— 那就是 `AstPrinter` 的 `"..."` 那个病)。
    /// 现查一句:`Syntax/Ast.cs` 里那几条表达式体属性
    /// (`grep "public .* => " Syntax/Ast.cs`)。</summary>
    private static readonly HashSet<string> Derived = ["Suffix", "IsOperator", "Curried", "HasAttrs"];

    /// <summary>要印的属性,次序 = **声明次序**。
    ///
    /// 反射交回的那一次实际也是声明序,但那是"实现上如此"、不是保证;这一份的用处是
    /// **逐字节可比**,所以显式按元数据 token 排一道,把次序钉死。
    ///
    /// 位置单独印在节点那一行(见 `Render`),不在字段里摊开。
    /// `record` 自己那个 `EqualityContract` 是 **protected**, `BindingFlags.Public`
    /// 本来就够不着;`NumberLiteral.Parsed` / `StringLiteral.Packed` 是字段、
    /// `Digits` 是 internal —— 也够不着,正合意:那些是算出来的缓存。</summary>
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
