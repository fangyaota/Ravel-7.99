namespace Ravel.Testing;

using Ravel.Runtime;

internal static class GoldenTestRunner
{
    private const string ExpectedSeparator = "# --- expected ---";

    private const string WarnMarker = "# warn";

    private const string NetMarker = "# net";

    private const string SlowMarker = "# slow";

    private const int MaxFiles = 20;

    private static LoopbackServer? _server;

    public static bool RunAll(bool warn = false, string[]? pick = null, bool fast = false, bool moreControlFlow = false,
                              bool update = false)
    {
        var testDir = FindTestDir();
        if (testDir == null)
        {
            Console.WriteLine("No tests/ directory found.");
            return false;
        }

        // 全量那一趟**取消了**:几分钟、夹着联网和起服务端的,红了也说不清是哪一摊。
        // 所以**必须挑词**。`--warn`/`--fast` 是开关,不是筛子 —— 只有它们也算没挑。
        if (pick is not { Length: > 0 })
        {
            Console.WriteLine("没挑词 —— 全量那一趟已经取消。给个沾相对路径的词,例如:");
            Console.WriteLine("    ravel test 367           编号里带 367 的那条");
            Console.WriteLine("    ravel test pattern       整个 pattern 那几条");
            Console.WriteLine("    ravel test net --fast    只跑 net/ 里不带 # slow 的");
            return false;
        }

        var chosen = Directory.GetFiles(testDir, "*.rav", SearchOption.AllDirectories)
            .Where(f => Matches(f, testDir, pick))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (chosen.Count == 0)
        {
            // 挑了个什么都不沾的词,别让人对着"0 passed"发愣
            Console.WriteLine($"没有一条用例对得上:{string.Join(" ", pick!)}");
            return false;
        }

        // 一次最多二十条:挑宽了当场拒绝,免得又变成"几分钟说不清哪一摊"
        if (chosen.Count > MaxFiles)
        {
            Console.WriteLine($"挑出 {chosen.Count} 个文件,超过上限 {MaxFiles} —— 把词写窄一点,或者删掉一些测试");
            Console.WriteLine("    ravel test 367           编号里带 367 的那条");
            Console.WriteLine("    ravel test net --fast    只跑 net/ 里不带 # slow 的");
            return false;
        }

        int passed = 0, failed = 0, todo = 0, skipped = 0, updated = 0;
        foreach (var file in chosen)
        {
            var test = Parse(File.ReadAllText(file));
            // 慢的那些**整条跳过**,连名字都不打 —— 打了就是刷屏,数量留给汇总那一行说
            if (fast && test.Slow) { skipped++; continue; }
            Console.Write($"{Path.GetRelativePath(testDir, file).Replace('\\', '/'),-35} ");

            // `# net` 的用例要真发请求:先把回环服务器拉起来(它自己把基址写进环境变量)。
            // 起不来就让它起不来 —— 用例会以"连不上"明显报错,不静默跳过
            if (test.Net)
            {
                try { _server ??= new LoopbackServer(); }
                catch (Exception ex) { Console.WriteLine($"(回环服务器起不来: {ex.Message})"); }
            }

            // `# warn` 的用例把 stderr 收进比对里;**`--warn` 那一趟只开开关、不动比对** ——
            // 它是"整库过筛子",警告照旧往真 stderr 上冒,不因此让谁红掉
            var output = CaptureOutput(test.Source, file, captureErr: test.Warn,
                warn: warn || test.Warn, moreControlFlow: moreControlFlow);
            if (IsPassing(test, output))
            {
                Console.WriteLine(test.ExpectError ? "OK (expected error)" : "OK");
                passed++;
                continue;
            }

            // `--update`:把这一条的期望区按实跑输出重写。只有"对不上"的才动,
            // 已经对上的文件一个字节也不碰;重写之后这条算更新,不算失败。
            if (update && !test.IsTodo)
            {
                UpdateExpectations(file, output);
                Console.WriteLine("UPDATED");
                updated++;
                continue;
            }

            if (test.IsTodo)
            {
                Console.WriteLine("TODO");
                todo++;
            }
            else
            {
                Console.WriteLine("FAIL");
                failed++;
            }

            Report(test, output);
        }

        _server?.Dispose();          // 把回环服务器收掉(顺手清掉那个环境变量)
        _server = null;

        // 跳过的要说一声 —— `--fast` 下的"全绿"不等于全量绿
        var tail = (skipped > 0 ? $"(跳过 {skipped} 条 # slow)" : "")
                   + (updated > 0 ? $"(更新 {updated} 条期望)" : "");
        Console.WriteLine($"\n  {passed} passed, {failed} failed, {todo} todo {tail}");
        return failed == 0;
    }

