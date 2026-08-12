namespace Ravel.Runtime;
using System.Linq;

public partial class Interpreter
{
    // ======================== class ========================
    // ======================== metaclass ========================
    Step EvalMetaclass(RuntimeValue.FunctionVal metaCtor, List<Expression> args){
        return EvalArgs(args, evaluated => {
            if(evaluated.Length==1 && evaluated[0] is RuntimeValue.BlockVal body)
                return EvalClassCore(RuntimeType.Object, null, body, metaCtor);
            var pf = evaluated[0];
            RuntimeType pt = RuntimeType.Object; RuntimeValue.FunctionVal? pc=null;
            if(pf is RuntimeValue.FunctionVal fn2&&fn2.Meta!=null){ pt=fn2.Meta.Type; pc=fn2; }
            else if(pf is RuntimeValue.TypeVal tv) pt=tv.Value;
            else ThrowStatic("metaclass parent must be a class or type");
            return D(RuntimeValue.FunctionVal.FromTrampolined(ba=>
                EvalClassCore(pt, pc, ExpectBlock(ba[0],"class body"), metaCtor)));
        });
    }

    Step EvalClass(RuntimeValue[] a){
        if(a.Length!=1) throw new RuntimeException("class expects 1 or 2 arguments");
        // class { body } — 无父类
        if(a[0] is RuntimeValue.BlockVal body){
            // 传递 Class 的 init 作为 metaCtor — 统一走 init 流程
            var classInit = RuntimeType.Class.Proto?.Variables.FirstOrDefault(kv=>kv.Value.HasAttr("init")).Value?.Value as RuntimeValue.FunctionVal;
            return EvalClassCore(RuntimeType.Object,null,body,classInit);
        }
        // class Parent — 返回等待 body 的函数
        var parentFn=a[0];
        RuntimeType parentType;
        RuntimeValue.FunctionVal? parentCtor=null;
        if(parentFn is RuntimeValue.FunctionVal pf&&pf.Meta!=null){ parentType=pf.Meta.Type; parentCtor=pf; }
        else if(parentFn is RuntimeValue.TypeVal tv){ parentType=tv.Value; }
        else throw new RuntimeException("class parent must be a class or type");
        return D(RuntimeValue.FunctionVal.FromTrampolined(ba=>EvalClassCore(parentType,parentCtor,ExpectBlock(ba[0],"class body"))));
    }

    static RuntimeValue.FunctionVal CombineInits(RuntimeValue.FunctionVal a, RuntimeValue.FunctionVal b){
        return RuntimeValue.FunctionVal.FromTrampolined(ia => {
            var step = a.CloneWithScope(Interpreter.Current!.CurrentScope).Trampolined!(ia);
            return OrElse(step, _ => b.CloneWithScope(Interpreter.Current!.CurrentScope).Trampolined!(ia));
        });
    }

    RuntimeValue.FunctionVal MakeClassConstructor(
        RuntimeType ct, Scope cs, Scope saved, RuntimeValue.FunctionVal ctor,
        List<string> inits, RuntimeValue.FunctionVal? parentCtor, RuntimeValue.BlockVal body,
        RuntimeValue.ClassMeta meta)
    {
        return RuntimeValue.FunctionVal.FromTrampolined(ia=>{
            var bi=(RuntimeValue.ObjectVal)cs.Lookup("base").Value;
            var inst=new RuntimeValue.ObjectVal(ct,new(),bi,Meta:meta);
            var instScope=new Scope(saved);
            instScope.Define("this",RuntimeType.Object,inst);
            instScope.Define("base",RuntimeType.Object,new RuntimeValue.ObjectVal(cs.Lookup("base").Value.Type,new()));
            instScope.Define("thistype",RuntimeType.Type,cs.Lookup("thistype").Value);
            // 浅拷贝整条 Proto 链字段到 instScope
            CopyProtoFields(instScope,cs,inits);
            inst=new RuntimeValue.ObjectVal(ct,new(),bi,Meta:meta,InstanceScope:instScope);
            instScope.DefineOrReplace("this",RuntimeType.Object,inst);
            cs.Lookup("this").Assign(inst);
            var tm=parentCtor?.Meta;
            while(tm!=null){ tm.ThisVar?.Assign(inst); tm=tm.Parent; }
            if(parentCtor?.Meta!=null){
                instScope.Lookup("base").Assign(new RuntimeValue.BaseRef(inst,parentCtor.Meta));
                var m=parentCtor.Meta;
                while(m!=null&&m.Parent!=null){
                    m.BaseVar?.Assign(new RuntimeValue.BaseRef(inst,m.Parent));
                    m=m.Parent;
                }
            }
            inst.Fields["block"]=body;
            foreach(var kv in cs.Variables)
                if(kv.Key!="this"&&kv.Key!="base"&&kv.Key!="block"&&kv.Key!="thistype"&&!inits.Contains(kv.Key))
                    inst.Fields[kv.Key]=kv.Value.Value;
            var savedCtor=cs.Lookup("this").Value;cs.Lookup("this").Assign(inst);
            var savedCtorScope=ctor.Scope;
            var savedCtorSc=CurrentScope;
            ctor.Scope=instScope;
            CurrentScope=instScope;
            var ctorStep=ctor.Trampolined!=null?ctor.Trampolined(ia):D(ctor.Direct!(ia));
            return Finally(
                Then(ctorStep,initResult=>{
                    if(initResult is RuntimeValue.FunctionVal nextFn&&initResult is not RuntimeValue.VoidVal)
                        return D(nextFn);
                    return D(inst);
                }),
                ()=>{
                    SyncScopeToFields(instScope,inst);
                    ctor.Scope=savedCtorScope;
                    CurrentScope=savedCtorSc;
                    cs.Lookup("this").Assign(savedCtor);
                    CurrentScope=saved;
                }
            );
        }, meta);
    }

