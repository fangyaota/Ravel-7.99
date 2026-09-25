namespace Ravel.Runtime;

/// <summary>模块文件加载:路径搜索、循环引用检测、已加载去重。
/// 搜索目录 = `references` 里的用户目录(优先) + <see cref="ModuleSearchPath.Defaults"/>。</summary>
public partial class Interpreter
{
    /// <summary>把模块名解析为绝对路径;找不到返回 null</summary>
    private string? ResolveModulePath(string path)
    {
        var refs = new List<string>();
        var rv = _global.TryLookup("references");
        if (rv?.Value is ListVal lv) refs.AddRange(lv.Elements.Select(e => As<StringVal>(e, "references 的元素").Value));
        refs.AddRange(ModuleSearchPath.Defaults);

        // 两轮:先在整个搜索路径里找原样文件名,再找补了 .rav 的(精确名优先于补后缀)
        foreach (var candidate in new[] { path, path + ".rav" })
            foreach (var d in refs)
            {
                var p = Path.Combine(d, candidate);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }

        return null;
    }

    /// <summary>加载模块文件,解析为 BlockExpr;找不到抛异常,循环引用抛异常,已加载返回 null(视为空块)</summary>
    private BlockExpr? LoadModuleAst(string path)
    {
        var full = ResolveModulePath(path);
        if (full == null) throw new RuntimeException("找不到文件: " + path);
        if (_loading.Contains(full)) throw new RuntimeException("检测到循环引用: " + path);
        if (_loaded.Contains(full)) return null;
        _loaded.Add(full);
        _loading.Push(full);
        try
        {
            return Parser.ParseBlock(File.ReadAllText(full), full);
        }
        finally
        {
            _loading.Pop();
        }
    }
}
