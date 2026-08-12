namespace Ravel.Runtime;
using System.IO;
using System.Linq;

public partial class Interpreter
{
    // ======================== 内置 ========================
    private void RegisterBuiltins()
    {
        // Class 类型注册默认 init（支持 class 作为父类）


        // System 内置模块
        var sysMt = RuntimeType.Define("System", RuntimeType.Ravel);
        var sysMod = new RuntimeValue.ModuleVal(sysMt, new Scope(_global));
        sysMod.ModuleScope.Define("Integer", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Int));
        sysMod.ModuleScope.Define("String", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.String));
        sysMod.ModuleScope.Define("Bool", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Bool));
        sysMod.ModuleScope.Define("Float", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Float));
        sysMod.ModuleScope.Define("BigInteger", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.BigInt));
        sysMod.ModuleScope.Define("Fraction", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Fraction));
        sysMod.ModuleScope.Define("BigFraction", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.BigFraction));
        sysMod.ModuleScope.Define("List", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.List));
        sysMod.ModuleScope.Define("Set", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Set));
        sysMod.ModuleScope.Define("Dict", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Dict));
        sysMod.ModuleScope.Define("Object", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Object));
        sysMod.ModuleScope.Define("Function", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Function));
        sysMod.ModuleScope.Define("Void", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Void));
        sysMod.ModuleScope.Define("AnyType", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Any));
        sysMod.ModuleScope.Define("EveryType", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Every));
        sysMod.ModuleScope.Define("ExceptionType", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Exception));
        sysMod.ModuleScope.Define("Class", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Class));
        sysMod.ModuleScope.Define("Type", RuntimeType.Type, new RuntimeValue.TypeVal(RuntimeType.Type));
        _modules["System"] = sysMod;
        _global.Define("System", sysMt, sysMod);

        // 注册所有内置类型
        foreach(var t in new[]{RuntimeType.Object, RuntimeType.ValueType, RuntimeType.Int,
            RuntimeType.Float, RuntimeType.Bool, RuntimeType.String, RuntimeType.BigInt,
            RuntimeType.Fraction, RuntimeType.BigFraction, RuntimeType.Class, RuntimeType.Function,
            RuntimeType.List, RuntimeType.Set, RuntimeType.Dict, RuntimeType.Void, RuntimeType.Type,
            RuntimeType.Ravel, RuntimeType.Any, RuntimeType.Every, RuntimeType.Exception, RuntimeType.ScopeType, RuntimeType.Property})
            _allTypes.Add(t);
        RuntimeType.Type.MetaClass = RuntimeType.Type;
        RuntimeType.Class.MetaClass = RuntimeType.Class;

        RuntimeType.Object.DefineMethod("ToString",(s,_)=>new RuntimeValue.StringVal(Show(s)));
        RuntimeType.Object.DefineMethod("Copy",(s,_)=>{
            switch(s){
                case RuntimeValue.IntVal v: return new RuntimeValue.IntVal(v.Value);
                case RuntimeValue.FloatVal v: return new RuntimeValue.FloatVal(v.Value);
                case RuntimeValue.BoolVal v: return new RuntimeValue.BoolVal(v.Value);
                case RuntimeValue.StringVal v: return new RuntimeValue.StringVal(v.Value);
                case RuntimeValue.BigIntVal v: return new RuntimeValue.BigIntVal(v.Value);
                case RuntimeValue.FractionVal v: return new RuntimeValue.FractionVal(v.Num,v.Den);
                case RuntimeValue.BigFractionVal v: return new RuntimeValue.BigFractionVal(v.Num,v.Den);
                case RuntimeValue.ListVal v: return new RuntimeValue.ListVal(new List<RuntimeValue>(v.Elements));
                case RuntimeValue.SetVal v: return new RuntimeValue.SetVal(new HashSet<RuntimeValue>(v.Elements));
                case RuntimeValue.DictVal v: return new RuntimeValue.DictVal(new Dictionary<string,RuntimeValue>(v.Entries));
                case RuntimeValue.ObjectVal v: {
                    var newFields = new Dictionary<string,RuntimeValue>(v.Fields);
                    Scope? newScope = null;
                    if(v.InstanceScope!=null){
                        newScope = new Scope(v.InstanceScope.Parent);
                        foreach(var kv in v.InstanceScope.Variables)
                            newScope.Define(kv.Key, kv.Value.TypeConstraint, kv.Value.Value);
                    }
                    return new RuntimeValue.ObjectVal(v.ClassType,newFields,v.Parent,v.Meta,newScope);
                }
                default: return s;
            }
        });
        RuntimeType.Object.DefineMethod("Fields",(s,_)=>{
            var all = new List<RuntimeValue>();
            var seen = new HashSet<string>();
            // 类实例：加字段
            if(s is RuntimeValue.ObjectVal ov&&ov.Meta!=null){
                var m = ov.Meta;
                while(m!=null){
                    if(m.ClassScope!=null){
                        foreach(var kv in m.ClassScope.Variables){
                            if(kv.Key=="this"||kv.Key=="base"||kv.Key=="thistype"||kv.Key=="block"||kv.Key=="init") continue;
                            if(kv.Value.HasAttr("by")||kv.Value.HasAttr("init")||kv.Value.IsOperator) continue;
                            if(kv.Value.HasAttr("core") && Interpreter.Current!._unsafeDepth==0) continue;
                            if(kv.Value.HasAttr("private")||kv.Value.HasAttr("protected")) continue;
                            if(seen.Add(kv.Key)) all.Add(new RuntimeValue.StringVal(kv.Key));
                        }
                    }
                    m = m.Parent;
                }
            }
            // 加类型方法（含继承链）
            var t = s.Type;
            while(t!=null){
                foreach(var n in t.MethodNames)
                    if(seen.Add(n)) all.Add(new RuntimeValue.StringVal(n));
                if(t==t.Parent) break;
                t = t.Parent;
            }
            return new RuntimeValue.ListVal(all);
        });
        RuntimeType.Int.DefineMethod("ToString",(s,_)=>new RuntimeValue.StringVal(((RuntimeValue.IntVal)s).Value.ToString()));
        RuntimeType.String.DefineMethod("Length",(s,_)=>new RuntimeValue.IntVal(((RuntimeValue.StringVal)s).Value.Length));
        RuntimeType.List.DefineMethod("Count",(s,_)=>new RuntimeValue.IntVal(((RuntimeValue.ListVal)s).Elements.Count));
        RuntimeType.List.DefineMethod("At",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.IntVal i) throw new RuntimeException("list.At expects int");
            var lst=(RuntimeValue.ListVal)s; if(i.Value<0||i.Value>=lst.Elements.Count) throw new RuntimeException("Index out of range");
            return lst.Elements[i.Value];
        });
        RuntimeType.List.DefineMethod("Add",(s,a)=>{
            if(a.Length!=1) throw new RuntimeException("list.Add expects 1 arg");
            ((RuntimeValue.ListVal)s).Elements.Add(a[0]);
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.List.DefineMethod("Remove",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.IntVal i) throw new RuntimeException("list.Remove expects int");
            var lst=(RuntimeValue.ListVal)s; if(i.Value<0||i.Value>=lst.Elements.Count) throw new RuntimeException("Index out of range");
            var v=lst.Elements[i.Value]; lst.Elements.RemoveAt(i.Value); return v;
        });
        RuntimeType.List.DefineMethod("Insert",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.IntVal i) throw new RuntimeException("list.Insert expects int");
            var lst=(RuntimeValue.ListVal)s; var idx=i.Value;
            if(idx<0||idx>lst.Elements.Count) throw new RuntimeException("Index out of range");
            return RuntimeValue.FunctionVal.FromDirect(va=>{
                if(va.Length!=1) throw new RuntimeException("list.Insert expects value");
                lst.Elements.Insert(idx,va[0]); return RuntimeValue.VoidVal.Instance;
            });
        });
        RuntimeType.List.DefineMethod("Set",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.IntVal i) throw new RuntimeException("list.Set expects int");
            var lst=(RuntimeValue.ListVal)s; var idx=i.Value;
            if(idx<0||idx>=lst.Elements.Count) throw new RuntimeException("Index out of range");
            return RuntimeValue.FunctionVal.FromDirect(va=>{
                if(va.Length!=1) throw new RuntimeException("list.Set expects value");
                lst.Elements[idx]=va[0]; return RuntimeValue.VoidVal.Instance;
            });
        });

        // ---- Set 方法 ----
        RuntimeType.Set.DefineMethod("Count",(s,_)=>new RuntimeValue.IntVal(((RuntimeValue.SetVal)s).Elements.Count));
        RuntimeType.Set.DefineMethod("Add",(s,a)=>{
            if(a.Length!=1) throw new RuntimeException("set.Add expects 1 arg");
            ((RuntimeValue.SetVal)s).Elements.Add(a[0]);
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Set.DefineMethod("Remove",(s,a)=>{
            if(a.Length!=1) throw new RuntimeException("set.Remove expects 1 arg");
            ((RuntimeValue.SetVal)s).Elements.Remove(a[0]);
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Set.DefineMethod("Contains",(s,a)=>{
            if(a.Length!=1) throw new RuntimeException("set.Contains expects 1 arg");
            return new RuntimeValue.BoolVal(((RuntimeValue.SetVal)s).Elements.Contains(a[0]));
        });

        // ---- Dict 方法 ----
        RuntimeType.Dict.DefineMethod("Count",(s,_)=>new RuntimeValue.IntVal(((RuntimeValue.DictVal)s).Entries.Count));
        RuntimeType.Dict.DefineMethod("Get",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.StringVal key) throw new RuntimeException("dict.Get expects string");
            var d=(RuntimeValue.DictVal)s;
            if(d.Entries.TryGetValue(key.Value,out var v)) return v;
            throw new RuntimeException($"Key not found: {key.Value}");
        });
        RuntimeType.Dict.DefineMethod("Set",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.StringVal key) throw new RuntimeException("dict.Set expects string key");
            return RuntimeValue.FunctionVal.FromDirect(va=>{
                if(va.Length!=1) throw new RuntimeException("dict.Set expects value");
                ((RuntimeValue.DictVal)s).Entries[key.Value]=va[0];
                return RuntimeValue.VoidVal.Instance;
            });
        });
        RuntimeType.Dict.DefineMethod("Has",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.StringVal key) throw new RuntimeException("dict.Has expects string");
            return new RuntimeValue.BoolVal(((RuntimeValue.DictVal)s).Entries.ContainsKey(key.Value));
        });
        RuntimeType.Dict.DefineMethod("Keys",(s,_)=>{
            var keys=new List<RuntimeValue>();
            foreach(var k in ((RuntimeValue.DictVal)s).Entries.Keys)
                keys.Add(new RuntimeValue.StringVal(k));
            return new RuntimeValue.ListVal(keys);
        });
        RuntimeType.Dict.DefineMethod("Values",(s,_)=>{
            var vals=new List<RuntimeValue>();
            foreach(var v in ((RuntimeValue.DictVal)s).Entries.Values)
                vals.Add(v);
            return new RuntimeValue.ListVal(vals);
        });

        // type 类型方法
        RuntimeType.Type.DefineMethod("name",(s,_)=>new RuntimeValue.StringVal(((RuntimeValue.TypeVal)s).Value.Name));
        RuntimeType.Type.DefineMethod("Parent",(s,_)=>{
            var p=((RuntimeValue.TypeVal)s).Value.Parent;
            if(p==null) return s;
            if(p==((RuntimeValue.TypeVal)s).Value) return s;
            return new RuntimeValue.TypeVal(p);
        });
        RuntimeType.Type.DefineMethod("Is",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.TypeVal other) throw new RuntimeException("type.Is expects type");
            return new RuntimeValue.BoolVal(((RuntimeValue.TypeVal)s).Value.IsAssignableTo(other.Value));
        });
        RuntimeType.Type.DefineMethod("Default",(s,_)=>{
            var tv=(RuntimeValue.TypeVal)s;
            return EvalTypeCastDirect(tv,RuntimeValue.DefaultVal.Instance);
        });
        RuntimeType.Function.DefineMethod("Name",(s,a)=>{
            var fn=(RuntimeValue.FunctionVal)s;
            if(a.Length>0 && a[0] is RuntimeValue.StringVal sv){ fn.Name=sv.Value; return RuntimeValue.VoidVal.Instance; }
            return fn.Name!=null ? new RuntimeValue.StringVal(fn.Name) : RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Type.DefineMethod("Metaclass",(s,_)=>{
            var tv=(RuntimeValue.TypeVal)s;
            if(tv.Value==RuntimeType.Type) return s;
            if(tv.Value.MetaClass!=null) return new RuntimeValue.TypeVal(tv.Value.MetaClass);
            return new RuntimeValue.TypeVal(RuntimeType.Type);
        });
        RuntimeType.Type.DefineMethod("Subtypes",(s,_)=>{
            var tv=(RuntimeValue.TypeVal)s;
            var subs = new List<RuntimeValue>();
            foreach(var t in Interpreter.Current!._allTypes){
                if(t!=tv.Value && t.IsAssignableTo(tv.Value))
                    subs.Add(new RuntimeValue.TypeVal(t));
            }
            return new RuntimeValue.ListVal(subs);
        });

        sysMod.ModuleScope.Define("WriteLine",RuntimeType.Function,RuntimeValue.FunctionVal.FromDirect(a=>{ Console.WriteLine(Show(a[0])); return RuntimeValue.VoidVal.Instance; }));
        sysMod.ModuleScope.Define("Write",RuntimeType.Function,RuntimeValue.FunctionVal.FromDirect(a=>{ Console.Write(Show(a[0])); return RuntimeValue.VoidVal.Instance; }));
        sysMod.ModuleScope.Define("ReadLine",RuntimeType.Function,RuntimeValue.FunctionVal.FromDirect(_=>new RuntimeValue.StringVal(Console.ReadLine()??"")));
        sysMod.ModuleScope.Define("True",RuntimeType.Bool,new RuntimeValue.BoolVal(true));
        sysMod.ModuleScope.Define("False",RuntimeType.Bool,new RuntimeValue.BoolVal(false));
        sysMod.ModuleScope.Define("Default",RuntimeType.Every,RuntimeValue.DefaultVal.Instance);
        sysMod.ModuleScope.Define("While",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>EvalWhile(a)));
        sysMod.ModuleScope.Define("If",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>EvalIf(a)));
        sysMod.ModuleScope.Define("CallCC",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>EvalCallCC(a)));
        sysMod.ModuleScope.Define("ValueTypeVal",RuntimeType.Type,new RuntimeValue.TypeVal(RuntimeType.ValueType));
        sysMod.ModuleScope.Define("TypeOf",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>{
            if(a.Length!=1) throw new RuntimeException("typeof expects 1 arg");
            return ToDone(new RuntimeValue.TypeVal(a[0].Type));
        }));
        sysMod.ModuleScope.Define("RandInt",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>{
            if(a.Length!=1||a[0] is not RuntimeValue.IntVal lo) throw new RuntimeException("randint expects int (min)");
            return ToDone(RuntimeValue.FunctionVal.FromTrampolined(b=>{
                if(b.Length!=1||b[0] is not RuntimeValue.IntVal hi) throw new RuntimeException("randint expects int (max)");
                return ToDone(new RuntimeValue.IntVal(Random.Shared.Next(lo.Value,hi.Value)));
            }));
        }));
        sysMod.ModuleScope.Define("With",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>{
            if(a.Length!=1) ThrowStatic("with expects 1 arg");
            var obj=a[0];
            var copy=obj switch{
                RuntimeValue.ObjectVal ov=>new RuntimeValue.ObjectVal(ov.ClassType,new Dictionary<string,RuntimeValue>(ov.Fields),ov.Parent,ov.Meta,CopyScope(ov.InstanceScope)),
                RuntimeValue.ListVal lv=>new RuntimeValue.ListVal(new List<RuntimeValue>(lv.Elements)),
                RuntimeValue.SetVal sv=>new RuntimeValue.SetVal(new HashSet<RuntimeValue>(sv.Elements)),
                RuntimeValue.DictVal dv=>new RuntimeValue.DictVal(new Dictionary<string,RuntimeValue>(dv.Entries)),
                _=>obj
            };
            return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ba=>{
                var block=ExpectBlock(ba[0],"with body");
                var saved=CurrentScope;
                if(copy is RuntimeValue.ObjectVal ov2&&ov2.InstanceScope!=null)
                    CurrentScope=ov2.InstanceScope;
                return Then(EvalBlockExec(block.Block),v=>{
                    CurrentScope=saved;
                    if(copy is RuntimeValue.ObjectVal ov3&&ov3.InstanceScope!=null)
                        SyncScopeToFields(ov3.InstanceScope,ov3);
                    return ToDone(copy);
                });
            }));
        }));
        sysMod.ModuleScope.Define("RavelMod",RuntimeType.Function,RuntimeValue.FunctionVal.FromDirect(a=>{
            var name=((RuntimeValue.StringVal)a[0]).Value;
            if(!_modules.TryGetValue(name,out var mv)){
                var mt=RuntimeType.Define(name,RuntimeType.Ravel);
                mv=new RuntimeValue.ModuleVal(mt,new Scope(_global));
                _modules[name]=mv;
                _global.Define(name,mt,mv);
            }
            CurrentScope=mv.ModuleScope;
            return RuntimeValue.VoidVal.Instance;
        }));
        sysMod.ModuleScope.Define("Using",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>{
            var path=((RuntimeValue.StringVal)a[0]).Value;
            var refs=new List<string>{"/workspace/ravel/lib/","/workspace/ravel/","./","lib/"};
            try{ var rv=_global.Lookup("references"); if(rv.Value is RuntimeValue.ListVal lv) refs=lv.Elements.Select(e=>((RuntimeValue.StringVal)e).Value).Concat(refs).ToList(); }catch{}
            string? full=null;
            foreach(var d in refs){ var p=System.IO.Path.Combine(d,path); if(File.Exists(p)){full=p;break;} }
            if(full==null) foreach(var d in refs){ var p=System.IO.Path.Combine(d,path+".rav"); if(File.Exists(p)){full=p;break;} }
            if(full==null) return ThrowRavel("Cannot find file: "+path);
            full=Path.GetFullPath(full);
            if(_loading.Contains(full)) return ThrowRavel("Circular reference detected: "+path);
            if(_loaded.Contains(full)) return ToDone(RuntimeValue.VoidVal.Instance);
            _loaded.Add(full);
            _loading.Push(full);
            var src=File.ReadAllText(full);
            var lexer=new Lexer(src); var parser=new Parser(lexer.Tokenize()); var ast=parser.Parse();
            var savedScope = CurrentScope;
            try{
                return EvalBlockStmtsDirect(ast.Statements,0,RuntimeValue.VoidVal.Instance,()=>{});
            }finally{
                _loading.Pop();
                CurrentScope = savedScope;
            }
        }));

        // ---- Scope 方法 ----
        RuntimeType.ScopeType.DefineMethod("Push",(s,_)=>{
            var scope=((RuntimeValue.ScopeVal)s).Scope;
            return new RuntimeValue.ScopeVal(scope.Push());
        });
        RuntimeType.ScopeType.DefineMethod("Define",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.StringVal name)
                throw new RuntimeException("scope.Define expects a name string");
            var scope=((RuntimeValue.ScopeVal)s).Scope;
            return RuntimeValue.FunctionVal.FromDirect(b=>{
                if(b.Length!=1||b[0] is not RuntimeValue.TypeVal tv)
                    throw new RuntimeException("scope.Define expects a type");
                scope.Define(name.Value,tv.Value,RuntimeValue.VoidVal.Instance);
                return RuntimeValue.VoidVal.Instance;
            });
        });
        RuntimeType.ScopeType.DefineMethod("Lookup",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.StringVal name)
                throw new RuntimeException("scope.Lookup expects a string");
            var scope=((RuntimeValue.ScopeVal)s).Scope;
            var vr=scope.Lookup(name.Value);
            return new RuntimeValue.PropertyVal(
                RuntimeValue.FunctionVal.FromDirect(_=>vr.Value),
                RuntimeValue.FunctionVal.FromDirect(args=>{vr.Assign(args[0]);return RuntimeValue.VoidVal.Instance;}),
                vr.Attrs.ToList()
            );
        });
        RuntimeType.ScopeType.DefineMethod("Variables",(s,_)=>{
            var scope=((RuntimeValue.ScopeVal)s).Scope;
            var d=new Dictionary<string,RuntimeValue>();
            foreach(var kv in scope.Variables){
                if(kv.Key=="this"||kv.Key=="base"||kv.Key=="block"||kv.Key=="thistype")continue;
                var vr=scope.Lookup(kv.Key);
                var getter=RuntimeValue.FunctionVal.FromDirect(_=>vr.Value);
                var setter=RuntimeValue.FunctionVal.FromDirect(a=>{vr.Assign(a[0]);return RuntimeValue.VoidVal.Instance;});
                d[kv.Key]=new RuntimeValue.PropertyVal(getter,setter,kv.Value.Attrs.ToList());
            }
            return new RuntimeValue.DictVal(d);
        });

        // ---- Property 方法 ----
        RuntimeType.Property.DefineMethod("Attrs",(s,_)=>{
            var pv=(RuntimeValue.PropertyVal)s;
            return pv.Attrs!=null
                ?new RuntimeValue.ListVal(pv.Attrs.Select(a=>new RuntimeValue.StringVal(a)).Cast<RuntimeValue>().ToList())
                :new RuntimeValue.ListVal(new List<RuntimeValue>());
        });

        // ---- Function 方法 ----
        RuntimeType.Function.DefineMethod("scope",(s,_)=>{
            var fn=(RuntimeValue.FunctionVal)s;
            return fn.Scope!=null?new RuntimeValue.ScopeVal(fn.Scope):RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Function.DefineMethod("setScope",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.ScopeVal sv) throw new RuntimeException("setScope expects a Scope");
            ((RuntimeValue.FunctionVal)s).Scope=sv.Scope;
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Function.DefineMethod("setType",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.TypeVal tv) throw new RuntimeException("setType expects a type");
            var fn=(RuntimeValue.FunctionVal)s;
            fn.Meta=new RuntimeValue.ClassMeta(Type:tv.Value,Init:fn,Fields:new string[0],MetaType:RuntimeType.Class);
            return RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Function.DefineMethod("prepend",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.BlockVal p) throw new RuntimeException("prepend expects a block");
            return ((RuntimeValue.FunctionVal)s).Prepend(p);
        });
        RuntimeType.Function.DefineMethod("append",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.BlockVal p) throw new RuntimeException("append expects a block");
            return ((RuntimeValue.FunctionVal)s).Append(p);
        });

        // ---- Type 方法 ----
        RuntimeType.Type.DefineMethod("Proto",(s,_)=>{
            var rt=s is RuntimeValue.TypeVal tv?tv.Value
                :s is RuntimeValue.FunctionVal fn&&fn.Meta!=null?fn.Meta.Type
                :throw new RuntimeException("Proto expects a type or class");
            return rt.Proto!=null?new RuntimeValue.ScopeVal(rt.Proto):RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Type.DefineMethod("Instantiate",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.ScopeVal sv) throw new RuntimeException("Instantiate expects a Scope");
            var rt=((RuntimeValue.TypeVal)s).Value;
            var fields=new Dictionary<string,RuntimeValue>();
            foreach(var kv in sv.Scope.Variables){
                if(kv.Key=="this"||kv.Key=="base"||kv.Key=="block"||kv.Key=="thistype")continue;
                fields[kv.Key]=kv.Value.Value;
            }
            return new RuntimeValue.ObjectVal(rt,fields,null,null,sv.Scope);
        });
        RuntimeType.Type.DefineMethod("Initializer",(s,_)=>{
            var rt=((RuntimeValue.TypeVal)s).Value;
            return rt.Initializer!=null?rt.Initializer:RuntimeValue.VoidVal.Instance;
        });
        RuntimeType.Type.DefineMethod("setInitializer",(s,a)=>{
            if(a.Length!=1||a[0] is not RuntimeValue.FunctionVal fn) throw new RuntimeException("setInitializer expects a function");
            ((RuntimeValue.TypeVal)s).Value.Initializer=fn;
            return RuntimeValue.VoidVal.Instance;
        });

        // ---- 内置函数 ----
        sysMod.ModuleScope.Define("unsafe",RuntimeType.Function,RuntimeValue.FunctionVal.FromDirect(_=>{
            Interpreter.Current!._unsafeDepth++;
            return RuntimeValue.VoidVal.Instance;
        }));
        sysMod.ModuleScope.Define("property",RuntimeType.Function,RuntimeValue.FunctionVal.FromDirect(a=>{
            if(a.Length!=1||a[0] is not RuntimeValue.FunctionVal g) throw new RuntimeException("property expects getter");
            return RuntimeValue.FunctionVal.FromDirect(b=>{
                if(b.Length!=1||b[0] is not RuntimeValue.FunctionVal s) throw new RuntimeException("property expects setter");
                return new RuntimeValue.PropertyVal(g,s);
            });
        }));
        sysMod.ModuleScope.Define("Foreach",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>{
            if(a.Length!=1||a[0] is not RuntimeValue.ListVal lst)
                throw new RuntimeException("Foreach expects a list");
            return ToDone(RuntimeValue.FunctionVal.FromTrampolined(b=>{
                if(b.Length!=1||b[0] is not RuntimeValue.FunctionVal fn)
                    throw new RuntimeException("Foreach expects a function");
                RuntimeValue last=RuntimeValue.VoidVal.Instance;
                foreach(var item in lst.Elements){
                    var s=fn.Trampolined!=null?fn.Trampolined(new[]{item}):ToDone(fn.Direct!(new[]{item}));
                    last=Step.Run(s);
                }
                return ToDone(last);
            }));
        }));
        sysMod.ModuleScope.Define("currentScope",RuntimeType.Function,RuntimeValue.FunctionVal.FromDirect(_=>
            new RuntimeValue.ScopeVal(Interpreter.Current!.CurrentScope)));
        sysMod.ModuleScope.Define("Eval",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>{
            if(a.Length!=1||a[0] is not RuntimeValue.StringVal s) return ThrowRavel("eval expects string");
            var lexer=new Lexer(s.Value); var tokens=lexer.Tokenize();
            var parser=new Parser(tokens); var ast=parser.Parse();
            return EvalBlockStmtsDirect(ast.Statements,0,RuntimeValue.VoidVal.Instance,()=>{});
        }));
        sysMod.ModuleScope.Define("Assert",RuntimeType.Function,RuntimeValue.FunctionVal.FromTrampolined(a=>{
            if(a.Length!=1||a[0] is not RuntimeValue.BoolVal cond)
                return ThrowRavel("assert expects bool");
            var ok=cond.Value;
            return ToDone(RuntimeValue.FunctionVal.FromDirect(ma=>{
                var msg=ma.Length>0&&ma[0] is RuntimeValue.StringVal s?s.Value:"assertion failed: "+Show(cond);
                if(!ok) throw new RuntimeException(msg);
                return RuntimeValue.VoidVal.Instance;
            }));
        }));
        sysMod.ModuleScope.Define("Exit",RuntimeType.Function,RuntimeValue.FunctionVal.FromDirect(a=>{
            var msg=a.Length>0?((RuntimeValue.StringVal)a[0]).Value:"";
            throw new ExitException(msg);
        }));
    }
}
