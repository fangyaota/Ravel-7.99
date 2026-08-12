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
        var systemModule = new RuntimeValue.ModuleVal(systemModuleType, new Scope(_global));
        systemModule.ModuleScope.Define("Integer", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Int));
        systemModule.ModuleScope.Define("String", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.String));
        systemModule.ModuleScope.Define("Bool", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Bool));
        systemModule.ModuleScope.Define("Float", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Float));
        systemModule.ModuleScope.Define("BigInteger", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.BigInt));
        systemModule.ModuleScope.Define("Fraction", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Fraction));
        systemModule.ModuleScope.Define("BigFraction", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.BigFraction));
        systemModule.ModuleScope.Define("List", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.List));
        systemModule.ModuleScope.Define("Set", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Set));
        systemModule.ModuleScope.Define("Dict", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Dict));
        systemModule.ModuleScope.Define("Object", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Object));
        systemModule.ModuleScope.Define("Function", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Function));
        systemModule.ModuleScope.Define("Void", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Void));
        systemModule.ModuleScope.Define("AnyType", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Any));
        systemModule.ModuleScope.Define("EveryType", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Every));
        systemModule.ModuleScope.Define("ExceptionType", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Exception));
        systemModule.ModuleScope.Define("Class", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Class));
        systemModule.ModuleScope.Define("Type", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Type));
        _modules["System"] = systemModule;
        _global.Define("System", systemModuleType, systemModule);

        // 注册所有内置类型
        foreach (var t in new[]{RuntimeType.Object, RuntimeType.ValueType, RuntimeType.Int,
            RuntimeType.Float, RuntimeType.Bool, RuntimeType.String, RuntimeType.BigInt,
            RuntimeType.Fraction, RuntimeType.BigFraction, RuntimeType.Class, RuntimeType.Function,
            RuntimeType.List, RuntimeType.Set, RuntimeType.Dict, RuntimeType.Void, RuntimeType.Type,
            RuntimeType.Ravel, RuntimeType.Any, RuntimeType.Every, RuntimeType.Exception, RuntimeType.ScopeType, RuntimeType.Property})
            AllTypes.Add(t);
        RuntimeType.Object.DefineMethod("ToString", (s, _) => new RuntimeValue.StringVal(Show(s)));
        RuntimeType.Object.DefineMethod("Copy", (s, _) =>
        {
            switch (s)
            {
                case RuntimeValue.IntVal v: return new RuntimeValue.IntVal(v.Value);
                case RuntimeValue.FloatVal v: return new RuntimeValue.FloatVal(v.Value);
                case RuntimeValue.BoolVal v: return new RuntimeValue.BoolVal(v.Value);
                case RuntimeValue.StringVal v: return new RuntimeValue.StringVal(v.Value);
                case RuntimeValue.BigIntVal v: return new RuntimeValue.BigIntVal(v.Value);
                case RuntimeValue.FractionVal v: return new RuntimeValue.FractionVal(v.Num, v.Den);
                case RuntimeValue.BigFractionVal v: return new RuntimeValue.BigFractionVal(v.Num, v.Den);
                case RuntimeValue.ListVal v: return new RuntimeValue.ListVal([.. v.Elements]);
                case RuntimeValue.SetVal v: return new RuntimeValue.SetVal([.. v.Elements]);
                case RuntimeValue.DictVal v: return new RuntimeValue.DictVal(new Dictionary<string, RuntimeValue>(v.Entries));
                case RuntimeValue.ObjectVal v:
                    {
                        var newFields = new Dictionary<string, RuntimeValue>(v.Fields);
                        Scope? newScope = null;
                        if (v.InstanceScope != null)
                        {
                            newScope = new Scope(v.InstanceScope.Parent);
                            foreach (var kv in v.InstanceScope.Variables)
                                newScope.Define(kv.Key, kv.Value.TypeConstraint, kv.Value.Value);
                        }
                        return new RuntimeValue.ObjectVal(v.ClassType, newFields, v.Parent, v.Meta, newScope);
                    }
                default: return s;
            }
        });
        RuntimeType.Object.DefineMethod("Fields", (s, _) =>
        {
            var all = new List<RuntimeValue>();
            var seen = new HashSet<string>();
            // 类实例：加字段
            if (s is RuntimeValue.ObjectVal { Meta: not null } ov)
            {
                var m = ov.Meta;
                while (m != null)
                {
                    if (m.ClassScope != null)
                    {
                        foreach (var kv in m.ClassScope.Variables)
                        {
                            if (kv.Key == "this" || kv.Key == "base" || kv.Key == "thistype" || kv.Key == "block" || kv.Key == "init") continue;
                            if (kv.Value.HasAttr("by") || kv.Value.HasAttr("init") || kv.Value.IsOperator) continue;
                            if (kv.Value.HasAttr("core") && Current!.UnsafeDepth == 0) continue;
                            if (kv.Value.HasAttr("private") || kv.Value.HasAttr("protected")) continue;
                            if (seen.Add(kv.Key)) all.Add(new RuntimeValue.StringVal(kv.Key));
                        }
                    }
                    m = m.Parent;
                }
            }
            // 加类型方法（含继承链）
            var t = s.Type;
            while (true)
            {
                foreach (var n in t.MethodNames)
                    if (seen.Add(n)) all.Add(new RuntimeValue.StringVal(n));
                if (t == t.Parent) break;
                t = t.Parent;
            }
            return new RuntimeValue.ListVal(all);
        });
        RuntimeType.Int.DefineMethod("ToString", (s, _) => new RuntimeValue.StringVal(((RuntimeValue.IntVal)s).Value.ToString()));
        RuntimeType.String.DefineMethod("Length", (s, _) => new RuntimeValue.IntVal(((RuntimeValue.StringVal)s).Value.Length));
        RuntimeType.List.DefineMethod("Count", (s, _) => new RuntimeValue.IntVal(((RuntimeValue.ListVal)s).Elements.Count));
        RuntimeType.List.DefineMethod("At", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.IntVal i) throw new RuntimeException("list.At 需要 int 参数");
            var lst = (RuntimeValue.ListVal)s; if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return lst.Elements[i.Value];
        });
        RuntimeType.List.DefineMethod("Add", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("list.Add 需要 1 个参数");
            ((RuntimeValue.ListVal)s).Elements.Add(a[0]);
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.List.DefineMethod("Remove", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.IntVal i) throw new RuntimeException("list.Remove 需要 int 参数");
            var lst = (RuntimeValue.ListVal)s; if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            var v = lst.Elements[i.Value]; lst.Elements.RemoveAt(i.Value); return v;
        });
        RuntimeType.List.DefineMethod("Insert", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.IntVal i) throw new RuntimeException("list.Insert 需要 int 参数");
            var lst = (RuntimeValue.ListVal)s; var idx = i.Value;
            if (idx < 0 || idx > lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return RuntimeValue.FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("list.Insert 需要值参数");
                lst.Elements.Insert(idx, va[0]); return RuntimeValue.VoidVal.Instance;
            });
        });
        RuntimeType.List.DefineMethod("Set", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.IntVal i) throw new RuntimeException("list.Set 需要 int 参数");
            var lst = (RuntimeValue.ListVal)s; var idx = i.Value;
            if (idx < 0 || idx >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return RuntimeValue.FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("list.Set 需要值参数");
                lst.Elements[idx] = va[0]; return RuntimeValue.VoidVal.Instance;
            });
        });

        // ---- Set 方法 ----
        RuntimeType.Set.DefineMethod("Count", (s, _) => new RuntimeValue.IntVal(((RuntimeValue.SetVal)s).Elements.Count));
        RuntimeType.Set.DefineMethod("Add", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Add 需要 1 个参数");
            ((RuntimeValue.SetVal)s).Elements.Add(a[0]);
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Set.DefineMethod("Remove", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Remove 需要 1 个参数");
            ((RuntimeValue.SetVal)s).Elements.Remove(a[0]);
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Set.DefineMethod("Contains", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Contains 需要 1 个参数");
            return new RuntimeValue.BoolVal(((RuntimeValue.SetVal)s).Elements.Contains(a[0]));
        });

        // ---- Dict 方法 ----
        RuntimeType.Dict.DefineMethod("Count", (s, _) => new RuntimeValue.IntVal(((RuntimeValue.DictVal)s).Entries.Count));
        RuntimeType.Dict.DefineMethod("Get", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.StringVal key) throw new RuntimeException("dict.Get 需要 string 参数");
            var d = (RuntimeValue.DictVal)s;
            if (d.Entries.TryGetValue(key.Value, out var v)) return v;
            throw new RuntimeException($"键不存在: {key.Value}");
        });
        RuntimeType.Dict.DefineMethod("Set", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.StringVal key) throw new RuntimeException("dict.Set 需要 string 键");
            return RuntimeValue.FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("dict.Set 需要值参数");
                ((RuntimeValue.DictVal)s).Entries[key.Value] = va[0];
                return RuntimeValue.VoidVal.Instance;
            });
        });
        RuntimeType.Dict.DefineMethod("Has", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.StringVal key) throw new RuntimeException("dict.Has 需要 string 参数");
            return new RuntimeValue.BoolVal(((RuntimeValue.DictVal)s).Entries.ContainsKey(key.Value));
        });
        RuntimeType.Dict.DefineMethod("Keys", (s, _) =>
        {
            var keys = new List<RuntimeValue>();
            foreach (var k in ((RuntimeValue.DictVal)s).Entries.Keys)
                keys.Add(new RuntimeValue.StringVal(k));
            return new RuntimeValue.ListVal(keys);
        });
        RuntimeType.Dict.DefineMethod("Values", (s, _) =>
        {
            var vals = new List<RuntimeValue>();
            foreach (var v in ((RuntimeValue.DictVal)s).Entries.Values)
                vals.Add(v);
            return new RuntimeValue.ListVal(vals);
        });

        // type 类型方法
        RuntimeType.Type.DefineMethod("name", (s, _) => new RuntimeValue.StringVal(((RuntimeValue.TypeVal)s).Value.Name));
        RuntimeType.Type.DefineMethod("Parent", (s, _) =>
        {
            var p = ((RuntimeValue.TypeVal)s).Value.Parent;
            if (p == ((RuntimeValue.TypeVal)s).Value) return s;
            return new RuntimeValue.TypeVal(p);
        });
        RuntimeType.Type.DefineMethod("Is", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.TypeVal other) throw new RuntimeException("type.Is 需要 type 参数");
            return new RuntimeValue.BoolVal(((RuntimeValue.TypeVal)s).Value.IsAssignableTo(other.Value));
        });
        RuntimeType.Type.DefineMethod("Default", (s, _) =>
        {
            var tv = (RuntimeValue.TypeVal)s;
            return EvalTypeCastDirect(tv, RuntimeValue.DefaultVal.Instance);
        });
        RuntimeType.Function.DefineMethod("Name", (s, a) =>
        {
            var fn = (RuntimeValue.FunctionVal)s;
            if (a.Length > 0 && a[0] is RuntimeValue.StringVal sv) { fn.Name = sv.Value; return RuntimeValue.VoidVal.Instance; }
            return fn.Name != null ? new RuntimeValue.StringVal(fn.Name) : RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Type.DefineMethod("Subtypes", (s, _) =>
        {
            var tv = (RuntimeValue.TypeVal)s;
            var subs = new List<RuntimeValue>();
            foreach (var t in Current!.AllTypes)
            {
                if (t != tv.Value && t.IsAssignableTo(tv.Value))
                    subs.Add(new RuntimeValue.TypeVal(t));
            }
            return new RuntimeValue.ListVal(subs);
        });

        systemModule.ModuleScope.Define("WriteLine", RuntimeType.Function, RuntimeValue.FunctionVal.FromDirect(a => { Console.WriteLine(Show(a[0])); return RuntimeValue.VoidVal.Instance; }));
        systemModule.ModuleScope.Define("Write", RuntimeType.Function, RuntimeValue.FunctionVal.FromDirect(a => { Console.Write(Show(a[0])); return RuntimeValue.VoidVal.Instance; }));
        systemModule.ModuleScope.Define("ReadLine", RuntimeType.Function, RuntimeValue.FunctionVal.FromDirect(_ => new RuntimeValue.StringVal(Console.ReadLine() ?? "")));
        systemModule.ModuleScope.Define("True", RuntimeType.Bool, new RuntimeValue.BoolVal(true));
        systemModule.ModuleScope.Define("False", RuntimeType.Bool, new RuntimeValue.BoolVal(false));
        systemModule.ModuleScope.Define("Default", RuntimeType.Every, RuntimeValue.DefaultVal.Instance);
        systemModule.ModuleScope.Define("While", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(EvalWhile));
        systemModule.ModuleScope.Define("If", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(EvalIf));
        systemModule.ModuleScope.Define("CallCC", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(EvalCallCC));
        systemModule.ModuleScope.Define("ValueTypeVal", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.ValueType));
        systemModule.ModuleScope.Define("TypeOf", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1) throw new RuntimeException("typeof 需要 1 个参数");
            return ToDone(new RuntimeValue.TypeVal(a[0].Type));
        }));
        systemModule.ModuleScope.Define("RandInt", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.IntVal lo) throw new RuntimeException("randint 需要 int 参数(最小值)");
            return ToDone(RuntimeValue.FunctionVal.FromTrampolined(b =>
            {
                if (b.Length != 1 || b[0] is not RuntimeValue.IntVal hi) throw new RuntimeException("randint 需要 int 参数(最大值)");
                return ToDone(new RuntimeValue.IntVal(Random.Shared.Next(lo.Value, hi.Value)));
            }));
        }));
        systemModule.ModuleScope.Define("With", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1) return ThrowRavel("with 需要 1 个参数");
            var obj = a[0];
            var copy = obj switch
            {
                RuntimeValue.ObjectVal ov => new RuntimeValue.ObjectVal(ov.ClassType, new Dictionary<string, RuntimeValue>(ov.Fields), ov.Parent, ov.Meta, CopyScope(ov.InstanceScope)),
                RuntimeValue.ListVal lv => new RuntimeValue.ListVal([.. lv.Elements]),
                RuntimeValue.SetVal sv => new RuntimeValue.SetVal([.. sv.Elements]),
                RuntimeValue.DictVal dv => new RuntimeValue.DictVal(new Dictionary<string, RuntimeValue>(dv.Entries)),
                _ => obj
            };
            return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ba =>
            {
                var block = ExpectBlock(ba[0], "with body");
                var saved = CurrentScope;
                if (copy is RuntimeValue.ObjectVal { InstanceScope: not null } ov2)
                    CurrentScope = ov2.InstanceScope;
                return Then(EvalBlockExec(block.Block), _ =>
                {
                    CurrentScope = saved;
                    if (copy is RuntimeValue.ObjectVal { InstanceScope: not null } ov3)
                        SyncScopeToFields(ov3.InstanceScope, ov3);
                    return ToDone(copy);
                });
            }));
        }));
        systemModule.ModuleScope.Define("RavelMod", RuntimeType.Function, RuntimeValue.FunctionVal.FromDirect(a =>
        {
            var name = ((RuntimeValue.StringVal)a[0]).Value;
            if (!_modules.TryGetValue(name, out var mv))
            {
                var mt = RuntimeType.Define(name, RuntimeType.Ravel);
                mv = new RuntimeValue.ModuleVal(mt, new Scope(_global));
                _modules[name] = mv;
                _global.Define(name, mt, mv);
            }
            CurrentScope = mv.ModuleScope;
            return RuntimeValue.VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("Using", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(a =>
        {
            var path = ((RuntimeValue.StringVal)a[0]).Value;
            var refs = new List<string> { "/workspace/ravel/lib/", "/workspace/ravel/", "./", "lib/" };
            var rv = _global.TryLookup("references");
            if (rv?.Value is RuntimeValue.ListVal lv) refs = [.. lv.Elements.Select(e => ((RuntimeValue.StringVal)e).Value), .. refs];
            string? full = null;
            foreach (var d in refs) { var p = Path.Combine(d, path); if (File.Exists(p)) { full = p; break; } }
            if (full == null) foreach (var d in refs) { var p = Path.Combine(d, path + ".rav"); if (File.Exists(p)) { full = p; break; } }
            if (full == null) return ThrowRavel("找不到文件: " + path);
            full = Path.GetFullPath(full);
            if (_loading.Contains(full)) return ThrowRavel("检测到循环引用: " + path);
            if (_loaded.Contains(full)) return ToDone(RuntimeValue.VoidVal.Instance);
            _loaded.Add(full);
            _loading.Push(full);
            var src = File.ReadAllText(full);
            var lexer = new Lexer(src); var parser = new Parser(lexer.Tokenize()); var ast = parser.Parse();
            var savedScope = CurrentScope;
            try
            {
                return EvalBlockStmts(ast.Statements, 0, RuntimeValue.VoidVal.Instance, () => { });
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
            var scope = ((RuntimeValue.ScopeVal)s).Scope;
            return new RuntimeValue.ScopeVal(scope.Push());
        });
        RuntimeType.ScopeType.DefineMethod("Define", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.StringVal name)
                throw new RuntimeException("scope.Define 需要字符串名称");
            var scope = ((RuntimeValue.ScopeVal)s).Scope;
            return RuntimeValue.FunctionVal.FromDirect(b =>
            {
                if (b.Length != 1 || b[0] is not RuntimeValue.TypeVal tv)
                    throw new RuntimeException("scope.Define 需要 type 参数");
                scope.Define(name.Value, tv.Value, RuntimeValue.VoidVal.Instance);
                return RuntimeValue.VoidVal.Instance;
            });
        });
        RuntimeType.ScopeType.DefineMethod("Lookup", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.StringVal name)
                throw new RuntimeException("scope.Lookup 需要字符串参数");
            var scope = ((RuntimeValue.ScopeVal)s).Scope;
            var vr = scope.Lookup(name.Value);
            return new RuntimeValue.PropertyVal(
                RuntimeValue.FunctionVal.FromDirect(_ => vr.Value),
                RuntimeValue.FunctionVal.FromDirect(args => { vr.Assign(args[0]); return RuntimeValue.VoidVal.Instance; }),
                [.. vr.Attrs]
            );
        });
        RuntimeType.ScopeType.DefineMethod("Variables", (s, _) =>
        {
            var scope = ((RuntimeValue.ScopeVal)s).Scope;
            var d = new Dictionary<string, RuntimeValue>();
            foreach (var kv in scope.Variables)
            {
                if (kv.Key == "this" || kv.Key == "base" || kv.Key == "block" || kv.Key == "thistype") continue;
                var vr = scope.Lookup(kv.Key);
                var getter = RuntimeValue.FunctionVal.FromDirect(_ => vr.Value);
                var setter = RuntimeValue.FunctionVal.FromDirect(a => { vr.Assign(a[0]); return RuntimeValue.VoidVal.Instance; });
                d[kv.Key] = new RuntimeValue.PropertyVal(getter, setter, [.. kv.Value.Attrs]);
            }
            return new RuntimeValue.DictVal(d);
        });

        // ---- Property 方法 ----
        RuntimeType.Property.DefineMethod("Attrs", (s, _) =>
        {
            var pv = (RuntimeValue.PropertyVal)s;
            return pv.Attrs != null
                ? new RuntimeValue.ListVal([.. pv.Attrs.Select(a => new RuntimeValue.StringVal(a))])
                : new RuntimeValue.ListVal([]);
        });

        // ---- Function 方法 ----
        RuntimeType.Function.DefineMethod("scope", (s, _) =>
        {
            var fn = (RuntimeValue.FunctionVal)s;
            return new RuntimeValue.ScopeVal(fn.Scope);
        });
        RuntimeType.Function.DefineMethod("setScope", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.ScopeVal sv) throw new RuntimeException("setScope 需要 Scope 参数");
            ((RuntimeValue.FunctionVal)s).Scope = sv.Scope;
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Function.DefineMethod("setType", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.TypeVal tv) throw new RuntimeException("setType 需要 type 参数");
            var fn = (RuntimeValue.FunctionVal)s;
            fn.Meta = new RuntimeValue.ClassMeta(Type: tv.Value, Init: fn, Fields: [], MetaType: RuntimeType.Class);
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Function.DefineMethod("prepend", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.BlockVal p) throw new RuntimeException("prepend 需要代码块参数");
            return ((RuntimeValue.FunctionVal)s).Prepend(p);
        });
        RuntimeType.Function.DefineMethod("append", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.BlockVal p) throw new RuntimeException("append 需要代码块参数");
            return ((RuntimeValue.FunctionVal)s).Append(p);
        });

        // ---- Type 方法 ----
        RuntimeType.Type.DefineMethod("Instantiate", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.ScopeVal sv) throw new RuntimeException("Instantiate 需要 Scope 参数");
            var rt = ((RuntimeValue.TypeVal)s).Value;
            var fields = new Dictionary<string, RuntimeValue>();
            foreach (var kv in sv.Scope.Variables)
            {
                if (kv.Key == "this" || kv.Key == "base" || kv.Key == "block" || kv.Key == "thistype") continue;
                fields[kv.Key] = kv.Value.Value;
            }
            return new RuntimeValue.ObjectVal(rt, fields, null, null, sv.Scope);
        });
        // ---- 内置函数 ----
        systemModule.ModuleScope.Define("unsafe", RuntimeType.Function, RuntimeValue.FunctionVal.FromDirect(_ =>
        {
            Current!.UnsafeDepth++;
            return RuntimeValue.VoidVal.Instance;
        }));
        systemModule.ModuleScope.Define("property", RuntimeType.Function, RuntimeValue.FunctionVal.FromDirect(a =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.FunctionVal g) throw new RuntimeException("property 需要 getter 函数");
            return RuntimeValue.FunctionVal.FromDirect(b =>
            {
                if (b.Length != 1 || b[0] is not RuntimeValue.FunctionVal s) throw new RuntimeException("property 需要 setter 函数");
                return new RuntimeValue.PropertyVal(g, s);
            });
        }));
        systemModule.ModuleScope.Define("Foreach", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.ListVal lst)
                throw new RuntimeException("Foreach 需要 list 参数");
            return ToDone(RuntimeValue.FunctionVal.FromTrampolined(b =>
            {
                if (b.Length != 1 || b[0] is not RuntimeValue.FunctionVal fn)
                    throw new RuntimeException("Foreach 需要函数参数");
                RuntimeValue last = RuntimeValue.VoidVal.Instance;
                foreach (var item in lst.Elements)
                {
                    var s = fn.Trampolined([item]);
                    last = Step.Run(s);
                }
                return ToDone(last);
            }));
        }));
        systemModule.ModuleScope.Define("currentScope", RuntimeType.Function, RuntimeValue.FunctionVal.FromDirect(_ =>
            new RuntimeValue.ScopeVal(Current!.CurrentScope)));
        systemModule.ModuleScope.Define("Eval", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.StringVal s) return ThrowRavel("eval expects string");
            var lexer = new Lexer(s.Value); var tokens = lexer.Tokenize();
            var parser = new Parser(tokens); var ast = parser.Parse();
            return EvalBlockStmts(ast.Statements, 0, RuntimeValue.VoidVal.Instance, () => { });
        }));
        systemModule.ModuleScope.Define("Assert", RuntimeType.Function, RuntimeValue.FunctionVal.FromTrampolined(a =>
        {
            if (a.Length != 1 || a[0] is not RuntimeValue.BoolVal cond)
                return ThrowRavel("assert 需要 bool 参数");
            var ok = cond.Value;
            return ToDone(RuntimeValue.FunctionVal.FromDirect(ma =>
            {
                var msg = ma.Length > 0 && ma[0] is RuntimeValue.StringVal s ? s.Value : "assertion failed: " + Show(cond);
                if (!ok) throw new RuntimeException(msg);
                return RuntimeValue.VoidVal.Instance;
            }));
        }));
        systemModule.ModuleScope.Define("Exit", RuntimeType.Function, RuntimeValue.FunctionVal.FromDirect(a =>
        {
            var msg = a.Length > 0 ? ((RuntimeValue.StringVal)a[0]).Value : "";
            throw new ExitException(msg);
        }));
    }
}