    // 只重写期望区:分隔行之前的源码一个字节不动。期望区里非空行一律加 `# ` 前缀
    // (那条文件还得能直接跑),空行留空 —— 和 `Parse` 的读法对齐。
    private static void UpdateExpectations(string file, string output)
    {
        var lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n').ToList();
        var sep = lines.FindIndex(l => l.TrimStart() == ExpectedSeparator);
        if (sep < 0) return;

        var kept = lines.Take(sep + 1).ToList();
        var body = output.TrimEnd('\n');
        if (body.Length > 0)
            kept.AddRange(body.Split('\n').Select(l => l.Length == 0 ? "" : "# " + l));
        kept.Add("");
        File.WriteAllText(file, string.Join("\n", kept));
    }

    private static bool Matches(string file, string testDir, string[]? pick)
    {
        if (pick is null || pick.Length == 0) return true;
        var rel = Path.GetRelativePath(testDir, file).Replace('\\', '/');
        return pick.Any(p => rel.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPassing(GoldenTest test, string output)
        => test.ExpectError ? output.StartsWith("Error:") : output.Trim() == test.Expected.Trim();

    private static void Report(GoldenTest test, string output)
    {
        Console.WriteLine($"       expected: {Flatten(test.Expected)}");
        Console.WriteLine($"       got:      {Flatten(output)}");
    }

    private static string Flatten(string s) => s.Trim().Replace("\n", "\\n");

    private static string CaptureOutput(string source, string? file, bool captureErr, bool warn,
                                        bool moreControlFlow)
    {
        var oldOut = Console.Out;
        var oldErr = Console.Error;
        var sw = new StringWriter();
        Console.SetOut(sw);
        if (captureErr) Console.SetError(sw);
        try
        {
            // 开关在构造**之后**拨:构造时就跑完 predefined 了,那是库、不是这次要盯的代码
            var flow = moreControlFlow || Parser.DeclaresMoreControlFlow(source);
        new Interpreter { WarnForgotCall = warn, MoreControlFlow = flow }
            .Interpret(Parser.ParseSource(source, file, flow));
        }
        // 语法错误也是「用户代码的问题」,和运行时错误一样算正常的 Error 输出
        catch (Exception ex) when (ex is RuntimeException or SyntaxException)
        {
            return "Error: " + ErrorReport.Format(ex);
        }
        catch (ExitException ex)
        {
            // exit 用异常解栈,测试里当一个正常终结
            return "Error: " + ex.Message;
        }
        catch (Exception ex)
        {
            // C# 异常漏出来了——解释器里的 bug,不是 Ravel 层的错误。
            // 前缀故意不叫 "Error:",否则 `# expect-error` 的用例会被它蒙混过关
            // (判据只是 output.StartsWith("Error:"))。
            return $"!! C# 异常 {ex.GetType().Name} 漏到顶层(不该出现,应转成 RuntimeException): {ex.Message}";
        }
        finally
        {
            Console.SetOut(oldOut);
            Console.SetError(oldErr);
        }

        return sw.ToString().Replace("\r\n", "\n").TrimEnd();
    }

    private static GoldenTest Parse(string content)
    {
        bool expectError = false, isTodo = false, warn = false, net = false, slow = false, inExpected = false;
        var sourceLines = new List<string>();
        var expectedLines = new List<string>();

        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed == ExpectedSeparator) { inExpected = true; continue; }

            // 期望区里 `# ` 是注释前缀,要剥掉;源区保持原样。
            //
            // **写用例时请一律加上 `# `**(空行除外 —— 空行留空,`#` 后面那个空格会被
            // `TrimEnd` 削掉、反而变成一句 `"#"`)。理由是那份文件还得**能直接跑**:
            // `ravel tests/xxx.rav` 不走这儿,期望区就那么原样喂给解析器 —— 不注释的话
            // 会当成代码执行,一句 `--> file:line` 就够它报「未预期的字符」。
            // (这里两种都收,是为了不把历史用例判死。)
            if (inExpected)
            {
                expectedLines.Add(trimmed.StartsWith("# ") ? trimmed[2..] : line);
                continue;
            }

            // 文件级标记。**要在源码里留一个空行**:标记本身不进源码,但行号不能因此往前挪一格 ——
            // 报错和警告都按行号指回源码(见 ErrorReport),少一行会让插入符指到上一行去。
            if (trimmed.StartsWith("# expect-error")) { expectError = true; sourceLines.Add(""); continue; }
            if (trimmed.StartsWith("# todo")) { isTodo = true; sourceLines.Add(""); continue; }
            if (trimmed == WarnMarker) { warn = true; sourceLines.Add(""); continue; }
            if (trimmed == NetMarker) { net = true; sourceLines.Add(""); continue; }
            if (trimmed == SlowMarker) { slow = true; sourceLines.Add(""); continue; }

            sourceLines.Add(line);
        }

        return new GoldenTest(string.Join("\n", sourceLines), string.Join("\n", expectedLines), expectError, isTodo, warn, net, slow);
    }

    private static string? FindTestDir()
    {
        var bases = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "tests"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tests"),
            "tests",
            "/workspace/ravel/tests"
        };
        return bases.FirstOrDefault(Directory.Exists);
    }
}

internal sealed record GoldenTest(string Source, string Expected, bool ExpectError, bool IsTodo, bool Warn, bool Net, bool Slow);
