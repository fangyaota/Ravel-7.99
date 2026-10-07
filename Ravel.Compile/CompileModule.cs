using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Ravel.Runtime;

/// <summary>**`Compile` 模块** —— 把一段 C# 编成 dll，Roslyn **当库**调。
///
///     using "plugins/Ravel.Compile.dll"
///     cs := "namespace Ravel.Runtime; …"          # 一个插件形状的 C# 源码
///     Compile.Cs cs "out/plugins/NMath.dll"
///
/// 存在的理由：`dotnet build` 那条路要求机器上**有 SDK**，而 `out/` 是
/// framework-dependent 发布 —— 只有 runtime 的机器上根本没有 `dotnet build`。
/// 生成器要在那儿把「`.rav` → `.cs` → `.dll`」走完，编译器就得带在身边。
///
/// **引用集是从当前进程取的**，这条比"生成一个 csproj 再 restore"干净得多：
///
/// * 框架那一套 → `TRUSTED_PLATFORM_ASSEMBLIES`（跑着的这个 runtime 的完整清单）
/// * 引擎自己 → `typeof(RuntimeValue).Assembly.Location`
/// * 要适配的第三方库 → 调用方从第三个参数传（**它多半已经在进程里了**：
///   生成器刚用 `Reflect.Type` 把它加载过，`Assembly.Location` 现成）
///
/// 于是**编出来的 dll 引用的就是跑着的那个 `Ravel.dll`** —— 版本天然对齐，
/// 而 `dotnet build` 那条路反而没这个保证（它编的可能指向另一份副本）。</summary>
[RavelModule("Compile")]
internal static class CompileModule
{
    /// <summary>源码 → dll。编不过**当场把诊断说出来**（带文件、行、列）。</summary>
    [RavelFn("Cs")]
    public static RuntimeValue Cs(RuntimeValue source, RuntimeValue outPath, RuntimeValue extraRefs)
    {
        var src  = PluginKit.Text(source, "Compile.Cs 的源码").Value;
        var outp = PluginKit.PathOf(outPath, "Compile.Cs 的输出路径");
        var name = Path.GetFileNameWithoutExtension(outp);

        var tree = CSharpSyntaxTree.ParseText(src);
        var opts = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithOptimizationLevel(OptimizationLevel.Release)
            .WithDeterministic(true);            // 同样的源 → 同样的字节

        var comp = CSharpCompilation.Create(name, [tree], References(extraRefs), opts);

        PluginKit.NeedParentDir(outp, "Compile.Cs 的输出路径");

        EmitResult result;
        using (var fs = File.Create(outp))
        {
            try { result = comp.Emit(fs); }
            catch (Exception ex)
            {
                throw PluginKit.Fail($"编译 {name} 的时候炸了：{ex.GetType().Name}: {ex.Message}");
            }
        }

        if (!result.Success)
        {
            // **只要 Error**：Warning 不该拦住"编出来能用"这件事。
            var errs = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => "  " + d);
            throw PluginKit.Fail($"{name} 没编过：\n" + string.Join("\n", errs),
                                 ErrorKind.Value);
        }

        return PluginKit.Void;
    }

    /// <summary>编译要引用的那一批程序集。**按文件名去重，先到的赢** ——
    /// 框架清单排在最前，所以它那一份是权威。</summary>
    private static List<MetadataReference> References(RuntimeValue extraRefs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<MetadataReference>();

        void Add(string? loc)
        {
            if (string.IsNullOrEmpty(loc) || !File.Exists(loc)) return;
            if (!seen.Add(Path.GetFileName(loc))) return;
            list.Add(MetadataReference.CreateFromFile(loc));
        }

        // 框架那一套。拿不到就退回"当前进程加载过的那些" —— 老 runtime 上没这个键。
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            foreach (var p in tpa.Split(Path.PathSeparator)) Add(p);
        else
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) Add(a.Location);

        // 引擎自己（生成的代码要 `PluginKit` / `IntVal` / `RuntimeValue`）
        Add(typeof(RuntimeValue).Assembly.Location);

        // 要适配的第三方库：调用方给
        foreach (var e in PluginKit.Elements(extraRefs, "Compile.Cs 的额外引用"))
            Add(PluginKit.PathOf(e, "Compile.Cs 的额外引用"));

        return list;
    }
}
