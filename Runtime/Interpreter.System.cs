namespace Ravel.Runtime;


using System.IO;
using System.Linq;

public partial class Interpreter
{
    // ======================== 内置 ========================
    /// <summary>注册所有内置类型、方法、运算符和 System 模块（While/If/CallCC/Eval/Using 等核心函数）</summary>
    private void RegisterBuiltins()
    {
        // Class 类型注册默认 init（支持 class 作为父类）


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
        foreach (var t in new[]{RuntimeType.Object, RuntimeType.ValueType, RuntimeType.Int,
            RuntimeType.Float, RuntimeType.Bool, RuntimeType.String, RuntimeType.BigInt,
            RuntimeType.Fraction, RuntimeType.BigFraction, RuntimeType.Class, RuntimeType.Function,
            RuntimeType.List, RuntimeType.Set, RuntimeType.Dict, RuntimeType.Void, RuntimeType.Type,
            RuntimeType.Ravel, RuntimeType.Any, RuntimeType.Every, RuntimeType.Exception, RuntimeType.ScopeType, RuntimeType.Property})
            AllTypes.Add(t);
        RuntimeType.Object.DefineMethod("ToString", (s, _) => new StringVal(Show(s)));
        RuntimeType.Object.DefineMethod("Copy", (s, _) =>
        {
            switch (s)
            {
                case IntVal v: return new IntVal(v.Value);
                case FloatVal v: return new FloatVal(v.Value);
                case BoolVal v: return new BoolVal(v.Value);
                case StringVal v: return new StringVal(v.Value);
                case BigIntVal v: return new BigIntVal(v.Value);
                case FractionVal v: return new FractionVal(v.Num, v.Den);
                case BigFractionVal v: return new BigFractionVal(v.Num, v.Den);
                case ListVal v: return new ListVal([.. v.Elements]);
                case SetVal v: return new SetVal([.. v.Elements]);
                case DictVal v: return new DictVal(new Dictionary<string, RuntimeValue>(v.Entries));
                case ObjectVal v:
                    {
                        var newFields = new Dictionary<string, RuntimeValue>(v.Fields);
                        Scope? newScope = null;
                        if (v.InstanceScope != null)
                        {
                            newScope = new Scope(v.InstanceScope.Parent);
                            foreach (var kv in v.InstanceScope.Variables)
                                newScope.Define(kv.Key, kv.Value.TypeConstraint, kv.Value.Value);
                        }
                        return new ObjectVal(v.ClassType, newFields, v.Parent, newScope);
                    }
                default: return s;
            }
        });
        RuntimeType.Object.DefineMethod("Fields", (s, _) =>
        {
            var all = new List<RuntimeValue>();
            var seen = new HashSet<string>();
            // 加类型方法（含继承链）
            var t = s.Type;
            while (true)
            {
                foreach (var n in t.MethodNames)
                    if (seen.Add(n)) all.Add(new StringVal(n));
                if (t == t.Parent) break;
                t = t.Parent;
            }
            return new ListVal(all);
        });
        RuntimeType.Int.DefineMethod("ToString", (s, _) => new StringVal(((IntVal)s).Value.ToString()));
        RuntimeType.String.DefineMethod("Length", (s, _) => new IntVal(((StringVal)s).Value.Length));
        RuntimeType.List.DefineMethod("Count", (s, _) => new IntVal(((ListVal)s).Elements.Count));
        RuntimeType.List.DefineMethod("At", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.At 需要 int 参数");
            var lst = (ListVal)s; if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return lst.Elements[i.Value];
        });
        RuntimeType.List.DefineMethod("Add", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("list.Add 需要 1 个参数");
            ((ListVal)s).Elements.Add(a[0]);
            return VoidVal.Instance;
        });
        RuntimeType.List.DefineMethod("Remove", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.Remove 需要 int 参数");
            var lst = (ListVal)s; if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            var v = lst.Elements[i.Value]; lst.Elements.RemoveAt(i.Value); return v;
        });
        RuntimeType.List.DefineMethod("Insert", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.Insert 需要 int 参数");
            var lst = (ListVal)s; var idx = i.Value;
            if (idx < 0 || idx > lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("list.Insert 需要值参数");
                lst.Elements.Insert(idx, va[0]); return VoidVal.Instance;
            });
        });
        RuntimeType.List.DefineMethod("Set", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.Set 需要 int 参数");
            var lst = (ListVal)s; var idx = i.Value;
            if (idx < 0 || idx >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("list.Set 需要值参数");
                lst.Elements[idx] = va[0]; return VoidVal.Instance;
            });
        });

        // ---- Set 方法 ----
        RuntimeType.Set.DefineMethod("Count", (s, _) => new IntVal(((SetVal)s).Elements.Count));
        RuntimeType.Set.DefineMethod("Add", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Add 需要 1 个参数");
            ((SetVal)s).Elements.Add(a[0]);
            return VoidVal.Instance;
        });
        RuntimeType.Set.DefineMethod("Remove", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Remove 需要 1 个参数");
            ((SetVal)s).Elements.Remove(a[0]);
            return VoidVal.Instance;
        });
        RuntimeType.Set.DefineMethod("Contains", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Contains 需要 1 个参数");
            return new BoolVal(((SetVal)s).Elements.Contains(a[0]));
        });

        // ---- Dict 方法 ----
        RuntimeType.Dict.DefineMethod("Count", (s, _) => new IntVal(((DictVal)s).Entries.Count));
        RuntimeType.Dict.DefineMethod("Get", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal key) throw new RuntimeException("dict.Get 需要 string 参数");
            var d = (DictVal)s;
            if (d.Entries.TryGetValue(key.Value, out var v)) return v;
            throw new RuntimeException($"键不存在: {key.Value}");
        });
        RuntimeType.Dict.DefineMethod("Set", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal key) throw new RuntimeException("dict.Set 需要 string 键");
            return FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("dict.Set 需要值参数");
                ((DictVal)s).Entries[key.Value] = va[0];
                return VoidVal.Instance;
            });
        });
        RuntimeType.Dict.DefineMethod("Has", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal key) throw new RuntimeException("dict.Has 需要 string 参数");
            return new BoolVal(((DictVal)s).Entries.ContainsKey(key.Value));
        });
        RuntimeType.Dict.DefineMethod("Keys", (s, _) =>
        {
            var keys = new List<RuntimeValue>();
            foreach (var k in ((DictVal)s).Entries.Keys)
                keys.Add(new StringVal(k));
            return new ListVal(keys);
        });
        RuntimeType.Dict.DefineMethod("Values", (s, _) =>
        {
            var vals = new List<RuntimeValue>();
            foreach (var v in ((DictVal)s).Entries.Values)
                vals.Add(v);
            return new ListVal(vals);
        });

        // type 类型方法
        RuntimeType.Type.DefineMethod("name", (s, _) => new StringVal(((TypeVal)s).Value.Name));
        RuntimeType.Type.DefineMethod("Parent", (s, _) =>
        {
            var p = ((TypeVal)s).Value.Parent;
            if (p == ((TypeVal)s).Value) return s;
            return new TypeVal(p);
        });
        RuntimeType.Type.DefineMethod("Is", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not TypeVal other) throw new RuntimeException("type.Is 需要 type 参数");
            return new BoolVal(((TypeVal)s).Value.IsAssignableTo(other.Value));
        });
        RuntimeType.Type.DefineMethod("Default", (s, _) =>
        {
            var tv = (TypeVal)s;
            return EvalTypeCastDirect(tv, DefaultVal.Instance);
        });
        RuntimeType.Function.DefineMethod("Name", (s, a) =>
        {
            var fn = (FunctionVal)s;
            if (a.Length > 0 && a[0] is StringVal sv) { fn.Name = sv.Value; return VoidVal.Instance; }
            return fn.Name != null ? new StringVal(fn.Name) : VoidVal.Instance;
        });
        RuntimeType.Type.DefineMethod("Subtypes", (s, _) =>
        {
            var tv = (TypeVal)s;
            var subs = new List<RuntimeValue>();
            foreach (var t in Current!.AllTypes)
            {
                if (t != tv.Value && t.IsAssignableTo(tv.Value))
                    subs.Add(new TypeVal(t));
            }
            return new ListVal(subs);
        });

        systemModule.ModuleScope.Define("WriteLine", RuntimeType.Function, FunctionVal.FromDirect(a => { Console.WriteLine(Show(a[0])); return VoidVal.Instance; }));
        systemModule.ModuleScope.Define("Write", RuntimeType.Function, FunctionVal.FromDirect(a => { Console.Write(Show(a[0])); return VoidVal.Instance; }));
        systemModule.ModuleScope.Define("ReadLine", RuntimeType.Function, FunctionVal.FromDirect(_ => new StringVal(Console.ReadLine() ?? "")));
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
                ObjectVal ov => new ObjectVal(ov.ClassType, new Dictionary<string, RuntimeValue>(ov.Fields), ov.Parent, CopyScope(ov.InstanceScope)),
                ListVal lv => new ListVal([.. lv.Elements]),
                SetVal sv => new SetVal([.. sv.Elements]),
                DictVal dv => new DictVal(new Dictionary<string, RuntimeValue>(dv.Entries)),
                _ => obj
            };
            return ToDone(FunctionVal.FromTrampolined(ba =>
            {
                var block = ExpectBlock(ba[0], "with body");
                var saved = CurrentScope;
                if (copy is ObjectVal { InstanceScope: not null } ov2)
                    CurrentScope = ov2.InstanceScope;
                return Then(EvalBlockExec(block.Block), _ =>
                {
                    CurrentScope = saved;
                    if (copy is ObjectVal { InstanceScope: not null } ov3)
                        SyncScopeToFields(ov3.InstanceScope, ov3);
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
            foreach (var d in refs) { var p = Path.Combine(d, path); if (File.Exists(p)) { full = p; break; } }
            if (full == null) foreach (var d in refs) { var p = Path.Combine(d, path + ".rav"); if (File.Exists(p)) { full = p; break; } }
            if (full == null) return ThrowRavel("找不到文件: " + path);
            full = Path.GetFullPath(full);
            if (_loading.Contains(full)) return ThrowRavel("检测到循环引用: " + path);
            if (_loaded.Contains(full)) return ToDone(VoidVal.Instance);
            _loaded.Add(full);
            _loading.Push(full);
            var src = File.ReadAllText(full);
            var lexer = new Lexer(src); var parser = new Parser(lexer.Tokenize()); var ast = parser.Parse();
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

        // ---- Scope 方法 ----
        RuntimeType.ScopeType.DefineMethod("Push", (s, _) =>
        {
            var scope = ((ScopeVal)s).Scope;
            return new ScopeVal(scope.Push());
        });
        RuntimeType.ScopeType.DefineMethod("Define", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal name)
                throw new RuntimeException("scope.Define 需要字符串名称");
            var scope = ((ScopeVal)s).Scope;
            return FunctionVal.FromDirect(b =>
            {
                if (b.Length != 1 || b[0] is not TypeVal tv)
                    throw new RuntimeException("scope.Define 需要 type 参数");
                scope.Define(name.Value, tv.Value, VoidVal.Instance);
                return VoidVal.Instance;
            });
        });
        RuntimeType.ScopeType.DefineMethod("Lookup", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal name)
                throw new RuntimeException("scope.Lookup 需要字符串参数");
            var scope = ((ScopeVal)s).Scope;
            var vr = scope.Lookup(name.Value);
            return new PropertyVal(
                FunctionVal.FromDirect(_ => vr.Value),
                FunctionVal.FromDirect(args => { vr.Assign(args[0]); return VoidVal.Instance; }),
                [.. vr.Attrs]
            );
        });
        RuntimeType.ScopeType.DefineMethod("Variables", (s, _) =>
        {
            var scope = ((ScopeVal)s).Scope;
            var d = new Dictionary<string, RuntimeValue>();
            foreach (var kv in scope.Variables)
            {
                if (kv.Key == "this" || kv.Key == "base" || kv.Key == "block" || kv.Key == "thistype") continue;
                var vr = scope.Lookup(kv.Key);
                var getter = FunctionVal.FromDirect(_ => vr.Value);
                var setter = FunctionVal.FromDirect(a => { vr.Assign(a[0]); return VoidVal.Instance; });
                d[kv.Key] = new PropertyVal(getter, setter, [.. kv.Value.Attrs]);
            }
            return new DictVal(d);
        });

        // ---- Property 方法 ----
        RuntimeType.Property.DefineMethod("Attrs", (s, _) =>
        {
            var pv = (PropertyVal)s;
            return pv.Attrs != null
                ? new ListVal([.. pv.Attrs.Select(a => new StringVal(a))])
                : new ListVal([]);
        });

        // ---- Function 方法 ----
        RuntimeType.Function.DefineMethod("scope", (s, _) =>
        {
            var fn = (FunctionVal)s;
            return new ScopeVal(fn.Scope);
        });
        RuntimeType.Function.DefineMethod("setScope", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not ScopeVal sv) throw new RuntimeException("setScope 需要 Scope 参数");
            ((FunctionVal)s).Scope = sv.Scope;
            return VoidVal.Instance;
        });
        RuntimeType.Function.DefineMethod("prepend", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not BlockVal p) throw new RuntimeException("prepend 需要代码块参数");
            return ((FunctionVal)s).Prepend(p);
        });
        RuntimeType.Function.DefineMethod("append", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not BlockVal p) throw new RuntimeException("append 需要代码块参数");
            return ((FunctionVal)s).Append(p);
        });

        // ---- Type 方法 ----
        RuntimeType.Type.DefineMethod("Instantiate", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not ScopeVal sv) throw new RuntimeException("Instantiate 需要 Scope 参数");
            var rt = ((TypeVal)s).Value;
            var fields = new Dictionary<string, RuntimeValue>();
            foreach (var kv in sv.Scope.Variables)
            {
                if (kv.Key == "this" || kv.Key == "base" || kv.Key == "block" || kv.Key == "thistype") continue;
                fields[kv.Key] = kv.Value.Value;
            }
            return new ObjectVal(rt, fields, null, sv.Scope);
        });
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
            var lexer = new Lexer(s.Value); var tokens = lexer.Tokenize();
            var parser = new Parser(tokens); var ast = parser.Parse();
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
