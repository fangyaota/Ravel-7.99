namespace Ravel.Testing;

using Ravel.Runtime;

/// <summary>golden test:每个 tests/*.rav 是一份「源码 + 期望输出」,由 `# --- expected ---` 分界。
/// 文件级标记:`# expect-error` 期望抛异常(只比对 Error 前缀),`# todo` 表示功能未实现——失败记为 TODO 而非 FAIL,
/// `# warn` 打开引擎的诊断提醒(见下)。</summary>
internal static class GoldenTestRunner
{
    private const string ExpectedSeparator = "# --- expected ---";

    /// <summary>打开「是不是忘了调用?」并把 **stderr 一起收进输出**:警告写的是 stderr,
    /// 而用例比的是 stdout —— 不收进来就一条也钉不住。
    ///
    /// 只在带这个标记的用例里改道:别的用例照旧往真 stderr 上写(见 tests/208、tests/229 里
    /// 那两条"只在终端上看得见"的输出)。</summary>
    private const string WarnMarker = "# warn";

    /// <summary>要连网的用例:运行器先把**回环服务器**拉起来,并把基址塞进环境变量
    /// (`LoopbackServer.EnvName`),用例里用 `System.Env` 读。
    ///
    /// 为什么不连真网络:那种用例会飘 —— 断网、限流、对面改了内容,都会让一份精确比对的
    /// 用例莫名其妙地红。回环服务器给的是**定死的字节**(见 LoopbackServer 里那几条路由)。</summary>
    private const string NetMarker = "# net";

    /// <summary>`# slow` —— 这条**跑起来费时间**(起服务器、等计时器、跑十万次切换…)。
    /// 迭代的时候 `ravel test --fast` 把它们跳过:改一行等三分钟和等十秒是两种体验。
    ///
    /// **别拿它当"这条不重要"**:全量那一趟照跑不误,它只是给了个"我这会儿只想跑快的"的开关。</summary>
    private const string SlowMarker = "# slow";

    /// <summary>回环服务器:第一次有人要才起,整个跑完关掉</summary>
    private static LoopbackServer? _server;

    /// <summary>跑 tests/ 下的 *.rav(**含子目录**),打印逐条结果与汇总。有 FAIL 时返回 false
    /// (调用方据此设退出码)。
    ///
    /// `pick` 是**挑选词**:给几个就只跑**相对路径里带其中任意一个**的(不区分大小写)。
    /// 子目录名也算在里面 —— 所以用例按文件夹分好之后,`ravel test http` 挑的就是那一摞。
    /// 不给就是全量。`fast` 跳过带 `# slow` 的那些。
    ///
    /// **没给 `fast` 就先提醒一句**(这一趟会连 `# slow` 一起跑,可能要几分钟)——
    /// 只提醒、不拦:全量本来就该全都跑,`--fast` 才是"我这会儿只想跑快的"。
    ///
    /// `warn` 是一次性的总开关(CLI 的 `--warn`):每条用例都带着它跑,
    /// 好把整个用例库当成一份样本,过一遍"是不是忘了调用"的筛子。</summary>
    public static bool RunAll(bool warn = false, string[]? pick = null, bool fast = false, bool moreControlFlow = false)
    {
        var testDir = FindTestDir();
        if (testDir == null)
        {
            Console.WriteLine("No tests/ directory found.");
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

        // 没加 `--fast`:这一趟会**连那几条 `# slow` 一起跑**(起服务器、等计时器、
        // 跑十万次切换…),可能要几分钟。跑之前先说一声 —— 免得对着不动的屏幕等,
        // 或者忘了这一档有多贵。
        //
        // 有意**只提醒、不拦**:全量那一趟本来就该"全都跑"(`# slow` 不代表不重要,
        // 见 `SlowMarker` 那段);`--fast` 才是"我这会儿只想跑快的"那个开关。
        if (!fast)
            Console.WriteLine("没加 `--fast`：这一趟会连 `# slow` 那几条一起跑，可能会非常慢。");

        int passed = 0, failed = 0, todo = 0, skipped = 0;
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
        var tail = skipped > 0 ? $"(跳过 {skipped} 条 # slow)" : "";
        Console.WriteLine($"\n  {passed} passed, {failed} failed, {todo} todo {tail}");
        return failed == 0;
    }

    /// <summary>这个文件的**相对路径**里含不含挑选词(含任意一个就算)。子目录名一并算进去
    /// —— 用例按文件夹分好之后,`ravel test http` 挑的就是那一摞。</summary>
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

    /// <summary>执行源码,捕获 stdout;异常按 CLI 的约定渲染成 "Error: ..."(运行时错误带位置和调用栈)。
    /// `captureErr` 时把 **stderr 并到同一个缓冲里** —— 同一个 StringWriter,警告就按**真实先后**
    /// 插在正常输出中间(`WriteErr` 那些也一并收进来),而不是被挪到末尾。</summary>
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

/// <summary>一个 golden test 文件解析后的几要素(标记 → 各自那一份,见 <see cref="GoldenTestRunner"/> 顶上的说明)</summary>
internal sealed record GoldenTest(string Source, string Expected, bool ExpectError, bool IsTodo, bool Warn, bool Net, bool Slow);
