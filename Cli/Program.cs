using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Spectre.Console.Cli;

namespace Ravel.Cli;

using Ravel.Runtime;
using Ravel.Testing;

/// <summary>命令行入口。**形状照老 Ravel 那份 `Program.cs`**:一个 `CommandApp`,
/// 每个模式一个 `Command` 类 —— 于是 `--help` / `--version` / 用法 / 每个开关的说明
/// 全是声明式的,不用手写一遍(从前是手写的一串 `if` + 每个模式各自一个实参循环)。
///
/// 但有一件事框架**办不到**,所以这儿自己切一刀。Ravel 的规矩是**脚本名之后的一个字不动**
/// 那几个字就是 `System.Args ()`(见 CONTEXT 的「System 模块」)。而 `Spectre.Console.Cli`
/// 在**整行**任意位置认开关,不认识的还静默吞掉 —— `ravel x.rav --warn` 里那个 `--warn`
/// 会被它抢走,脚本就再也看不见(实测:`--foo` 连报错都没有,直接没了)。
///
/// 所以 `Split` 先把命令行在**脚本名**处切成两半:**前半**交给框架去认,后半原样递给脚本。
/// 也试过不动刀:`CommandArgumentAttribute` 没有 `RemainingArguments`,
/// `ConvertFlagsToRemainingArguments` 只是把不认识的开关收进 `Remaining` —— 位置照样是乱的。</summary>
internal static class Program
{
    /// <summary>框架自己 new 命令对象(没有构造注入的口子),而一个进程只跑一条命令,
    /// 所以脚本那几个实参走静态字段。`Split` 切出来的后半段就是它。</summary>
    private static string[] _scriptArgs = [];

    /// <summary>脚本名**之前**认得的开关。只用来守一条:报错一律中文(见 `CheckHead`)——
    /// 真正的解析还是框架干,这儿只是先认一遍名字。`--help` / `--version` 是框架自带的。</summary>
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
            // 版本只写在 csproj 一处(`<Version>7.99</Version>`)。**不能用
            // `UseAssemblyInformationalVersion ()`** —— 它交回的是带 SourceLink 那截
            // commit 尾巴的 `7.99+e16bc9e…`,打出来没法看;`+` 后面那截切掉。
            config.SetApplicationVersion(
                typeof(Program).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion?.Split('+')[0] ?? "?");
            config.SetApplicationCulture(new CultureInfo("zh-CN"));   // 帮助文案钉死中文(默认跟系统语言)
            config.Settings.StrictParsing = true;         // 不认识的开关要报错,别静默吞掉
            config.AddCommand<TestCommand>("test").WithDescription("跑 golden 用例(不给词就全量)");
            config.AddCommand<StripCommand>("strip").WithDescription("剥掉 .rav 里的注释(就地)")
                .WithExample("strip", "out/lib");
            config.AddCommand<AstCommand>("ast").WithDescription("把 AST 转储成文本(结构,给 diff 用)")
                .WithExample("ast", "lib tests examples");
        });
        return app.Run(head);
    }

    /// <summary>脚本名**之前**那几个开关自己先认一遍。
    ///
    /// 框架的解析错误是**英文**的(`Error: Unknown option 'x'.`),而这儿"报错也中文" ——
    /// 所以这一小段不交给它。只管**脚本名之前**:那之后的东西是脚本的,不进这一格。
    ///
    /// 认得的原样放行,不认得的当场说中文、连用法一块儿报。</summary>
    private static int CheckHead(string[] args)
    {
        for (var i = 0; i < args.Length && args[i].StartsWith('-'); i++)
        {
            if (Array.IndexOf(HeadSwitches, args[i]) >= 0) continue;
            Console.WriteLine($"不认识的开关 '{args[i]}'。");
            Console.WriteLine("用法: ravel [--warn] [--more-control-flow] [脚本.rav [参数…]]");
            Console.WriteLine("      ravel test [词…] [--fast]");
            Console.WriteLine("      ravel strip <文件或目录…>");
            Console.WriteLine("      ravel ast <文件或目录…>");
            Console.WriteLine("      ravel --help");
            return 1;
        }
        return 0;
    }

    /// <summary>在**脚本名**处把命令行切成两半:
    ///
    ///     ravel --warn x.rav a --warn   →   前半 [--warn x.rav]   后半 [a --warn]
    ///
    /// 切点 = 第一个**不以 `-` 开头**的词。它要是子命令(`test` / `strip` / `ast`)就**不切** ——
    /// 那几条没有"脚本",整行的实参都是框架的。
    ///
    /// 子命令前面那几个开关得**挪到子命令名后面**:框架不认"子命令名之前的开关",
    /// `ravel --warn test` 会被它当成"跑一个叫 test 的脚本"。挪一下对用户无关紧要。</summary>
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

    /// <summary>不带脚本 = 进 REPL。**这个 REPL 是 Ravel 自己写的**(`lib/repl.rav`,
    /// 入口 `Repl.Run ()`)—— 引擎里那一版(C# 的 `NeoInteractor`)已经删掉:两边行为一模一样,
    /// 而"这门语言能拿自己写自己的 REPL"正是它的门面(照它写一遍的动机、以及为什么能成,
    /// `lib/repl.rav` 头上有)。
    ///
    /// 所以这儿做的事就是**跑两行脚本**,不多不少:不去查模块、不去摸它的内部 —— 那样等于
    /// 把 REPL 的入口又编回 C# 里,下回想改就得动引擎(`lib/repl.rav` 也就不用谈"库"了)。
    ///
    /// 顺带白拿一条:C# 那版没有"看不见终端"这一路,而 `lib/repl.rav` 有(见它的 `Run`)——
    /// 管道里喂进去的整段会被当成一段程序跑完,于是 `echo 'print 1' | ravel` 现在通。</summary>
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

    /// <summary>跑一段源码 —— **单文件和 REPL 入口都走这里**,报错就只写这一套。
    /// 出事交回 1(和测试那条线一个规矩:说了话就是出事)。</summary>
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

    /// <summary>把内层异常一层层接在后面。**判断一条 C# 异常"到底为什么"常常全在内层** ——
    /// `TypeInitializationException`(静态构造函数抛了)就是最典型的一个:它自己那句话
    /// 什么线索都没有,真正的原因(文件不在、本机库加载不了)在下一层。</summary>
    private static string Inner(Exception ex)
    {
        var text = "";
        for (var x = ex.InnerException; x is not null; x = x.InnerException)
            text += $"\n   :: {x.GetType().Name}: {x.Message}";
        return text;
    }
}

