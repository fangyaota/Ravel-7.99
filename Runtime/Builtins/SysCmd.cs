using System.Text;

namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

internal static class SysCmd
{
    // 两条路,同一个实现 —— 和 HTTP 那边一个规矩:
    //   * `Cmd`     阻塞到命令跑完(顶层脚本那种);
    //   * `CmdTask` 交回一个句柄,任务里 `group.Await` 它,别的任务接着跑。
    // 几个任务各自 fork 一个进程时,这条路才真的重叠得起来。
    [Sys("Cmd")]
    public static RuntimeValue RunCmd(RuntimeValue a)
        => Fs("跑命令", () => RunCmdCore(a).GetAwaiter().GetResult());

    [Sys("CmdTask")]
    public static RuntimeValue CmdTask(RuntimeValue a) => new WaitableVal(FsAsync("跑命令", () => RunCmdCore(a)));

    private static async Task<RuntimeValue> RunCmdCore(RuntimeValue a)
    {
        var command = As<StringVal>(a, "cmd 的命令").Value;
        var windows = OperatingSystem.IsWindows();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = windows ? "cmd.exe" : "/bin/sh",
            Arguments = (windows ? "/c " : "-c ") + command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new RuntimeException("跑命令失败: 进程起不来", ErrorKind.Io);
        // 两路输出**同时**抽走(各自一个任务)—— 顺序读会在大输出时死锁。
        var outTask = ReadAllBytes(proc.StandardOutput.BaseStream);
        var errTask = ReadAllBytes(proc.StandardError.BaseStream);
        await proc.WaitForExitAsync();

        var entries = new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("out")] = new StringVal(DecodeOutput(await outTask)),
            [new StringVal("err")] = new StringVal(DecodeOutput(await errTask)),
            [new StringVal("code")] = IntVal.Of(proc.ExitCode),
        };
        return new DictVal(entries);
    }

    public static async Task<byte[]> ReadAllBytes(Stream s)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }

    [Sys("Open")]
    public static RuntimeValue Open(RuntimeValue a)
    {
        var target = As<StringVal>(a, "Open 的目标").Value;

        // **本机的东西存在不存在,自己先说一句** —— 交给系统的话回来的是它那句
        // "系统找不到指定的文件",那是跟着系统语言变的,而报错文案是要被用例钉住的。
        // 网址(`http://…` 那种)不走这一条:它本来就不该在本地存在。
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.IsFile)
        {
            if (!File.Exists(target) && !Directory.Exists(target))
                throw new RuntimeException($"打不开 '{target}':没有这个东西", ErrorKind.Io);
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or InvalidOperationException
                                      or FileNotFoundException)
        {
            throw new RuntimeException($"打不开 '{target}':{ex.Message}", ErrorKind.Io);
        }

        return VoidVal.Instance;
    }

    public static string DecodeOutput(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException)
        {
            return Console.OutputEncoding.GetString(bytes);
        }
    }
}
