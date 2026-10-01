using System.Text;

namespace Ravel.Runtime;

/// <summary>`System.Cmd` —— 跑外部命令。
///
/// 走**系统 shell**(Windows 上是 `cmd.exe /c`,别处 `/bin/sh -c`):管道、重定向、通配符
/// 这些都归它管,我们不解析。**非零退出码不是错误** —— 程序失败是常事,原样放在 `code` 里
/// 交给调用方判断(真正起不来进程才是错)。
///
/// 交回的是一张 dict:`out`(标准输出)/ `err`(标准错误)/ `code`(退出码)。
/// 两路输出**同时**抽走(各自一个线程)—— 顺序读会在大输出时死锁。</summary>
public partial class Interpreter
{
    [Sys("Cmd")]
    private static RuntimeValue RunCmd(RuntimeValue a) => Fs("跑命令", () =>
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
        var outTask = System.Threading.Tasks.Task.Run(() => ReadAllBytes(proc.StandardOutput.BaseStream));
        var errTask = System.Threading.Tasks.Task.Run(() => ReadAllBytes(proc.StandardError.BaseStream));
        proc.WaitForExit();

        var entries = new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("out")] = new StringVal(DecodeOutput(outTask.Result)),
            [new StringVal("err")] = new StringVal(DecodeOutput(errTask.Result)),
            [new StringVal("code")] = new IntVal(proc.ExitCode),
        };
        return new DictVal(entries);
    });

    private static byte[] ReadAllBytes(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>外部命令的输出按什么编码解?——**先按 UTF-8 试,不合法就退回控制台编码**。
    /// 两边都常见:git / python 那些吐 UTF-8,而 `dir` 这类走的是控制台那套(中文 Windows
    /// 上就是 GBK)。合法的 UTF-8 里出现 GBK 字节的概率极低,所以这个判据够用。</summary>
    private static string DecodeOutput(byte[] bytes)
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