    Step EvalClassCore(RuntimeType parentType, RuntimeValue.FunctionVal? parentCtor, RuntimeValue.BlockVal body, RuntimeValue.FunctionVal? metaCtor=null, RuntimeType? metaType=null){
        var ct=RuntimeType.Define("<class>",parentType);
        _allTypes.Add(ct);
        var saved=CurrentScope;
        // 原型链：父 Proto → 子 scope
        var parentProto=parentType.Proto;
        var cs=parentProto!=null?parentProto.Push():saved.Push();
        cs.Define("this",RuntimeType.Object,new RuntimeValue.ObjectVal(ct,new()));
        cs.Define("base",RuntimeType.Object,new RuntimeValue.ObjectVal(parentType,new()));
        cs.Define("thistype",RuntimeType.Type,new RuntimeValue.TypeVal(ct)).SetAttr("readonly");
        CurrentScope=cs;
        // 1) 定义时跑 block → 建原型
        Step.Run(EvalBlockStmtsDirect(body.Block.Statements,0,RuntimeValue.VoidVal.Instance,()=>{}));
        CurrentScope=saved;
        // 提取 init/operators
        var inits=new List<string>();
        var ops=new List<string>();
        foreach(var kv in cs.Variables){
            if(kv.Key=="this"||kv.Key=="base"||kv.Key=="block"||kv.Key=="thistype")continue;
            if(kv.Value.HasAttr("init")) inits.Add(kv.Key);
            if(kv.Key.StartsWith("operator")) ops.Add(kv.Key);
        }
        foreach(var op in ops){
            if(cs.Lookup(op) is Variable v2&&v2.Value is RuntimeValue.FunctionVal ofn)
                ct.Operators[ExtractOp(op)]=ofn;
        }
        // 继承父类运算符
        if(parentType.Proto!=null){
            foreach(var kv in parentType.Proto.Variables)
                if(kv.Key.StartsWith("operator")&&kv.Value.Value is RuntimeValue.FunctionVal ofn2)
                    if(!ct.Operators.ContainsKey(ExtractOp(kv.Key)))
                        ct.Operators[ExtractOp(kv.Key)]=ofn2;
        }
        RuntimeValue.FunctionVal ctor;
        if(inits.Count>0){
            ctor=(RuntimeValue.FunctionVal)cs.Lookup(inits[0]).Value;
            for(int i=1;i<inits.Count;i++){
                var next=(RuntimeValue.FunctionVal)cs.Lookup(inits[i]).Value;
                ctor=CombineInits(ctor,next);
            }
        }
        else ctor=RuntimeValue.FunctionVal.FromTrampolined(_=>new Error("No constructor"));
        // 存原型
        ct.Rebuild(cs);
        // 2) 返回构造器：浅拷贝原型 → 跑 init
        var arr=inits.ToArray();
        var metaTypeFinal = metaType ?? metaCtor?.Meta?.Type ?? RuntimeType.Class;
        ct.MetaClass = metaTypeFinal;
        var meta=new RuntimeValue.ClassMeta(ct,ctor,arr,cs.Lookup("this"),cs.Lookup("base"),parentCtor?.Meta,cs,metaTypeFinal);
        var classCtor = D(MakeClassConstructor(ct, cs, saved, ctor, inits, parentCtor, body, meta));
        // metaclass: 创建执行 scope 并调 init
        if(metaCtor!=null && metaCtor.Meta?.Init is RuntimeValue.FunctionVal mif){
            return M(()=>{
                var clsResult = Step.Run(classCtor);
                var metaScope = new Scope(_global);
                metaScope.Define("this", ct, clsResult);
                metaScope.Define("parent", RuntimeType.Type, new RuntimeValue.TypeVal(parentType));
                metaScope.Define("block", RuntimeType.Function, body);
                metaScope.Define("thistype", RuntimeType.Type, new RuntimeValue.TypeVal(ct));
                var saved = CurrentScope;
                var savedMeta = _currentMetaType;
                _currentMetaType = metaTypeFinal;
                CurrentScope = metaScope;
                // 柯里化调多参 init: (parent) → inner → (block) → result
                var step = mif.Trampolined!=null
                    ? mif.Trampolined(new RuntimeValue[]{new RuntimeValue.TypeVal(parentType)})
                    : D(mif.Direct!(new RuntimeValue[]{new RuntimeValue.TypeVal(parentType)}));
                return Then(step, r1 => {
                    if(r1 is not RuntimeValue.FunctionVal inner){ ThrowStatic("metaclass has no valid init"); return D(clsResult); }
                    return Then(
                        inner.Trampolined!=null ? inner.Trampolined(new RuntimeValue[]{body}) : D(inner.Direct!(new RuntimeValue[]{body})),
                        r2 => {
                            CurrentScope = saved;
                            _currentMetaType = savedMeta;
                            return D(r2 is RuntimeValue.FunctionVal mf ? mf : clsResult);
                        });
                });
            });
        }
        return classCtor;
    }
    Step EvalBlockStmtsDirect(List<Statement> ss,int i,RuntimeValue last,Action onDone){
        if(i>=ss.Count){ onDone(); return D(last); }
        if(ss[i] is ExpressionStatement es) return Then(EvalExpr(es.Expr),v=>EvalBlockStmtsDirect(ss,i+1,v,onDone));
        return Then(EvalStmt(ss[i]),v=>EvalBlockStmtsDirect(ss,i+1,v,onDone));
    }

}
