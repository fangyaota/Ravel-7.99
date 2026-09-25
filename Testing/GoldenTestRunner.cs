namespace Ravel.Testing;

using Ravel.Runtime;

/// <summary>golden test:每个 tests/*.rav 是一份「源码 + 期望输出」,由 `# --- expected ---` 分界。
/// 两个文件级标记:`# expect-error` 期望抛异常(只比对 Error 前缀),`# todo` 表示功能未实现——失败记为 TODO 而非 FAIL。</summary>
internal static class GoldenTestRunner
{
    private const string ExpectedSeparator = "# --- expected ---";

    /// <summary>跑 tests/ 下全部 *.rav,打印逐条结果与汇总。有 FAIL 时返回 false(调用方据此设退出码)</summary>
    public static bool RunAll()
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

            var output = CaptureOutput(test.Source, file);
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

    /// <summary>执行源码,捕获 stdout;异常按 CLI 的约定渲染成 "Error: ..."(运行时错误带位置和调用栈)</summary>
    private static string CaptureOutput(string source, string? file = null)
    {
        var oldOut = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            new Interpreter().Interpret(Parser.ParseSource(source, file));
        }
        catch (RuntimeException ex)
        {
            return "Error: " + ErrorReport.Format(ex);
        }
        catch (SyntaxException ex)
        {
            // 语法错误也是「用户代码的问题」,和运行时错误一样算正常的 Error 输出
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
        }

        return sw.ToString().Replace("\r\n", "\n").TrimEnd();
    }

    private static GoldenTest Parse(string content)
    {
        bool expectError = false, isTodo = false, inExpected = false;
        var sourceLines = new List<string>();
        var expectedLines = new List<string>();

        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("# expect-error")) { expectError = true; continue; }
            if (trimmed.StartsWith("# todo")) { isTodo = true; continue; }
            if (trimmed == ExpectedSeparator) { inExpected = true; continue; }

            // 期望区里 `# ` 是注释前缀,要剥掉;源区保持原样
            if (inExpected) expectedLines.Add(trimmed.StartsWith("# ") ? trimmed[2..] : line);
            else sourceLines.Add(line);
        }

        return new GoldenTest(string.Join("\n", sourceLines), string.Join("\n", expectedLines), expectError, isTodo);
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

/// <summary>一个 golden test 文件解析后的四要素</summary>
internal sealed record GoldenTest(string Source, string Expected, bool ExpectError, bool IsTodo);
