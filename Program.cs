using Ravel;
using Ravel.Repl;
using Ravel.Runtime;
using Ravel.Testing;

if (args.Length == 0)
{
    new NeoInteractor(new Interpreter()).Run();
}
else if (args[0] == "test")
{
    if (!GoldenTestRunner.RunAll()) Environment.ExitCode = 1;
}
else
{
    RunFile(args[0]);
}

static void RunFile(string path)
{
    string source;
    try
    {
        source = File.ReadAllText(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        // 不接的话 `ravel nosuchfile.rav` 会甩一坨 .NET 堆栈出来,
        // 那是给开发者看的,不是给写 .rav 的人看的
        Console.WriteLine($"读不到文件 '{path}': {ex.Message}");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine($"── {path} ──");
    Console.WriteLine(source.Trim());
    Console.WriteLine("── Output ──");
    try
    {
        new Interpreter().Interpret(Parser.ParseSource(source, path));
    }
    // 运行时错误和语法错误渲染同一份报告(位置 + 源码行 + 插入符 + 调用栈),
    // 所以用一条 when 收下来,不必写两遍一模一样的 catch
    catch (Exception ex) when (ex is RuntimeException or SyntaxException)
    {
        Console.WriteLine($"Error: {ErrorReport.Format(ex)}");
    }
    catch (ExitException ex)
    {
        // `exit ""` 是"什么都不说就结束",别打出一个空的 `Error:` 行
        if (ex.Message.Length > 0) Console.WriteLine($"Error: {ex.Message}");
    }
    catch (Exception ex)
    {
        // 走到这里说明解释器自己有 bug:该转成 RuntimeException/SyntaxException 的没转
        Console.WriteLine($"!! 解释器内部错误 {ex.GetType().Name}: {ex.Message}");
        // 上面那句故意不带栈,免得刷屏;但解释器自己的 bug 只能靠栈才查得下去
        // (递归到栈溢出这类尤其如此,消息里什么线索都没有)。要的时候开这个开关。
        if (Environment.GetEnvironmentVariable("RAVEL_TRACE") == "1") Console.WriteLine(ex.StackTrace);
    }

    Console.WriteLine();
}
