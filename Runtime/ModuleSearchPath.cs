namespace Ravel.Runtime;

/// <summary>模块文件的搜索目录(定义在一处,predefined.rav 与 using/ravel 共用)。
/// 相对路径按当前工作目录解析;`/workspace/ravel/` 是历史容器工作目录,留着兼容旧脚本。
/// 此前 predefined.rav 与 using 各写了一份、顺序还不一致,这里统一成「先本地再 lib/」。
///
/// **最后两格是"跑起来的那个程序集的目录"**:插件 dll 由构建拷到 `$(OutDir)plugins/`
/// (见 Ravel.csproj 的 `CopyPlugins`),而 `plugins/...` 这个相对路径本来是靠 `"./"` 也就是
/// **工作目录**解的 —— 从别的目录跑 `dotnet /别处/out/ravel.dll 脚本.rav` 就找不到。
/// 从前只有 `structures.rav` 一个可选库踩这条,现在官方扩展(`native.rav`)是六七个库的地基,
/// 得堵上。放**末尾**:predefined.rav 也走这张表,别让它抢在 `lib/` 前面。
///
/// **`<程序集目录>/lib/` 是同一个毛病的另一半**:标准库不是编译产物,解释器是**读源码**跑的
/// (`predefined.rav` 就在里头),所以发布时整个 `lib/` 也拷到 dll 旁边(见 `CopyLib`)。
/// 没有这一格的话,`out/` 那一份只在**工作目录正好有 `lib/`** 时跑得起来 —— 换个目录就
/// 「找不到 lib/predefined.rav」。</summary>
internal static class ModuleSearchPath
{
    /// <summary>按优先级排列</summary>
    public static readonly string[] Defaults =
    [
        "./",
        "lib/",
        "/workspace/ravel/lib/",
        "/workspace/ravel/",
        AppContext.BaseDirectory,
        Path.Combine(AppContext.BaseDirectory, "lib"),
    ];
}
