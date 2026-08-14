namespace Ravel.Runtime;

using System.IO;
using System.Linq;

public partial class Interpreter
{
    // ======================== 内置 ========================
    /// <summary>注册内置类型、运算符和 System 模块（While/If/CallCC/Eval/Using 等核心函数）</summary>
    private void RegisterBuiltins()
    {
        // System 内置模块
        var systemModuleType = RuntimeType.Define("System", RuntimeType.Ravel);
        var systemModule = new ModuleVal(systemModuleType, new Scope(_global));
        systemModule.ModuleScope.Define("Integer", RuntimeType.Type, new TypeVal(RuntimeType.Int));
        systemModule.ModuleScope.Define("String", RuntimeType.Type, new TypeVal(RuntimeType.String));
        systemModule.ModuleScope.Define("Bool", RuntimeType.Type, new TypeVal(RuntimeType.Bool));
        systemModule.ModuleScope.Define("Float", RuntimeType.Type, new TypeVal(RuntimeType.Float));
        systemModule.ModuleScope.Define("BigInteger", RuntimeType.Type, new TypeVal(RuntimeType.BigInt));
        systemModule.ModuleScope.Define("Fraction", RuntimeType.Type, new TypeVal(RuntimeType.Fraction));
        systemModule.ModuleScope.Define("BigFraction", RuntimeType.Type, new TypeVal(RuntimeType.BigFraction));
        systemModule.ModuleScope.Define("List", RuntimeType.Type, new TypeVal(RuntimeType.List));
        systemModule.ModuleScope.Define("Set", RuntimeType.Type, new TypeVal(RuntimeType.Set));
        systemModule.ModuleScope.Define("Dict", RuntimeType.Type, new TypeVal(RuntimeType.Dict));
        systemModule.ModuleScope.Define("Object", RuntimeType.Type, new TypeVal(RuntimeType.Object));
        systemModule.ModuleScope.Define("Function", RuntimeType.Type, new TypeVal(RuntimeType.Function));
        systemModule.ModuleScope.Define("Void", RuntimeType.Type, new TypeVal(RuntimeType.Void));
        systemModule.ModuleScope.Define("AnyType", RuntimeType.Type, new TypeVal(RuntimeType.Any));
        systemModule.ModuleScope.Define("EveryType", RuntimeType.Type, new TypeVal(RuntimeType.Every));
        systemModule.ModuleScope.Define("ExceptionType", RuntimeType.Type, new TypeVal(RuntimeType.Exception));
        systemModule.ModuleScope.Define("Class", RuntimeType.Type, new TypeVal(RuntimeType.Class));
        systemModule.ModuleScope.Define("Type", RuntimeType.Type, new TypeVal(RuntimeType.Type));
        _modules["System"] = systemModule;
        _global.Define("System", systemModuleType, systemModule);

        systemModule.ModuleScope.Define("WriteLine", RuntimeType.Function, FunctionVal.FromDirect(a =>
        {
            Console.WriteLine(Show(a));
            return VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("Write", RuntimeType.Function, FunctionVal.FromDirect(a =>
        {
            Console.Write(Show(a));
            return VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("ReadLine", RuntimeType.Function,
            FunctionVal.FromDirect(_ => new StringVal(Console.ReadLine() ?? "")));
        systemModule.ModuleScope.Define("True", RuntimeType.Bool, new BoolVal(true));
        systemModule.ModuleScope.Define("False", RuntimeType.Bool, new BoolVal(false));
        systemModule.ModuleScope.Define("Default", RuntimeType.Every, DefaultVal.Instance);
        systemModule.ModuleScope.Define("While", RuntimeType.Function, new ControlFunction(ControlKind.While, 2, RList<RuntimeValue>.Empty));
        systemModule.ModuleScope.Define("If", RuntimeType.Function, new ControlFunction(ControlKind.If, 3, RList<RuntimeValue>.Empty));
        systemModule.ModuleScope.Define("CallCC", RuntimeType.Function, new ControlFunction(ControlKind.CallCC, 1, RList<RuntimeValue>.Empty));
        systemModule.ModuleScope.Define("ValueTypeVal", RuntimeType.Type, new TypeVal(RuntimeType.ValueType));
        systemModule.ModuleScope.Define("TypeOf", RuntimeType.Function, FunctionVal.FromTrampolined(a =>
            ToDone(new TypeVal(a.Type))));
        systemModule.ModuleScope.Define("RandInt", RuntimeType.Function, FunctionVal.FromTrampolined2((lo, hi) =>
        {
            if (lo is not IntVal l) throw new RuntimeException("randint 需要 int 参数(最小值)");
            if (hi is not IntVal h) throw new RuntimeException("randint 需要 int 参数(最大值)");
            return ToDone(new IntVal(Random.Shared.Next(l.Value, h.Value)));
        }));
        systemModule.ModuleScope.Define("With", RuntimeType.Function, new ControlFunction(ControlKind.With, 2, RList<RuntimeValue>.Empty));
        systemModule.ModuleScope.Define("RavelMod", RuntimeType.Function, FunctionVal.FromDirect(a =>
        {
            var name = ((StringVal)a).Value;
            if (!_modules.TryGetValue(name, out var mv))
            {
                var mt = RuntimeType.Define(name, RuntimeType.Ravel);
                mv = new ModuleVal(mt, new Scope(_global));
                _modules[name] = mv;
                _global.Define(name, mt, mv);
            }

            SetAmbientScope(mv.ModuleScope);
            return VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("Using", RuntimeType.Function, new ControlFunction(ControlKind.Using, 1, RList<RuntimeValue>.Empty));

        // ---- 内置函数 ----
        systemModule.ModuleScope.Define("unsafe", RuntimeType.Function, FunctionVal.FromDirect(_ =>
        {
            Current!.UnsafeDepth++;
            return VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("property", RuntimeType.Function, FunctionVal.FromTrampolined2((g, s) =>
        {
            if (g is not FunctionVal gf) throw new RuntimeException("property 需要 getter 函数");
            if (s is not FunctionVal sf) throw new RuntimeException("property 需要 setter 函数");
            return ToDone(new PropertyVal(gf, sf));
        }));
        systemModule.ModuleScope.Define("Foreach", RuntimeType.Function, new ControlFunction(ControlKind.Foreach, 2, RList<RuntimeValue>.Empty));
        systemModule.ModuleScope.Define("currentScope", RuntimeType.Function, FunctionVal.FromDirect(_ =>
            new ScopeVal(Current!.CurrentScope)));
        systemModule.ModuleScope.Define("Eval", RuntimeType.Function, new ControlFunction(ControlKind.Eval, 1, RList<RuntimeValue>.Empty));
        systemModule.ModuleScope.Define("Assert", RuntimeType.Function, FunctionVal.FromTrampolined2((cond, msg) =>
        {
            if (cond is not BoolVal b) throw new RuntimeException("assert 需要 bool 参数");
            var ok = b.Value;
            var m = msg is StringVal s ? s.Value : "assertion failed: " + Show(cond);
            if (!ok) throw new RuntimeException(m);
            return ToDone(VoidVal.Instance);
        }));
        systemModule.ModuleScope.Define("Exit", RuntimeType.Function, FunctionVal.FromDirect(a =>
        {
            var msg = a is StringVal s ? s.Value : "";
            throw new ExitException(msg);
        }));
    }
}
