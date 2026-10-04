namespace Ravel.Runtime;

/// <summary>模块文件加载:路径搜索、循环引用检测、已加载去重。
/// 搜索目录 = `ReferencesPath` 里的用户目录(优先) + <see cref="ModuleSearchPath.Defaults"/>。</summary>
public partial class Interpreter
{
    /// <summary>`using` 被执行的那一刻,把它记进**当前正在跑的那个模块**。没有命名模块在跑
    /// (主脚本顶层、`ravel ""` 之后)就记给 `<global>` 那一枚 —— 那些名字本来就落在全局。
    ///
    /// 记的是**写的那一串**(`"seqs.rav"`),不是解析后的绝对路径 —— `MyMod.References ()`
    /// 要回答的是"我这个模块引了谁",那是个源码上的关系。存路径而不是存模块,是因为
    /// 记下的**这一瞬间**目标还没跑、模块还没建出来(见 <see cref="ResolveReferences"/>)。</summary>
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

    /// <summary>把一个模块那串 `using` **解析成模块** —— 查"文件 → 它装出来的那个模块"。
    /// 那个文件要是没写模块名(`native.rav` / `enum.rav` 这种只往全局落东西的),
    /// 一律算到 <see cref="GlobalModule"/> 那一枚上:它们落的就是同一个地方。
    /// 文件找不到(改过名、删了)也照此办理,不给空位。
    ///
    /// **这里不能抛** —— 这一条路是"回看这个模块引了谁"(`References ()`),是**读**不是加载。
    /// 所以有歧义时也当"没解析出来"(见 <see cref="FindModuleFiles"/>):由 `using` 那边报错,
    /// 在这儿炸等于 `MyMod.References ()` 也会炸。</summary>
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

    /// <summary>**没写模块名的那些文件**共用的那一枚模块,包的是全局作用域。
    /// 主脚本顶层那些 `using` 也记在它身上 —— 那也是全局这一层引的。
    /// 它不进 `_modules`:它没有模块名,不该被 `ravel "<global>"` 找得到。</summary>
    private ModuleVal GlobalModule()
        => _globalModule ??= new ModuleVal("<global>", _global) { Owner = this };

    /// <summary>`using` 后面那一串能对上哪些文件。**每种写法挑第一个搜到的目录** ——
    /// 同名文件在两个搜索目录里都有不算歧义,先到先得,那是搜索路径本来该有的意思
    /// (和 `PATH` 一样)。
    ///
    /// 写法有三种:**写全的**、补 `.rav` 的、补 `.dll` 的 —— 于是后缀可以省:
    ///
    ///     using "seqs"              # → lib/seqs.rav
    ///     using "Ravel.Extensions"  # → plugins/Ravel.Extensions.dll
    ///
    /// 交回**所有命中的**(写的那一串 + 绝对路径),而不是挑一个 —— "两种写法都对上"
    /// 该怎么办得由调用方定:`using` 那边当场报错,`References ()` 那边当没解析出来。
    /// (dll 能只写名字,是因为 `plugins/` 也在搜索目录里,见 `ModuleSearchPath`。)</summary>
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

    /// <summary>把模块名解析为绝对路径;找不到返回 null,**两种写法都对上就报错**。
    ///
    /// 报错而不是挑一个:`x.rav` 和 `x.dll` 是完全不同的两样东西(一份源码、一个程序集),
    /// 挑谁都是瞎猜 —— 所以"不歧义"是能省后缀的前提。反过来,能省后缀也正是为了
    /// **不用记住自己在写 `.rav` 还是 `.dll`**(那本来就是个实现细节:`Ravel.Extensions.dll`
    /// 底下装的是 `Math` / `Native`,和 `lib/` 里的模块一样是个模块)。
    ///
    /// 报错信息里写的是**两种写法的样子**(`x.rav` / `x.dll`),不是绝对路径:要办的事是
    /// "把后缀写全",摆两个长路径反而看不清;顺带让它钉得住(用例里没有机器相关的串)。</summary>
    private string? ResolveModulePath(string path)
    {
        var hits = FindModuleFiles(path);
        if (hits.Count == 0) return null;
        if (hits.Count == 1) return hits[0].Full;

        throw new RuntimeException(
            $"`using \"{path}\"` 有歧义:{string.Join(" ", hits.Select(h => "`" + h.Written + "`"))} 都对得上"
            + " —— 把后缀写全再试", ErrorKind.Io);
    }

    /// <summary>加载模块文件,解析为 BlockExpr;找不到抛异常,循环引用抛异常,已加载返回 null(视为空块)。
    ///
    /// **`.dll` 那一条是插件**:不是 Ravel 源码,而是一个编译好的扩展程序集 ——
    /// 扫里面带 `[RavelModule]` / `[RavelFn]` / `[RavelClass]` 的类与函数,按特性上那个名字
    /// 填进模块(见 `Runtime/Builtins/PluginApi.cs`)。它没有代码要跑,所以也交回 null。
    ///
    /// `_loading` 记的是「**模块体正在执行**」的集合,由调用方(StepUsing)在推块帧前进、块帧跑完后退。
    /// 从前 push/pop 都圈在 ParseBlock 外面——而解析一个文件时不会去解析另一个文件
    /// (`using` 是运行时构造),于是这个集合最多只有一个元素,`Contains` 永远为假:
    /// 循环引用检测是死代码,A→B→A 会静默地什么都不做,拿到的可能是只跑了一半的模块。
    /// 这也是 `tests/module/95_circular_ref.rav` 一直测不到东西的原因(它当时连文件都找不到)。</summary>
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
            PluginLoader.Load(this, System.Reflection.Assembly.LoadFrom(full), _global, _modules);
            return null;
        }

        var text = File.ReadAllText(full);
        return Parser.ParseBlock(text, full, MoreControlFlow || Parser.DeclaresMoreControlFlow(text));
    }

    /// <summary>ravel "M":切换到命名模块的作用域(首次访问时创建),后续语句落在该模块里。
    /// `ravel ""` 是**回全局作用域**——文档就是这么用的(`ravel "MyMath"` … `ravel ""` … 引用它)。
    ///
    /// 这个函数**只管建模块、切作用域**。从前它多一件事:模块建好之后还去查一张
    /// `CSharpModules` 表、把"C# 造的成员"填进去(`Math` 是最后一个用户)——
    /// 那是"模块成员是 C# 写的"的**另一条路**,和现在这条(`using "x.dll"` 扫特性)
    /// 只是历史先后不同。`Math` 搬进官方扩展之后那条路没人走了,表和查表一起删掉。</summary>
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
            _global.Define(name, BuiltinClasses.Ravel, mv);
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
