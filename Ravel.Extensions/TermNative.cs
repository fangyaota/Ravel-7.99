namespace Ravel.Extensions;

using Spectre.Console;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>终端 —— 标记渲染、尺寸、交互问答。落到 `Spectre.Console` 上(`lib/term.rav` 那层
/// 只管"什么时候用哪一条")。
///
/// **渲染那两条是纯函数**:`TermRender` 交回**一串**(不是往屏幕上写)。写出去是 Ravel 的事
/// (`System.WriteLine`)—— 这一条很要紧:测试运行器把 `Console.Out` 换成自己的 StringWriter,
/// 而 Spectre 那台静态的 `AnsiConsole` 是**第一次用到时就把它抓住**的,
/// 直接从这儿写会绕开替换、还会把后面每一条用例的输出都引到那个已经作废的 writer 上。
/// 交回字符串就绕开了整件事,顺带也**钉得住**(同样一棵标记每次给同一串)。
///
/// 标记的语法就是 Spectre 那套(`[red]…[/]`、`[b]…[/]`、`[[` 表示一个方括号)——
/// 不是新发明的一套,想查用法去查 Spectre 的文档就行。`TermStrip` 把标记剥掉,
/// 要往日志里写"同一条消息但没有颜色"时用。
///
/// 交互那三条(`Ask` / `Confirm` / `Choose`)得用**真的**那台(要读键盘、要问终端多宽),
/// 所以它们当场建一个 `IAnsiConsole`,而不是用那个会被缓存住的静态 `AnsiConsole`。</summary>
[RavelModule("Native")]
internal static class TermNative
{
    /// <summary>标记文本 → 一串。`ansi` 说要不要上色 ——
    /// **由调用方给**,而不是这儿自己看 `Console.IsOutputRedirected`:那看的是**进程**的
    /// stdout,而测试运行器换的是 `Console.SetOut`(进程没被重定向),于是同一条用例
    /// 在这儿跑和进了管道跑会给出两种结果。</summary>
    [RavelFn("TermRender")]
    public static RuntimeValue TermRender(RuntimeValue markup, RuntimeValue ansi) => Bad("渲染标记", () =>
    {
        var w = new StringWriter();
        Screen(w, ansi is BoolVal { Value: true }).Markup(Text(markup, "TermRender 的标记文本").Value);
        return new StringVal(w.ToString());
    });

    /// <summary>把标记剥掉,只留文本(要往日志、文件里写的时候用)</summary>
    [RavelFn("TermStrip")]
    public static RuntimeValue TermStrip(RuntimeValue markup) => Bad("剥标记", () =>
        new StringVal(Markup.Remove(Text(markup, "TermStrip 的标记文本").Value)));

    /// <summary>把一段**普通文本**里的 `[` `]` 转义掉,好安全地放进标记里。
    /// (用户输入的字里带个方括号是很正常的事,不转义就成了标记、把整串搞坏)</summary>
    [RavelFn("TermEscape")]
    public static RuntimeValue TermEscape(RuntimeValue text) => Bad("转义标记", () =>
        new StringVal(Markup.Escape(Text(text, "TermEscape 的文本").Value)));

    /// <summary>输出是不是一个**真终端**(管道的另一头不是)。决定要不要上色看它</summary>
    [RavelFn("TermTty")]
    public static RuntimeValue TermTty(RuntimeValue _) => new BoolVal(!Console.IsOutputRedirected);

    /// <summary>终端宽多少格。**看不见终端时给 80** —— 管道里问不到尺寸,
    /// 而调用方多半是想按宽度折个行,给个约定俗成的数比报错有用</summary>
    [RavelFn("TermWidth")]
    public static RuntimeValue TermWidth(RuntimeValue _)
    {
        try
        {
            var n = Console.WindowWidth;
            return new IntVal(n > 0 ? n : 80);
        }
        catch (IOException)
        {
            return new IntVal(80);                 // 没接终端
        }
    }

    /// <summary>问一句、读一行(空行也收 —— 卡着不让人过比读到一个空串烦人)</summary>
    [RavelFn("TermAsk")]
    public static RuntimeValue TermAsk(RuntimeValue prompt) => Bad("问一句", () =>
        new StringVal(Live().Prompt(new TextPrompt<string>(Text(prompt, "TermAsk 的提示").Value).AllowEmpty())));

    /// <summary>是/否。默认是"是"(回车就过)</summary>
    [RavelFn("TermConfirm")]
    public static RuntimeValue TermConfirm(RuntimeValue prompt) => Bad("问一句", () =>
        new BoolVal(Live().Confirm(Text(prompt, "TermConfirm 的提示").Value, defaultValue: true)));

    /// <summary>从一列里挑一个 —— 上下键选,交回**选中的那个字符串**</summary>
    [RavelFn("TermChoose")]
    public static RuntimeValue TermChoose(RuntimeValue question, RuntimeValue choices) => Bad("选一个", () =>
    {
        var options = Elements(choices, "TermChoose 的选项").Select(c => Text(c, "TermChoose 的选项").Value);
        return new StringVal(Live().Prompt(
            new SelectionPrompt<string>().Title(Text(question, "TermChoose 的问题").Value).AddChoices(options)));
    });

    // ── 下面是自己人 ──

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
