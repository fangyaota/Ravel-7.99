using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Ravel.Runtime;

[RavelModule("Compile")]
internal static class CompileModule
{
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
