namespace Ravel.Extensions;

using Spectre.Console;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Native")]
internal static class TerminalNative
{
    [RavelFn("TerminalRender")]
    public static RuntimeValue TerminalRender(RuntimeValue markup, RuntimeValue ansi) => Bad("渲染标记", () =>
    {
        var w = new StringWriter();
        Screen(w, ansi is BoolVal { Value: true }).Markup(Text(markup, "TerminalRender 的标记文本").Value);
        return new StringVal(w.ToString());
    });

    [RavelFn("TerminalStrip")]
    public static RuntimeValue TerminalStrip(RuntimeValue markup) => Bad("剥标记", () =>
        new StringVal(Markup.Remove(Text(markup, "TerminalStrip 的标记文本").Value)));

    [RavelFn("TerminalEscape")]
    public static RuntimeValue TerminalEscape(RuntimeValue text) => Bad("转义标记", () =>
        new StringVal(Markup.Escape(Text(text, "TerminalEscape 的文本").Value)));

    [RavelFn("TerminalTty")]
    public static RuntimeValue TerminalTty(RuntimeValue _) => new BoolVal(!Console.IsOutputRedirected);

    [RavelFn("TerminalWidth")]
    public static RuntimeValue TerminalWidth(RuntimeValue _) => IntVal.Of(Width());

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

    [RavelFn("TerminalAsk")]
    public static RuntimeValue TerminalAsk(RuntimeValue prompt) => Bad("问一句", () =>
        new StringVal(Live().Prompt(new TextPrompt<string>(Text(prompt, "TerminalAsk 的提示").Value).AllowEmpty())));

    [RavelFn("TerminalConfirm")]
    public static RuntimeValue TerminalConfirm(RuntimeValue prompt) => Bad("问一句", () =>
        new BoolVal(Live().Confirm(Text(prompt, "TerminalConfirm 的提示").Value, defaultValue: true)));

    [RavelFn("TerminalChoose")]
    public static RuntimeValue TerminalChoose(RuntimeValue question, RuntimeValue choices) => Bad("选一个", () =>
    {
        var options = Elements(choices, "TerminalChoose 的选项").Select(c => Text(c, "TerminalChoose 的选项").Value);
        return new StringVal(Live().Prompt(
            new SelectionPrompt<string>().Title(Text(question, "TerminalChoose 的问题").Value).AddChoices(options)));
    });

    [RavelFn("TerminalAskDefault")]
    public static RuntimeValue TerminalAskDefault(RuntimeValue prompt, RuntimeValue dflt) => Bad("问一句", () =>
        new StringVal(Live().Prompt(new TextPrompt<string>(Text(prompt, "TerminalAskDefault 的提示").Value)
            .DefaultValue(Text(dflt, "TerminalAskDefault 的默认值").Value))));

    [RavelFn("TerminalConfirmDefault")]
    public static RuntimeValue TerminalConfirmDefault(RuntimeValue prompt, RuntimeValue dflt) => Bad("问一句", () =>
        new BoolVal(Live().Confirm(Text(prompt, "TerminalConfirmDefault 的提示").Value,
                                   defaultValue: dflt is BoolVal { Value: true })));

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

        return IntVal.Of(Live().Prompt(prompt));
    });

    // ── 下面是自己人 ──

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

    private static IAnsiConsole Screen(TextWriter w, bool ansi) => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
        ColorSystem = ansi ? ColorSystemSupport.Standard : ColorSystemSupport.NoColors,
        Out = new AnsiConsoleOutput(w),
    });

    private static IAnsiConsole Live() => AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.Detect,
        ColorSystem = ColorSystemSupport.Detect,
        Out = new AnsiConsoleOutput(Console.Out),
    });

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
