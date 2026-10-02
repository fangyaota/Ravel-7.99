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

    /// <summary>回环服务器:第一次有人要才起,整个跑完关掉</summary>
    private static LoopbackServer? _server;

    /// <summary>跑 tests/ 下全部 *.rav,打印逐条结果与汇总。有 FAIL 时返回 false(调用方据此设退出码)。
    /// `warn` 是一次性的总开关(CLI 的 `--warn`):每条用例都带着它跑,
    /// 好把整个用例库当成一份样本,过一遍"是不是忘了调用"的筛子。</summary>
    public static bool RunAll(bool warn = false)
    {
        var testDir = FindTestDir();
        if (testDir == null)
        {
            Console.WriteLine("No tests/ directory found.");
            return false;
        }

        int passed = 0, failed = 0, todo = 0;
        foreach (var file in Directory.GetFiles(testDir, "*.rav").OrderBy(f => f))
        {
            var test = Parse(File.ReadAllText(file));
            Console.Write($"{Path.GetFileName(file),-35} ");

            // `# net` 的用例要真发请求:先把回环服务器拉起来(它自己把基址写进环境变量)。
            // 起不来就让它起不来 —— 用例会以"连不上"明显报错,不静默跳过
            if (test.Net)
            {
                try { _server ??= new LoopbackServer(); }
                catch (Exception ex) { Console.WriteLine($"(回环服务器起不来: {ex.Message})"); }
            }

            // `# warn` 的用例把 stderr 收进比对里;**`--warn` 那一趟只开开关、不动比对** ——
            // 它是"整库过筛子",警告照旧往真 stderr 上冒,不因此让谁红掉
            var output = CaptureOutput(test.Source, file, captureErr: test.Warn, warn: warn || test.Warn);
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

        Console.WriteLine($"\n  {passed} passed, {failed} failed, {todo} todo");
        return failed == 0;
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
    private static string CaptureOutput(string source, string? file, bool captureErr, bool warn)
    {
        var oldOut = Console.Out;
        var oldErr = Console.Error;
        var sw = new StringWriter();
        Console.SetOut(sw);
        if (captureErr) Console.SetError(sw);
        try
        {
            // 开关在构造**之后**拨:构造时就跑完 predefined 了,那是库、不是这次要盯的代码
            new Interpreter { WarnForgotCall = warn }.Interpret(Parser.ParseSource(source, file));
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
        bool expectError = false, isTodo = false, warn = false, net = false, inExpected = false;
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

            sourceLines.Add(line);
        }

        return new GoldenTest(string.Join("\n", sourceLines), string.Join("\n", expectedLines), expectError, isTodo, warn, net);
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
internal sealed record GoldenTest(string Source, string Expected, bool ExpectError, bool IsTodo, bool Warn, bool Net);
