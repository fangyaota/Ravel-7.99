namespace Ravel.Runtime;

public partial class Interpreter
{
    private void RecordReference(string path)
    {
        // **正在加载的那个文件优先** —— 模块体里再 `using` 一个文件时,那个文件的 `using`
        // 属于它自己,不属于外层刚 `ravel` 过的模块。
        if (_loading.Count > 0 && _refsByPath.TryGetValue(_loading.Peek(), out var list))
        {
            list.Add(path);
            return;
        }
        // 没有文件在加载 = 主脚本:记给当前挂着的那个模块(`ravel "M"` … `ravel ""` 之间)
        (_currentModule ?? GlobalModule()).ReferencePaths.Add(path);
    }

    internal IReadOnlyList<ModuleVal> ResolveReferences(ModuleVal mv)
    {
        var found = new List<ModuleVal>();
        foreach (var p in mv.ReferencePaths)
        {
            var hits = FindModuleFiles(p);
            found.Add(hits.Count == 1 && _moduleByPath.TryGetValue(hits[0].Full, out var m)
                ? m : GlobalModule());
        }

        return found;
    }

    private ModuleVal GlobalModule()
        => _globalModule ??= new ModuleVal("<global>", _global) { Owner = this };

    private List<(string Written, string Full)> FindModuleFiles(string path)
    {
        var refs = new List<string>();
        var rv = _global.TryLookup("ReferencesPath");
        if (rv?.Value is ListVal lv) refs.AddRange(lv.Elements.Select(e => As<StringVal>(e, "references 的元素").Value));
        refs.AddRange(ModuleSearchPath.Defaults);

        var hits = new List<(string Written, string Full)>();
        foreach (var candidate in new[] { path, path + ".rav", path + ".dll" })
            foreach (var d in refs)
            {
                var p = Path.Combine(d, candidate);
                if (!File.Exists(p)) continue;
                hits.Add((candidate, Path.GetFullPath(p)));
                break;   // 这一种写法在第一个搜到的目录上收工,接着看下一种
            }

        return hits;
    }

    private string? ResolveModulePath(string path)
    {
        var hits = FindModuleFiles(path);
        if (hits.Count == 0) return null;
        if (hits.Count == 1) return hits[0].Full;

        throw new RuntimeException(
            $"`using \"{path}\"` 有歧义:{string.Join(" ", hits.Select(h => "`" + h.Written + "`"))} 都对得上"
            + " —— 把后缀写全再试", ErrorKind.Io);
    }

    private BlockExpr? LoadModuleAst(string path)
    {
        var full = ResolveModulePath(path);
        if (full == null) throw new RuntimeException("找不到文件: " + path, ErrorKind.Io);
        if (_loading.Contains(full)) throw new RuntimeException("检测到循环引用: " + path);
        if (_loaded.Contains(full)) return null;
        _loaded.Add(full);

        // **插件 dll**:`using "plugins/x.dll"` —— 读进来、扫一遍(见 PluginLoader),
        // 然后交回 null(它没有 Ravel 代码要跑,和"已经加载过"一个待遇)。
        if (full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            // 这个 dll 装进来的模块**不进 `_moduleByPath`** —— 路径对不上"一个"模块:
            // `Ravel.Extensions.dll` 一次装进 `Native` 和 `Math` 两个,挑哪个都是瞎猜。
            // 而模块本来就有自己的来路:`Math` 由 `lib/math.rav` 那句 `ravel "Math"` 认领,
            // 装它的那个 `lib/native.rav` 自己没写模块名,于是它算 `<global>` 那一枚
            // (见 `GlobalModule`)—— 和"没写模块名的文件"一条规矩。
            PluginLoader.Load(this, LoadPluginAssembly(full), _global, _modules);
            return null;
        }

        return ParseLibBlock(full, MoreControlFlow);
    }

    private static System.Reflection.Assembly LoadPluginAssembly(string full)
    {
        var name = Path.GetFileNameWithoutExtension(full);
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                return loaded;

        return System.Reflection.Assembly.LoadFrom(full);
    }

    internal RuntimeValue EnterModule(string name)
    {
        // 空名字特判成「回全局」。不特判的话会建出一个**名字叫 "" 的模块**,
        // 后面的定义全落在那儿;而模块作用域只挂到 _global 上,所以那些定义
        // 从别的模块根本看不见:`ravel ""` 之后 `b := 2`,再 `ravel "A"` 就读不到 b。
        if (name.Length == 0)
        {
            // 只有**顶层**(主脚本)的 `ravel ""` 才动这个游标。文件里的 `ravel ""`
            // 是那个文件自己的事(`lib/io.rav` / `lib/keys.rav` 都以它收尾),
            // 不该把主脚本正挂着的那个模块清掉。
            if (_loading.Count == 0) _currentModule = null;
            SetAmbientScope(_global);
            return VoidVal.Instance;
        }

        if (!_modules.TryGetValue(name, out var mv))
        {
            // 父作用域是**此刻这一块**的,不是硬挂全局。一个文件常在 `ravel "X"` **之前**
            // 先摆几个名字(`io.rav` 先写 `IFile`、`IDir`,再 `ravel "Io"`,最后
            // `impl (IFile File { … })` 把它们接到类上)—— 那些名字落在"这个文件跑在哪个
            // 作用域里",而 X 得看得见它们,不然那句 `impl` 会报「未定义的变量 'IFile'」,
            // 而它明明就在上几行定义过。从顶层 `using` 时这一块就是全局,和从前一模一样;
            // 只有"**从模块里** `using` 一个模块文件"那条路不一样 —— 那条从前是坏的。
            mv = ModuleVal.WithOwnTable(name, AmbientScope(), this);
            _modules[name] = mv;
            _global.Define(name, mv);
        }

        // 引擎塞给模块的两样(和"成员"无关),放在 `if` **外面**:模块可能是**别的来路
        // 先建好**的(`Math` 由扩展 dll 建出来,`lib/math.rav` 再 `ravel "Math"` 认领),
        // 那也要接得上。
        mv.Owner = this;
        if (_loading.Count > 0)
        {
            // 把这个文件**已经攒下的那些 `using`** 接给模块(`ravel "M"` 常常写在它们后面);
            // 此后新来的 `using` 往同一个列表里加。
            mv.ReferencePaths = _refsByPath.TryGetValue(_loading.Peek(), out var acc) ? acc : [];
            // 这个文件装出来的模块就是**它** —— `References ()` 回查的就是这一条。
            // 一个文件里 `ravel` 好几回的(io.rav 那种 `ravel "Io"` … `ravel ""`),
            // 认最前面那个:路径能对上的"就是这个文件的模块"。
            _moduleByPath.TryAdd(_loading.Peek(), mv);
        }

        // 这一句之后的 `using` 记给谁:**顶层**的才记给刚 `ravel` 的这个模块;
        // 文件里跑的那些不动它 —— 那个文件自己的 `using` 归它自己(`_refsByPath` 那条),
        // 而它跑完回到调用方之后,调用方要接着用的是原来那个。
        // (从前不分这两处,于是 `lib/repl.rav` 这种**以 `ravel "Repl"` 收尾、没有
        //  `ravel ""`** 的文件一跑完,游标就停在 `Repl` 上:主脚本之后每一条 `using`
        //  都记到了 `Repl` 头上。)
        if (_loading.Count == 0) _currentModule = mv;
        SetAmbientScope(mv.Scope);
        return VoidVal.Instance;
    }
}
