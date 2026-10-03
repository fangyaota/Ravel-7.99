using System.Text;

namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

/// <summary>`System.Cmd` —— 跑外部命令。
///
/// 走**系统 shell**(Windows 上是 `cmd.exe /c`,别处 `/bin/sh -c`):管道、重定向、通配符
/// 这些都归它管,我们不解析。**非零退出码不是错误** —— 程序失败是常事,原样放在 `code` 里
/// 交给调用方判断(真正起不来进程才是错)。
///
/// 交回的是一张 dict:`out`(标准输出)/ `err`(标准错误)/ `code`(退出码)。
/// 两路输出**同时**抽走(各自一个线程)—— 顺序读会在大输出时死锁。</summary>
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

    /// <summary>`System.Open x` —— **交给系统去打开**:文件用默认程序(网页就开默认浏览器),
    /// 网址也一样。是"打开它"而不是"跑它",所以**不等它关掉**,立刻交回来。
    ///
    /// 为什么要引擎里出一条:这件事**每台机器做法都不一样**(Windows `start` / macOS `open` /
    /// Linux `xdg-open`),而库里要判平台只能去嗅环境变量 —— 判错了就是"什么都没发生",
    /// 静默失败。.NET 那一条 `UseShellExecute = true` 正好是"让系统自己决定拿谁开"的
    /// 跨平台说法,而且**不走 shell**:路径里的空格、中文、`&` 都不用自己转义。
    ///
    /// 打不开就报 `IoError` —— 目标不存在、没有默认程序、被策略挡了,都落这儿。</summary>
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

    /// <summary>外部命令的输出按什么编码解?——**先按 UTF-8 试,不合法就退回控制台编码**。
    /// 两边都常见:git / python 那些吐 UTF-8,而 `dir` 这类走的是控制台那套(中文 Windows
    /// 上就是 GBK)。合法的 UTF-8 里出现 GBK 字节的概率极低,所以这个判据够用。</summary>
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
