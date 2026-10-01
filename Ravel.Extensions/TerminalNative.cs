namespace Ravel.Extensions;

using Spectre.Console;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>终端 —— 标记渲染、尺寸、交互问答。落到 `Spectre.Console` 上(`lib/terminal.rav` 那层
/// 只管"什么时候用哪一条")。
///
/// **渲染那两条是纯函数**:`TerminalRender` 交回**一串**(不是往屏幕上写)。写出去是 Ravel 的事
/// (`System.WriteLine`)—— 这一条很要紧:测试运行器把 `Console.Out` 换成自己的 StringWriter,
/// 而 Spectre 那台静态的 `AnsiConsole` 是**第一次用到时就把它抓住**的,
/// 直接从这儿写会绕开替换、还会把后面每一条用例的输出都引到那个已经作废的 writer 上。
/// 交回字符串就绕开了整件事,顺带也**钉得住**(同样一棵标记每次给同一串)。
///
/// 标记的语法就是 Spectre 那套(`[red]…[/]`、`[b]…[/]`、`[[` 表示一个方括号)——
/// 不是新发明的一套,想查用法去查 Spectre 的文档就行。`TerminalStrip` 把标记剥掉,
/// 要往日志里写"同一条消息但没有颜色"时用。
///
/// 交互那三条(`Ask` / `Confirm` / `Choose`)得用**真的**那台(要读键盘、要问终端多宽),
/// 所以它们当场建一个 `IAnsiConsole`,而不是用那个会被缓存住的静态 `AnsiConsole`。</summary>
[RavelModule("Native")]
internal static class TerminalNative
{
    /// <summary>标记文本 → 一串。`ansi` 说要不要上色 ——
    /// **由调用方给**,而不是这儿自己看 `Console.IsOutputRedirected`:那看的是**进程**的
    /// stdout,而测试运行器换的是 `Console.SetOut`(进程没被重定向),于是同一条用例
    /// 在这儿跑和进了管道跑会给出两种结果。</summary>
    [RavelFn("TerminalRender")]
    public static RuntimeValue TerminalRender(RuntimeValue markup, RuntimeValue ansi) => Bad("渲染标记", () =>
    {
        var w = new StringWriter();
        Screen(w, ansi is BoolVal { Value: true }).Markup(Text(markup, "TerminalRender 的标记文本").Value);
        return new StringVal(w.ToString());
    });

    /// <summary>把标记剥掉,只留文本(要往日志、文件里写的时候用)</summary>
    [RavelFn("TerminalStrip")]
    public static RuntimeValue TerminalStrip(RuntimeValue markup) => Bad("剥标记", () =>
        new StringVal(Markup.Remove(Text(markup, "TerminalStrip 的标记文本").Value)));

    /// <summary>把一段**普通文本**里的 `[` `]` 转义掉,好安全地放进标记里。
    /// (用户输入的字里带个方括号是很正常的事,不转义就成了标记、把整串搞坏)</summary>
    [RavelFn("TerminalEscape")]
    public static RuntimeValue TerminalEscape(RuntimeValue text) => Bad("转义标记", () =>
        new StringVal(Markup.Escape(Text(text, "TerminalEscape 的文本").Value)));

    /// <summary>输出是不是一个**真终端**(管道的另一头不是)。决定要不要上色看它</summary>
    [RavelFn("TerminalTty")]
    public static RuntimeValue TerminalTty(RuntimeValue _) => new BoolVal(!Console.IsOutputRedirected);

    /// <summary>终端宽多少格。**看不见终端时给 80** —— 管道里问不到尺寸,
    /// 而调用方多半是想按宽度折个行,给个约定俗成的数比报错有用</summary>
    [RavelFn("TerminalWidth")]
    public static RuntimeValue TerminalWidth(RuntimeValue _) => new IntVal(Width());

    /// <summary>把几行**已经带标记的**文本装进一个方框(就是 Spectre 的 `Panel`),交回整块。
    /// 和 `TerminalRender` 一样是**纯函数**,只是外面多包一圈边框 ——
    /// `Repl/NeoInteractor.cs` 那边写的是 `AnsiConsole.Write(new Panel(text))`,这两条是同一件事。
    ///
    /// 宽度取**当前终端**的宽(看不见终端就用 80):方框要跟屏幕一样宽,这是它唯一的意义。
    /// (自己拿 `┌─┐` 拼也行,但每行的**纯文本**宽度得先算出来 —— 行里全是标记,
    /// 那笔账不该由调用方算。)</summary>
    [RavelFn("TerminalPanel")]
    public static RuntimeValue TerminalPanel(RuntimeValue lines, RuntimeValue ansi) => Bad("画方框", () =>
    {
        var text = string.Join("\n", Elements(lines, "TerminalPanel 的几行")
            .Select(l => Text(l, "TerminalPanel 的一行").Value));

        var w = new StringWriter();
        var console = Screen(w, ansi is BoolVal { Value: true });
        console.Profile.Width = Width();
        console.Write(new Panel(text));
        return new StringVal(w.ToString());
    });

    /// <summary>问一句、读一行(空行也收 —— 卡着不让人过比读到一个空串烦人)</summary>
    [RavelFn("TerminalAsk")]
    public static RuntimeValue TerminalAsk(RuntimeValue prompt) => Bad("问一句", () =>
        new StringVal(Live().Prompt(new TextPrompt<string>(Text(prompt, "TerminalAsk 的提示").Value).AllowEmpty())));

    /// <summary>是/否。默认是"是"(回车就过)</summary>
    [RavelFn("TerminalConfirm")]
    public static RuntimeValue TerminalConfirm(RuntimeValue prompt) => Bad("问一句", () =>
        new BoolVal(Live().Confirm(Text(prompt, "TerminalConfirm 的提示").Value, defaultValue: true)));

    /// <summary>从一列里挑一个 —— 上下键选,交回**选中的那个字符串**</summary>
    [RavelFn("TerminalChoose")]
    public static RuntimeValue TerminalChoose(RuntimeValue question, RuntimeValue choices) => Bad("选一个", () =>
    {
        var options = Elements(choices, "TerminalChoose 的选项").Select(c => Text(c, "TerminalChoose 的选项").Value);
        return new StringVal(Live().Prompt(
            new SelectionPrompt<string>().Title(Text(question, "TerminalChoose 的问题").Value).AddChoices(options)));
    });

    /// <summary>问一句、读一行,**回车收默认值**(那个默认值就摆在提示行里,可以直接改掉)。
    /// `TerminalAsk` 是它没有默认值的那一版</summary>
    [RavelFn("TerminalAskDefault")]
    public static RuntimeValue TerminalAskDefault(RuntimeValue prompt, RuntimeValue dflt) => Bad("问一句", () =>
        new StringVal(Live().Prompt(new TextPrompt<string>(Text(prompt, "TerminalAskDefault 的提示").Value)
            .DefaultValue(Text(dflt, "TerminalAskDefault 的默认值").Value))));

    /// <summary>是/否,**默认值由调用方给**。(`TerminalConfirm` 那条恒为"是",
    /// 是给"要不要接着?"这种问法用的;开关那两处要的是"默认取反当前值",所以得传得进来)</summary>
    [RavelFn("TerminalConfirmDefault")]
    public static RuntimeValue TerminalConfirmDefault(RuntimeValue prompt, RuntimeValue dflt) => Bad("问一句", () =>
        new BoolVal(Live().Confirm(Text(prompt, "TerminalConfirmDefault 的提示").Value,
                                   defaultValue: dflt is BoolVal { Value: true })));

    /// <summary>一列里挑一个,交回**选中的序号**(不是那个字符串)—— 调用方按序号分派就是了。
    ///
    /// 和 `TerminalChoose` 的分别:这一条是**主菜单**那种用法 —— 带搜索、每屏定死 6 条、
    /// 每项前面挂着序号。参数是照着 `Repl/NeoInteractor.cs` 那台 `SelectionPrompt<int>`
    /// 一条条抄的,为的是两边的菜单长得一模一样。</summary>
    [RavelFn("TerminalMenu")]
    public static RuntimeValue TerminalMenu(RuntimeValue title, RuntimeValue items) => Bad("菜单", () =>
    {
        var options = Elements(items, "TerminalMenu 的选项")
            .Select(c => Text(c, "TerminalMenu 的选项").Value)
            .ToList();

        var prompt = new SelectionPrompt<int>()
            .Title(Text(title, "TerminalMenu 的标题").Value)
            .EnableSearch()
            .PageSize(6)
            .MoreChoicesText("[grey]（往下滑获取更多选项）[/]")
            .SearchPlaceholderText("[grey]（输入序号快速跳转：）[/]")
            .UseConverter(i => $"{i}: {options[i]}")
            .AddChoices(Enumerable.Range(0, options.Count));

        return new IntVal(Live().Prompt(prompt));
    });

    // ── 下面是自己人 ──

    /// <summary>终端宽多少格(**看不见就给 80**)。`Console.WindowWidth` 重定向时是抛的,
    /// 而调用方多半只是想按宽度折个行</summary>
    private static int Width()
    {
        try
        {
            var n = Console.WindowWidth;
            return n > 0 ? n : 80;
        }
        catch (IOException)
        {
            return 80;                 // 没接终端
        }
    }

    /// <summary>往一个流里渲染的"屏" —— 不上色时 `Ansi = No`:标记照样解析、照样消失,
    /// 只是不吐转义序列出来</summary>
    private static IAnsiConsole Screen(TextWriter w, bool ansi) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
        ColorSystem = ansi ? ColorSystemSupport.Standard : ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(w),
    });

    /// <summary>真终端那一台(**不缓存**)—— 交互几条要读键盘、要问尺寸,得接真的</summary>
    private static IAnsiConsole Live() => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.Detect,
        ColorSystem = ColorSystemSupport.Detect,
        Out = new AnsiConsoleOutput(Console.Out),
    });

    /// <summary>终端那一批的兜底:Spectre 对**写坏的标记**是抛异常(它自己那种),
    /// 漏出去会把程序打掉 —— 和别处一条规矩</summary>
    private static RuntimeValue Bad(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
                                   or IOException or NotSupportedException)
        {
            throw Fail($"{what}失败: {ex.Message}");
        }
    }
}
