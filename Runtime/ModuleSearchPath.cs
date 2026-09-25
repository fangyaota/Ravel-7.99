namespace Ravel.Runtime;

/// <summary>模块文件的搜索目录(定义在一处,predefined.rav 与 using/ravel 共用)。
/// 相对路径按当前工作目录解析;`/workspace/ravel/` 是历史容器工作目录,留着兼容旧脚本。
/// 此前 predefined.rav 与 using 各写了一份、顺序还不一致,这里统一成「先本地再 lib/」。</summary>
internal static class ModuleSearchPath
{
    /// <summary>按优先级排列</summary>
    public static readonly string[] Defaults =
    [
        "./",
        "lib/",
        "/workspace/ravel/lib/",
        "/workspace/ravel/",
    ];
}
