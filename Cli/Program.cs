using Ravel;
using Ravel.Runtime;
using Ravel.Testing;

// 脚本名**之前**那些 `--` 开关由 CLI 自己吃;脚本名和它之后的一律是脚本的
// (`System.Args ()` 交回的就是后者)。所以 `ravel --warn x.rav --warn` 里后面那个
// `--warn` 是给脚本的,和前面那个不是一回事。
var warn = false;
var first = 0;
for (; first < args.Length && args[first].StartsWith('-'); first++)
{
    switch (args[first])
    {
        case "--warn" or "-w": warn = true; break;
        default:
            Console.WriteLine($"不认识的开关 '{args[first]}'。用法: ravel [--warn] [脚本.rav [参数…]]");
            Environment.ExitCode = 1;
            return;
    }
}

var rest = args[first..];

if (rest.Length == 0)
{
    RunRepl(warn);
}
else if (rest[0] == "test")
{
    // `ravel test [词…] [--fast]`:
    //
    //     词    只跑**相对路径里带它**的(不区分大小写,子目录名也算在内)。
    //           给几个就是"含其中任意一个"。不给就是全量。
    //             ravel test http          名字/目录里带 http 的那些
    //             ravel test 29 30         29x 和 30x
    //           **迭代的时候就用它** —— 全量那份要跑几分钟,改一行不值得。
    //     --fast 跳过带 `# slow` 的(起服务器、等计时器那几条)。
    //
    // `--warn` 传下去:每条用例都带上开关跑一遍。**比对的是 stdout**,警告走 stderr ——
    // 所以这一趟不会让谁红掉,它就是个"整个用例库过一遍筛子"的用法:
    //     ravel --warn test 2>&1 | grep 警告
    // (单独一条用例要**钉住**警告长什么样,在它开头写 `# warn`,见 GoldenTestRunner)
    var pick = new List<string>();
    var fast = false;
    foreach (var arg in rest[1..])
    {
        if (arg is "--fast" or "-f") fast = true;
        else pick.Add(arg);
    }

    if (!GoldenTestRunner.RunAll(warn, [.. pick], fast)) Environment.ExitCode = 1;
}
else if (rest[0] == "strip")
{
    // `ravel strip <文件或目录>`:把 `.rav` 里的注释剥掉(就地)。**不进解释器** ——
    // 这是源码加工,不跑代码;发布产物里的标准库就是这么来的(Ravel.csproj 的
    // `StripLibComments`),`--warn` 对它没有意义。
    if (!Strip.Run(rest[1..])) Environment.ExitCode = 1;
}
else
{
    // 脚本名之后那些交给 `System.Args ()`(REPL / `ravel test` 没有,它们是空的)
    RunFile(rest[0], rest[1..], warn);
}

/// <summary>不带参数 = 进 REPL。**这个 REPL 是 Ravel 自己写的**(`lib/repl.rav`,
/// 入口 `Repl.Run ()`)—— 引擎里那一版(C# 的 `NeoInteractor`)已经删掉:两边行为一模一样,
/// 而"这门语言能拿自己写自己的 REPL"正是它的门面(照它写一遍的动机、以及为什么能成,
/// `lib/repl.rav` 头上有)。
///
/// 所以这儿做的事就是**跑两行脚本**,不多不少:不去查模块、不去摸它的内部 —— 那样等于
/// 把 REPL 的入口又编回 C# 里,下回想改就得动引擎(`lib/repl.rav` 也就不用谈"库"了)。
///
/// 顺带白拿一条:C# 那版没有"看不见终端"这一路,而 `lib/repl.rav` 有(见它的 `Run`)——
/// 管道里喂进去的整段会被当成一段程序跑完,于是 `echo 'print 1' | ravel` 现在通。</summary>
static void RunRepl(bool warn)
    => RunSource("using \"repl.rav\"\nRepl.Run ()\n", "<repl>", [], warn);

static void RunFile(string path, string[] scriptArgs, bool warn)
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
/*
    Console.WriteLine($"── {path} ──");
    Console.WriteLine(source.Trim());
    Console.WriteLine("── Output ──");
*/
    RunSource(source, path, scriptArgs, warn);
    Console.WriteLine();
}

/// <summary>跑一段源码 —— **单文件和 REPL 入口都走这里**,报错就只写这一套。
/// 出事把退出码拨成 1(和测试那条线一个规矩:说了话就是出事)。</summary>
static void RunSource(string source, string path, string[] scriptArgs, bool warn)
{
    try
    {
        // 开关在**建完之后**才拨:构造时就把 predefined 跑了,而那是库、不是用户代码 ——
        // 提醒要说的是"你这几行",不该被库里的写法刷屏(`lib/` 里自己开另说)。
        new Interpreter(scriptArgs) { WarnForgotCall = warn }.Interpret(Parser.ParseSource(source, path));
    }
    // 运行时错误和语法错误渲染同一份报告(位置 + 源码行 + 插入符 + 调用栈),
    // 所以用一条 when 收下来,不必写两遍一模一样的 catch
    catch (Exception ex) when (ex is RuntimeException or SyntaxException)
    {
        Console.WriteLine($"Error: {ErrorReport.Format(ex)}");
        Environment.ExitCode = 1;
    }
    catch (ExitException ex)
    {
        // `exit ""` 是"什么都不说就结束",别打出一个空的 `Error:` 行
        // (退出码也跟着这条线走:说了话就是出事,于是 `Test.Report ()` 那种
        //  "跑完打一行汇总再抛出去"的脚本能让 CI 真的红掉)
        if (ex.Message.Length > 0)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
    catch (Exception ex)
    {
        // 走到这里说明解释器自己有 bug:该转成 RuntimeException/SyntaxException 的没转。
        // **内层异常一并打出来**:TypeInitializationException 这类外面那层什么都不说
        // (「静态构造函数抛了」),真正的缘由(少了哪个本机库、哪个文件)全在内层 ——
        // 只看外层那句,等于什么都没说。
        Console.WriteLine($"!! 解释器内部错误 {ex.GetType().Name}: {ex.Message}{Inner(ex)}");
        Environment.ExitCode = 1;
        // 上面那句故意不带栈,免得刷屏;但解释器自己的 bug 只能靠栈才查得下去
        // (递归到栈溢出这类尤其如此,消息里什么线索都没有)。要的时候开这个开关。
        if (Environment.GetEnvironmentVariable("RAVEL_TRACE") == "1") Console.WriteLine(ex.StackTrace);
    }
}

/// <summary>把内层异常一层层接在后面。**判断一条 C# 异常"到底为什么"常常全在内层** ——
/// `TypeInitializationException`(静态构造函数抛了)就是最典型的一个:它自己那句话
/// 什么线索都没有,真正的原因(文件不在、本机库加载不了)在下一层。</summary>
static string Inner(Exception ex)
{
    var text = "";
    for (var x = ex.InnerException; x is not null; x = x.InnerException)
        text += $"\n   :: {x.GetType().Name}: {x.Message}";
    return text;
}
