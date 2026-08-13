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

        // 注册所有内置类型
        foreach (var t in new[]
                 {
                     RuntimeType.Object, RuntimeType.ValueType, RuntimeType.Int,
                     RuntimeType.Float, RuntimeType.Bool, RuntimeType.String, RuntimeType.BigInt,
                     RuntimeType.Fraction, RuntimeType.BigFraction, RuntimeType.Class, RuntimeType.Function,
                     RuntimeType.Block,
                     RuntimeType.List, RuntimeType.Set, RuntimeType.Dict, RuntimeType.Void, RuntimeType.Type,
                     RuntimeType.Ravel, RuntimeType.Any, RuntimeType.Every, RuntimeType.Exception,
                     RuntimeType.ScopeType, RuntimeType.Property
                 })
            RuntimeType.AllTypes.Add(t);

        systemModule.ModuleScope.Define("WriteLine", RuntimeType.Function, FunctionVal.FromDirect(a =>
        {
            Console.WriteLine(Show(a[0]));
            return VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("Write", RuntimeType.Function, FunctionVal.FromDirect(a =>
        {
            Console.Write(Show(a[0]));
            return VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("ReadLine", RuntimeType.Function,
            FunctionVal.FromDirect(_ => new StringVal(Console.ReadLine() ?? "")));
        systemModule.ModuleScope.Define("True", RuntimeType.Bool, new BoolVal(true));
        systemModule.ModuleScope.Define("False", RuntimeType.Bool, new BoolVal(false));
        systemModule.ModuleScope.Define("Default", RuntimeType.Every, DefaultVal.Instance);
        systemModule.ModuleScope.Define("While", RuntimeType.Function, FunctionVal.FromTrampolined(EvalWhile));
        systemModule.ModuleScope.Define("If", RuntimeType.Function, FunctionVal.FromTrampolined(EvalIf));
        systemModule.ModuleScope.Define("CallCC", RuntimeType.Function, FunctionVal.FromTrampolined(EvalCallCC));
        systemModule.ModuleScope.Define("ValueTypeVal", RuntimeType.Type, new TypeVal(RuntimeType.ValueType));
        systemModule.ModuleScope.Define("TypeOf", RuntimeType.Function, FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1) throw new RuntimeException("typeof 需要 1 个参数");
            return ToDone(new TypeVal(a[0].Type));
        }));
        systemModule.ModuleScope.Define("RandInt", RuntimeType.Function, FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1 || a[0] is not IntVal lo) throw new RuntimeException("randint 需要 int 参数(最小值)");
            return ToDone(FunctionVal.FromTrampolined(b =>
            {
                if (b.Length != 1 || b[0] is not IntVal hi) throw new RuntimeException("randint 需要 int 参数(最大值)");
                return ToDone(new IntVal(Random.Shared.Next(lo.Value, hi.Value)));
            }));
        }));
        systemModule.ModuleScope.Define("With", RuntimeType.Function, FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1) return ThrowRavel("with 需要 1 个参数");
            var obj = a[0];
            var copy = obj switch
            {
                ObjectVal ov => new ObjectVal(ov.ClassType, RuntimeType.CopyScope(ov.Scope)),
                ListVal lv => new ListVal([.. lv.Elements]),
                SetVal sv => new SetVal([.. sv.Elements]),
                DictVal dv => new DictVal(new Dictionary<string, RuntimeValue>(dv.Entries)),
                _ => obj
            };
            return ToDone(FunctionVal.FromTrampolined(ba =>
            {
                var fn = ExpectFunc(ba[0], "with body");
                var savedScope = fn.Scope;
                if (copy is ObjectVal ov2)
                {
                    fn.Scope = ov2.Scope;
                }

                return Then(fn.Trampolined([VoidVal.Instance]), _ =>
                {
                    fn.Scope = savedScope;
                    return ToDone(copy);
                });
            }));
        }));
        systemModule.ModuleScope.Define("RavelMod", RuntimeType.Function, FunctionVal.FromDirect(a =>
        {
            var name = ((StringVal)a[0]).Value;
            if (!_modules.TryGetValue(name, out var mv))
            {
                var mt = RuntimeType.Define(name, RuntimeType.Ravel);
                mv = new ModuleVal(mt, new Scope(_global));
                _modules[name] = mv;
                _global.Define(name, mt, mv);
            }

            CurrentScope = mv.ModuleScope;
            return VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("Using", RuntimeType.Function, FunctionVal.FromTrampolined(a =>
        {
            var path = ((StringVal)a[0]).Value;
            var refs = new List<string> { "/workspace/ravel/lib/", "/workspace/ravel/", "./", "lib/" };
            var rv = _global.TryLookup("references");
            if (rv?.Value is ListVal lv) refs = [.. lv.Elements.Select(e => ((StringVal)e).Value), .. refs];
            string? full = null;
            foreach (var d in refs)
            {
                var p = Path.Combine(d, path);
                if (File.Exists(p))
                {
                    full = p;
                    break;
                }
            }

            if (full == null)
                foreach (var d in refs)
                {
                    var p = Path.Combine(d, path + ".rav");
                    if (File.Exists(p))
                    {
                        full = p;
                        break;
                    }
                }

            if (full == null) return ThrowRavel("找不到文件: " + path);
            full = Path.GetFullPath(full);
            if (_loading.Contains(full)) return ThrowRavel("检测到循环引用: " + path);
            if (_loaded.Contains(full)) return ToDone(VoidVal.Instance);
            _loaded.Add(full);
            _loading.Push(full);
            var src = File.ReadAllText(full);
            var lexer = new Lexer(src);
            var parser = new Parser(lexer.Tokenize());
            var ast = parser.Parse();
            var savedScope = CurrentScope;
            try
            {
                return EvalBlockStmts(ast.Statements, 0, VoidVal.Instance, () => { });
            }
            finally
            {
                _loading.Pop();
                CurrentScope = savedScope;
            }
        }));

        // ---- 内置函数 ----
        systemModule.ModuleScope.Define("unsafe", RuntimeType.Function, FunctionVal.FromDirect(_ =>
        {
            Current!.UnsafeDepth++;
            return VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("property", RuntimeType.Function, FunctionVal.FromDirect(a =>
        {
            if (a.Length != 1 || a[0] is not FunctionVal g) throw new RuntimeException("property 需要 getter 函数");
            return FunctionVal.FromDirect(b =>
            {
                if (b.Length != 1 || b[0] is not FunctionVal s) throw new RuntimeException("property 需要 setter 函数");
                return new PropertyVal(g, s);
            });
        }));
        systemModule.ModuleScope.Define("Foreach", RuntimeType.Function, FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1 || a[0] is not ListVal lst)
                throw new RuntimeException("Foreach 需要 list 参数");
            return ToDone(FunctionVal.FromTrampolined(b =>
            {
                if (b.Length != 1 || b[0] is not FunctionVal fn)
                    throw new RuntimeException("Foreach 需要函数参数");
                RuntimeValue last = VoidVal.Instance;
                foreach (var item in lst.Elements)
                {
                    var s = fn.Trampolined([item]);
                    last = Step.Run(s);
                }

                return ToDone(last);
            }));
        }));
        systemModule.ModuleScope.Define("currentScope", RuntimeType.Function, FunctionVal.FromDirect(_ =>
            new ScopeVal(Current!.CurrentScope)));
        systemModule.ModuleScope.Define("Eval", RuntimeType.Function, FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1 || a[0] is not StringVal s) return ThrowRavel("eval expects string");
            var lexer = new Lexer(s.Value);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var ast = parser.Parse();
            return EvalBlockStmts(ast.Statements, 0, VoidVal.Instance, () => { });
        }));
        systemModule.ModuleScope.Define("Assert", RuntimeType.Function, FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1 || a[0] is not BoolVal cond)
                return ThrowRavel("assert 需要 bool 参数");
            var ok = cond.Value;
            return ToDone(FunctionVal.FromDirect(ma =>
            {
                var msg = ma.Length > 0 && ma[0] is StringVal s ? s.Value : "assertion failed: " + Show(cond);
                if (!ok) throw new RuntimeException(msg);
                return VoidVal.Instance;
            }));
        }));
        systemModule.ModuleScope.Define("Exit", RuntimeType.Function, FunctionVal.FromDirect(a =>
        {
            var msg = a.Length > 0 ? ((StringVal)a[0]).Value : "";
            throw new ExitException(msg);
        }));
    }
}
