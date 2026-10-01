namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

/// <summary>`System` 的打印与输入那几条(见 <see cref="SysAttribute"/>)。
///
/// 标准错误那两条:和 `Write` / `WriteLine` 一个样,只是走 `Console.Error` ——
/// 报错报告、`[outdated]`、警告那些**引擎自己的话**也在那儿;控制台当文件用时,
/// `stderr` 那个 `IFile` 落到这儿。</summary>
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

    /// <summary>把标准输入读到 EOF(终端上要 Ctrl+Z / Ctrl+D 收)。`stdin` 那个文件的
    /// `Read ()` 靠它 —— 想读一行用 `ReadLine`(它是 `input`)。</summary>
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

    /// <summary>开始把 stdout 收进缓冲区。**已经在收就什么都不做** —— 嵌套没有意义,
    /// 内层那一下收工也不该把外层正在收的东西放出去。</summary>
    [Sys("CaptureStart")]
    public static RuntimeValue CaptureStart(RuntimeValue _)
    {
        if (_capture is not null) return VoidVal.Instance;

        _savedOut = Console.Out;
        _capture = new StringWriter();
        Console.SetOut(_capture);
        return VoidVal.Instance;
    }

    /// <summary>收工:把 stdout 换回去,交出这段时间写出去的那串。
    /// **没在收就交回空串** —— 忘了配对、或者在收尾那条路上又调了一次,都不该炸。</summary>
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

    /// <summary>读**一个键**,不等回车。交回一个**键名字符串**;
    /// **没有输入了**(stdin 关了、或者管道读空了)交回 `()`。
    ///
    /// 词汇表是定死的,而且一个按键**要么是一个字符、要么是个名字**,不会两头都像:
    ///
    ///     "a" "中" " " …                    可打印的:原样那一个字符
    ///     "enter" "tab" "backspace" "delete" "esc"
    ///     "up" "down" "left" "right" "home" "end" "pgup" "pgdn" "insert"
    ///     "f1" … "f12"
    ///     "ctrl+a" "alt+x" "shift+tab"        修饰键当**前缀**
    ///
    /// **Ctrl+C 不动它**:没设 `TreatControlCAsInput`,于是它照旧杀进程 ——
    /// 那是 REPL 写坏时唯一的逃生口,不能让编辑器把它吃掉。</summary>
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

    /// <summary>清屏</summary>
    [Sys("ScreenClear")]
    public static RuntimeValue ScreenClear(RuntimeValue _) => Screen(() => Console.Clear());

    /// <summary>把光标挪到第 `row` 行第 `col` 列(**都从 0 数**)。越界**夹住**不报错 ——
    /// `Console.SetCursorPosition` 越界是要抛的,而"画到屏幕外面去了"这种事
    /// 在重画循环里太常见了,不该把编辑器打断。</summary>
    [Sys("CursorTo")]
    public static RuntimeValue CursorTo(RuntimeValue a, RuntimeValue b) => Screen(() =>
    {
        var r = BuiltinClasses.IntArg(a, "CursorTo 的行");
        var c = BuiltinClasses.IntArg(b, "CursorTo 的列");
        Console.SetCursorPosition(Math.Clamp(c, 0, Math.Max(0, SafeWidth() - 1)),
                                  Math.Clamp(r, 0, Math.Max(0, SafeHeight() - 1)));
    });

    /// <summary>光标露不露出来。编辑器自己画光标块时要藏起来。
    /// **看不见终端就当"做过了"** —— `Console.CursorVisible` 在没有终端时是抛的,
    /// 而这不是那种值得把程序打断的事。</summary>
    [Sys("CursorShow")]
    public static RuntimeValue CursorShow(RuntimeValue a)
        => Screen(() => Console.CursorVisible = a is BoolVal { Value: true });

    /// <summary>屏幕那三条共用的兜底:**没有终端时静默跳过**。
    ///
    /// 它们都会抛 `IOException`("句柄无效")—— 输出重定向到管道/文件时根本没有屏幕。
    /// 而"往屏幕上画"这件事在那种场合**本来就什么也做不了**,那就不该把一个
    /// 管道里跑的 REPL 打断(和 `Terminal.Width` 退回 80 是一个道理)。
    /// 参数类型不对照旧当场报(那是调用方写错了,不是环境没有)。</summary>
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

    /// <summary>终端宽/高,**看不见时给一个约定俗成的数**(80 × 24)。
    /// `Console.WindowWidth` 在重定向时是抛的,而调用方多半只是想按宽度折个行。</summary>
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

    /// <summary>`ConsoleKeyInfo` → 那个键名。认不出的交回 null(调用方给 `()`)。</summary>
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
