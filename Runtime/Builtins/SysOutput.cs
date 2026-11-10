namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

internal static class SysOutput
{
    [Sys("WriteLine")]
    public static RuntimeValue WriteLineToOut(RuntimeValue a)
    {
        Console.WriteLine(Show(a));
        return VoidVal.Instance;
    }

    [Sys("Write")]
    public static RuntimeValue WriteToOut(RuntimeValue a)
    {
        Console.Write(Show(a));
        return VoidVal.Instance;
    }

    [Sys("ReadLine")]
    public static RuntimeValue ReadLineFromIn(RuntimeValue _) => new StringVal(Console.ReadLine() ?? "");

    [Sys("WriteErr")]
    public static RuntimeValue WriteToErr(RuntimeValue a)
    {
        Console.Error.Write(Show(a));
        return VoidVal.Instance;
    }

    [Sys("WriteLineErr")]
    public static RuntimeValue WriteLineToErr(RuntimeValue a)
    {
        Console.Error.WriteLine(Show(a));
        return VoidVal.Instance;
    }

    [Sys("ReadAllInput")]
    public static RuntimeValue ReadAllInput(RuntimeValue _) => new StringVal(Console.In.ReadToEnd());

    // ── 抓 stdout ──
    //
    // 这两条是 `Console.SetOut` 那一对的**原语化** —— 库想要"先把这段代码的打印收起来,
    // 回头再决定打不打"时用它(REPL 的「显示结果」开关就是;C# 那版 REPL 里写的是
    // 同两句话)。**只动 stdout,不动 stderr**:报错报告、警告那些引擎自己的话不受影响。
    //
    // 为什么是**两条**而不是"收一个函数进去":内置这一族没有从 C# 这边回调 Ravel 函数的
    // 口子,收函数就得碰求值器内部;两条不必,而且 Ravel 那边本来就有 `try`,
    // 收尾该放哪一目了然 —— 失败那条路也得收,不然 stdout 一直被抓着。

    [Sys("CaptureStart")]
    public static RuntimeValue CaptureStart(RuntimeValue _)
    {
        if (_capture is not null) return VoidVal.Instance;

        _savedOut = Console.Out;
        _capture = new StringWriter();
        Console.SetOut(_capture);
        return VoidVal.Instance;
    }

    [Sys("CaptureEnd")]
    public static RuntimeValue CaptureEnd(RuntimeValue _)
    {
        if (_capture is not { } buffer) return new StringVal("");

        Console.SetOut(_savedOut ?? Console.Out);
        _capture = null;
        _savedOut = null;
        return new StringVal(buffer.ToString());
    }

    // `Console.SetOut` 本来就是进程级的,收的那一份状态跟着一起放这儿
    private static StringWriter? _capture;
    private static TextWriter? _savedOut;

    // ── 终端:读单个键 + 屏幕 ──
    //
    // 这四条是"写一个编辑器"绕不开的那一格。前三条本来能用 ANSI 转义串拼出来,
    // 但那要靠终端**开了 VT 处理**;`Console` 这几个 API 到哪儿都对,所以走它们。
    // (库那边照旧只拿原语,策略在 `lib/repl.rav`。)

    [Sys("ReadKey")]
    public static RuntimeValue ReadKey(RuntimeValue _)
    {
        ConsoleKeyInfo k;
        try
        {
            k = Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            return VoidVal.Instance;              // 重定向且读空了 —— "没有输入了"
        }

        var name = KeyName(k);
        return name is null ? VoidVal.Instance : new StringVal(name);
    }

    [Sys("ScreenClear")]
    public static RuntimeValue ScreenClear(RuntimeValue _) => Screen(() => Console.Clear());

    [Sys("CursorTo")]
    public static RuntimeValue CursorTo(RuntimeValue a, RuntimeValue b) => Screen(() =>
    {
        var r = BuiltinClasses.IntArg(a, "CursorTo 的行");
        var c = BuiltinClasses.IntArg(b, "CursorTo 的列");
        Console.SetCursorPosition(Math.Clamp(c, 0, Math.Max(0, SafeWidth() - 1)),
                                  Math.Clamp(r, 0, Math.Max(0, SafeHeight() - 1)));
    });

    [Sys("CursorShow")]
    public static RuntimeValue CursorShow(RuntimeValue a)
        => Screen(() => Console.CursorVisible = a is BoolVal { Value: true });

    private static RuntimeValue Screen(Action body)
    {
        try
        {
            body();
        }
        catch (IOException)
        {
            // 没有终端
        }

        return VoidVal.Instance;
    }

    private static int SafeWidth()
    {
        try
        {
            var w = Console.WindowWidth;
            return w > 0 ? w : 80;
        }
        catch (IOException)
        {
            return 80;
        }
    }

    private static int SafeHeight()
    {
        try
        {
            var h = Console.WindowHeight;
            return h > 0 ? h : 24;
        }
        catch (IOException)
        {
            return 24;
        }
    }

    private static string? KeyName(ConsoleKeyInfo k)
    {
        var mods = "";
        if (k.Modifiers.HasFlag(ConsoleModifiers.Control)) mods += "ctrl+";
        if (k.Modifiers.HasFlag(ConsoleModifiers.Alt)) mods += "alt+";
        if (k.Modifiers.HasFlag(ConsoleModifiers.Shift)) mods += "shift+";

        var named = k.Key switch
        {
            ConsoleKey.Enter => "enter",
            ConsoleKey.Tab => "tab",
            ConsoleKey.Backspace => "backspace",
            ConsoleKey.Delete => "delete",
            ConsoleKey.Escape => "esc",
            ConsoleKey.UpArrow => "up",
            ConsoleKey.DownArrow => "down",
            ConsoleKey.LeftArrow => "left",
            ConsoleKey.RightArrow => "right",
            ConsoleKey.Home => "home",
            ConsoleKey.End => "end",
            ConsoleKey.PageUp => "pgup",
            ConsoleKey.PageDown => "pgdn",
            ConsoleKey.Insert => "insert",
            >= ConsoleKey.F1 and <= ConsoleKey.F12 => "f" + (k.Key - ConsoleKey.F1 + 1),
            _ => null,
        };

        // 有名字就给名字(修饰键带上);没名字而**又按了修饰键**,那还是那个字符本身
        // —— 编辑器要的是"用户想输入什么",`ctrl+a` 这种组合键由调用方自己看名字
        if (named is not null) return mods + named;

        // 可打印的那一个字符。控制字符(没名字的那些)一律不认
        return !char.IsControl(k.KeyChar) ? k.KeyChar.ToString() : mods.Length > 0 ? mods.TrimEnd('+') : null;
    }
}
