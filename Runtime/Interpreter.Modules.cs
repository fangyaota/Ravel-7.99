namespace Ravel.Runtime;

/// <summary>模块文件加载:路径搜索、循环引用检测、已加载去重。
/// 搜索目录 = `references` 里的用户目录(优先) + <see cref="ModuleSearchPath.Defaults"/>。</summary>
public partial class Interpreter
{
    /// <summary>把模块名解析为绝对路径;找不到返回 null</summary>
    private string? ResolveModulePath(string path)
    {
        var refs = new List<string>();
        var rv = _global.TryLookup("References");
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
    /// 这也是 `tests/95_circular_ref.rav` 一直测不到东西的原因(它当时连文件都找不到)。</summary>
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
            PluginLoader.Load(this, System.Reflection.Assembly.LoadFrom(full), _global, _modules);
            return null;
        }

        return Parser.ParseBlock(File.ReadAllText(full), full);
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
            SetAmbientScope(_global);
            return VoidVal.Instance;
        }

        if (!_modules.TryGetValue(name, out var mv))
        {
            var mt = BuiltinClasses.NewModuleClass(name, BuiltinClasses.Ravel);
            mv = new ModuleVal(mt, new Scope(_global));
            _modules[name] = mv;
            _global.Define(name, mt, mv);
        }

        SetAmbientScope(mv.Scope);
        return VoidVal.Instance;
    }
}
