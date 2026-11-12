using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Spectre.Console.Cli;

namespace Ravel.Cli;

using Ravel.Runtime;
using Ravel.Testing;

internal static class Program
{
    private static string[] _scriptArgs = [];

    private static readonly string[] HeadSwitches =
        ["-h", "--help", "-v", "--version", "-w", "--warn", "--more-control-flow"];

    private static int Main(string[] args)
    {
        if (CheckHead(args) != 0) return 1;

        var (head, tail) = Split(args);
        _scriptArgs = tail;

        var app = new CommandApp<RunCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("ravel");
            // 版本只写在 csproj 一处(`<Version>7.999</Version>`)。**不能用
            // `UseAssemblyInformationalVersion ()`** —— 它交回的是带 SourceLink 那截
            // commit 尾巴的 `7.999+e16bc9e…`,打出来没法看;`+` 后面那截切掉。
            config.SetApplicationVersion(
                typeof(Program).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion?.Split('+')[0] ?? "?");
            config.SetApplicationCulture(new CultureInfo("zh-CN"));   // 帮助文案钉死中文(默认跟系统语言)
            config.Settings.StrictParsing = true;         // 不认识的开关要报错,别静默吞掉
            config.AddCommand<TestCommand>("test").WithDescription("跑 golden 用例(必须挑词,全量已取消)")
                .WithExample("test", "lang")
                .WithExample("test", "367")
                .WithExample("test", "net", "--fast");
            config.AddCommand<StripCommand>("strip").WithDescription("剥掉 .rav 里的注释(就地)")
                .WithExample("strip", "out/lib");
            config.AddCommand<AstCommand>("ast").WithDescription("把 AST 转储成文本(结构,给 diff 用)")
                .WithExample("ast", "lib tests examples")
                .WithExample("ast", "--source lib/predefined.rav")
                .WithExample("ast", "--out out/ast lib tests examples");
        });
        return app.Run(head);
    }

    private static int CheckHead(string[] args)
    {
        for (var i = 0; i < args.Length && args[i].StartsWith('-'); i++)
        {
            if (Array.IndexOf(HeadSwitches, args[i]) >= 0) continue;
            Console.WriteLine($"不认识的开关 '{args[i]}'。");
            Console.WriteLine("用法: ravel [--warn] [--more-control-flow] [脚本.rav [参数…]]");
            Console.WriteLine("      ravel test <词…> [--fast]");
            Console.WriteLine("      ravel strip <文件或目录…>");
            Console.WriteLine("      ravel ast <文件或目录…>");
            Console.WriteLine("      ravel --help");
            return 1;
        }
        return 0;
    }

    private static (string[] Head, string[] Tail) Split(string[] args)
    {
        var i = 0;
        while (i < args.Length && args[i].StartsWith('-')) i++;

        if (i >= args.Length) return (args, []);                   // 全是开关:没有脚本
        if (args[i] is "test" or "strip" or "ast")
        {
            var leading = args[..i];
            return ([args[i], .. leading, .. args[(i + 1)..]], []);
        }
        return (args[..(i + 1)], args[(i + 1)..]);                 // 前半含脚本名本身
    }

    internal static int RunRepl(CliSettings s)
        => RunSource("using \"repl.rav\"\nRepl.Run ()\n", "<repl>", [], s);

    internal static int RunFile(string path, CliSettings s)
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
            return 1;
        }
        var code = RunSource(source, path, _scriptArgs, s);
        Console.WriteLine();
        return code;
    }

    internal static int RunSource(string source, string path, string[] scriptArgs, CliSettings s)
    {
        try
        {
            // 开关在**建完之后**才拨:构造时就把 predefined 跑了,而那是库、不是用户代码 ——
            // 提醒要说的是"你这几行",不该被库里的写法刷屏(`lib/` 里自己开另说)。
            // 文件头那条 `#program --more-control-flow=true` 和命令行开关是**或**的关系
            var flow = s.MoreControlFlow || Parser.DeclaresMoreControlFlow(source);
            new Interpreter(scriptArgs) { WarnForgotCall = s.Warn, MoreControlFlow = flow }
                .Interpret(Parser.ParseSource(source, path, flow));
            return 0;
        }
        // 运行时错误和语法错误渲染同一份报告(位置 + 源码行 + 插入符 + 调用栈),
        // 所以用一条 when 收下来,不必写两遍一模一样的 catch
        catch (Exception ex) when (ex is RuntimeException or SyntaxException)
        {
            Console.WriteLine($"Error: {ErrorReport.Format(ex)}");
            return 1;
        }
        catch (ExitException ex)
        {
            // `exit ""` 是"什么都不说就结束",别打出一个空的 `Error:` 行
            // (退出码也跟着这条线走:说了话就是出事,于是 `Test.Report ()` 那种
            //  "跑完打一行汇总再抛出去"的脚本能让 CI 真的红掉)
            if (ex.Message.Length == 0) return 0;
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            // 走到这里说明解释器自己有 bug:该转成 RuntimeException/SyntaxException 的没转。
            // **内层异常一并打出来**:TypeInitializationException 这类外面那层什么都不说
            // (「静态构造函数抛了」),真正的缘由(少了哪个本机库、哪个文件)全在内层 ——
            // 只看外层那句,等于什么都没说。
            Console.WriteLine($"!! 解释器内部错误 {ex.GetType().Name}: {ex.Message}{Inner(ex)}");
            // 上面那句故意不带栈,免得刷屏;但解释器自己的 bug 只能靠栈才查得下去
            // (递归到栈溢出这类尤其如此,消息里什么线索都没有)。要的时候开这个开关。
            if (Environment.GetEnvironmentVariable("RAVEL_TRACE") == "1") Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static string Inner(Exception ex)
    {
        var text = "";
        for (var x = ex.InnerException; x is not null; x = x.InnerException)
            text += $"\n   :: {x.GetType().Name}: {x.Message}";
        return text;
    }
}

internal abstract class CliSettings : CommandSettings
{
    [CommandOption("-w|--warn")]
    [Description("开「是不是忘了调用?」的提醒(警告走 stderr)")]
    public bool Warn { get; init; }

    [CommandOption("--more-control-flow")]
    [Description("放行 return / break / continue,默认关")]
    public bool MoreControlFlow { get; init; }
}

internal sealed class RunSettings : CliSettings
{
    [CommandArgument(0, "[脚本]")]
    [Description("要跑的 .rav。它之后的参数原样交给脚本,见 System.Args ()")]
    public string? Script { get; init; }
}

internal sealed class TestSettings : CliSettings
{
    [CommandArgument(0, "[词…]")]
    [Description("只跑相对路径里带它的(可给几个);不给就报错,全量已经取消")]
    public string[] Picks { get; init; } = [];

    [CommandOption("-f|--fast")]
    [Description("跳过带 # slow 的用例")]
    public bool Fast { get; init; }
}

internal sealed class StripSettings : CliSettings
{
    [CommandArgument(0, "<文件或目录…>")]
    [Description("目录会递归收 *.rav")]
    public string[] Paths { get; init; } = [];
}

internal sealed class RunCommand : Command<RunSettings>
{
    public override int Execute(CommandContext context, RunSettings settings, CancellationToken cancellation)
        => string.IsNullOrEmpty(settings.Script)
            ? Program.RunRepl(settings)
            : Program.RunFile(settings.Script, settings);
}

internal sealed class TestCommand : Command<TestSettings>
{
    public override int Execute(CommandContext context, TestSettings settings, CancellationToken cancellation)
        => GoldenTestRunner.RunAll(settings.Warn, settings.Picks, settings.Fast, settings.MoreControlFlow) ? 0 : 1;
}

internal sealed class StripCommand : Command<StripSettings>
{
    public override int Execute(CommandContext context, StripSettings settings, CancellationToken cancellation)
        => Strip.Run(settings.Paths) ? 0 : 1;
}

internal sealed class AstSettings : CliSettings
{
    [CommandArgument(0, "<文件或目录…>")]
    [Description("目录会递归收 *.rav")]
    public string[] Paths { get; init; } = [];

    [CommandOption("-s|--source")]
    [Description("印成 .rav 源码(而不是结构转储);出来的是 Lowering 之后的树")]
    public bool Source { get; init; }

    [CommandOption("-o|--out")]
    [Description("写成文件而不是打到 stdout;每个 .rav 落两份(结构转储 + 还原的源码)")]
    public string? Out { get; init; }
}

internal sealed class AstCommand : Command<AstSettings>
{
    public override int Execute(CommandContext context, AstSettings settings, CancellationToken cancellation)
        => AstDump.Run(settings.Paths, settings.Source, settings.Out) ? 0 : 1;
}
