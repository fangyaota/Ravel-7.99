namespace Ravel.Runtime;

internal static class ModuleSearchPath
{
    public static readonly string[] Defaults =
    [
        "./",
        "lib/",
        "plugins/",
        "/workspace/ravel/lib/",
        "/workspace/ravel/",
        AppContext.BaseDirectory,
        Path.Combine(AppContext.BaseDirectory, "lib"),
        Path.Combine(AppContext.BaseDirectory, "plugins"),
    ];
}