/// <summary>三个模式共用的开关。放基类是因为 `Program.Split` 会把子命令名**前面**的开关
/// 挪到后面去,于是每条命令都得认得它们(用不着的那条忽略掉)。
///
/// 说明文字里**别写方括号** —— 帮助是当 Spectre 标记渲染的,`[x]` 会被当成样式。</summary>
internal abstract class CliSettings : CommandSettings
{
    [CommandOption("-w|--warn")]
    [Description("开「是不是忘了调用?」的提醒(警告走 stderr)")]
    public bool Warn { get; init; }

    [CommandOption("--more-control-flow")]
    [Description("放行 return / break / continue,默认关")]
    public bool MoreControlFlow { get; init; }
}

/// <summary>跑脚本;不给脚本就进 REPL。</summary>
internal sealed class RunSettings : CliSettings
{
    [CommandArgument(0, "[脚本]")]
    [Description("要跑的 .rav。它之后的参数原样交给脚本,见 System.Args ()")]
    public string? Script { get; init; }
}

/// <summary>跑 golden 用例。
///
///     词    只跑**相对路径里带它**的(不区分大小写,子目录名也算在内)。
///           给几个就是"含其中任意一个"。不给就是全量。
///             ravel test http          名字/目录里带 http 的那些
///             ravel test 29 30         29x 和 30x
///           **迭代的时候就用它** —— 全量那份要跑几分钟,改一行不值得。
///     --fast 跳过带 `# slow` 的(起服务器、等计时器那几条)。
///
/// `--warn` 传下去:每条用例都带上开关跑一遍。**比对的是 stdout**,警告走 stderr ——
/// 所以这一趟不会让谁红掉,它就是个"整个用例库过一遍筛子"的用法:
///     ravel --warn test 2>&1 | grep 警告
/// (单独一条用例要**钉住**警告长什么样,在它开头写 `# warn`,见 GoldenTestRunner)</summary>
internal sealed class TestSettings : CliSettings
{
    [CommandArgument(0, "[词…]")]
    [Description("只跑相对路径里带它的(可给几个);不给就全量")]
    public string[] Picks { get; init; } = [];

    [CommandOption("-f|--fast")]
    [Description("跳过带 # slow 的用例")]
    public bool Fast { get; init; }
}

/// <summary>剥注释 —— **不进解释器**,是源码加工(发布产物里的标准库就是这么来的,
/// 见 csproj 的 StripLibComments)。`--warn` 对它没有意义。</summary>
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

/// <summary>转储 AST —— 和 `strip` 一样**不进解释器**:它只做"读源码、出一份树"。

/// 用处是**动解析器前后各跑一遍逐字节 diff**(见 `Cli/AstDump.cs` 抬头),所以实参收的是
/// 一摞路径而不是一个文件:一次把整个语料转出去,重定向到文件再比。</summary>
internal sealed class AstSettings : CliSettings
{
    [CommandArgument(0, "<文件或目录…>")]
    [Description("目录会递归收 *.rav")]
    public string[] Paths { get; init; } = [];
}

internal sealed class AstCommand : Command<AstSettings>
{
    public override int Execute(CommandContext context, AstSettings settings, CancellationToken cancellation)
        => AstDump.Run(settings.Paths) ? 0 : 1;
}
