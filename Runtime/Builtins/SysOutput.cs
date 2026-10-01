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
}
