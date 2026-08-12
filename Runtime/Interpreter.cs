namespace Ravel.Runtime;
using System.IO;
using System.Linq;

public partial class Interpreter
{
    private readonly Scope _global;
    public Scope CurrentScope { get; set; }
    public static Interpreter? Current;

    private static readonly Dictionary<string, RuntimeType> TypeRegistry = new()
    {
        ["object"]=RuntimeType.Object,["int"]=RuntimeType.Int,["bool"]=RuntimeType.Bool,
        ["string"]=RuntimeType.String,["function"]=RuntimeType.Function,["list"]=RuntimeType.List,
        ["void"]=RuntimeType.Void,["type"]=RuntimeType.Type,["Any"]=RuntimeType.Any,["float"]=RuntimeType.Float,["bigint"]=RuntimeType.BigInt,["fraction"]=RuntimeType.Fraction,["bigfraction"]=RuntimeType.BigFraction,["set"]=RuntimeType.Set,["dict"]=RuntimeType.Dict, 
    };

    public Interpreter() { _global=new Scope(); CurrentScope=_global; RegisterBuiltins(); Current=this; LoadPredefined(); }
    readonly Dictionary<string,RuntimeValue.ModuleVal> _modules=new();
    readonly HashSet<string> _loaded=new();
    readonly Stack<string> _loading=new();
    internal bool _callccActive;
    internal int _unsafeDepth;
    internal readonly List<RuntimeType> _allTypes = new();

    void LoadPredefined(){
        foreach(var d in new[]{"./","/workspace/ravel/lib/","/workspace/ravel/","lib/"}){
            var p=System.IO.Path.Combine(d,"predefined.rav");
            if(File.Exists(p)){
                var src=File.ReadAllText(p);
                var lexer=new Lexer(src); var parser=new Parser(lexer.Tokenize()); var ast=parser.Parse();
                Step.Run(EvalBlockStmtsDirect(ast.Statements,0,RuntimeValue.VoidVal.Instance,()=>{}));
                return;
            }
        }
    }
    public void Interpret(Program p) { foreach(var s in p.Statements) Step.Run(EvalStmt(s)); }

    // ======================== CPS 基础 ========================
    internal static Step ToDone(RuntimeValue v) => new Done(v);
    internal static Step ToMore(Func<Step> f) => new More(f);
    internal static Step Then(Step first, Func<RuntimeValue, Step> onDone)
    {
        if (first is CallCC cc)
        {
            // 捕获 continuation 以及当前 scope
            var capturedScope = Current!.CurrentScope;
            var contRef = new Ref<Func<RuntimeValue, Step>>(onDone);
            Current._callccActive = true;
            var k = new RuntimeValue.ContinuationVal(r =>
            {
                if (Current._callccActive)
                {
                    // fn body 内部调用 → 用 Escape 退出 fn body（一发）
                    return new Escape(r);
                }
                else
                {
                    // fn body 已结束，外部调用 → 直接执行 continuation（多发）
                    var saved = Current!.CurrentScope;
                    Current.CurrentScope = capturedScope;
                    var step = contRef.Value(r);
                    return Then(step, _ =>
                    {
                        Current.CurrentScope = saved;
                        return ToDone(r);
                    });
                }
            });
            return ToMore(() =>
            {
                var savedOuter = Current!.CurrentScope;
                var s = cc.Fn.Trampolined!([k]);
                var result = Step.Run(s);
                Current._callccActive = false;
                Current.CurrentScope = savedOuter;
                // 显式调用被捕获的 continuation
                return contRef.Value(result);
            });
        }
        if (first is Escape e) return e;
        if (first is Error err) return err;
        if (first is Done d) return onDone(d.Value);
        var m = (More)first;
        return ToMore(() => Then(m.Next(), onDone));
    }
    internal static Step OrElse(Step first, Func<Error, Step> onError){
        if(first is Escape e) return e;
        if(first is Error err) return onError(err);
        if(first is Done d) return first;
        var m=(More)first; return ToMore(()=>OrElse(m.Next(),onError));
    }
    /// <summary>无论 Step 结果是 Done/Escape/Error，都执行 onFinally</summary>
    internal static Step Finally(Step first, Action onFinally)
    {
        if (first is Escape e) { onFinally(); return e; }
        if (first is Error err) { onFinally(); return err; }
        if (first is Done d) { onFinally(); return d; }
        var m = (More)first;
        return ToMore(() => Finally(m.Next(), onFinally));
    }


    // ======================== 控制流 ========================
    Step EvalWhile(RuntimeValue[] a){
        var c=ExpectBlock(a[0],"while cond");
        return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ba=>{
            var b=ExpectBlock(ba[0],"while body");
            return Loop(c,b,RuntimeValue.VoidVal.Instance);
        }));
    }
    Step Loop(RuntimeValue.BlockVal c,RuntimeValue.BlockVal b,RuntimeValue last)=>Then(c.Invoke(),cv=>{
        if(cv is not RuntimeValue.BoolVal bv) throw new RuntimeException("while condition must be bool");
        if(!bv.Value) return ToDone(last);
        return Then(b.Invoke(),bv2=>Loop(c,b,bv2));
    });

    Step EvalIf(RuntimeValue[] a){
        var c=ExpectBlock(a[0],"if cond");
        return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ta=>{
            var t=ExpectBlock(ta[0],"if then");
            return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ea=>{
                var e=ExpectBlock(ea[0],"if else");
                return Then(c.Invoke(),cv=>{
                    if(cv is not RuntimeValue.BoolVal b) throw new RuntimeException("if condition must be bool");
                    return (b.Value?t:e).Invoke();
                });
            }));
        }));
    }

    // ======================== callcc (多发射续延) ========================
    Step EvalCallCC(RuntimeValue[] a){
        if(a.Length!=1||a[0] is not RuntimeValue.FunctionVal fn) throw new RuntimeException("callcc expects 1 function");
        // 返回 CallCC——Then 在此捕获 continuation 并创建多发续延
        return new CallCC(fn);
    }

    // ======================== 语句 ========================
    Step EvalStmt(Statement stmt)=>stmt switch{
        VarDefinition v=>Then(EvalExpr(v.Value),val=>{
            var dt=v.TypeAnnotation!=null?ResolveType(v.TypeAnnotation):val.Type;
            if(v.TypeAnnotation!=null&&!val.Type.IsAssignableTo(dt)){
                // 隐式转换：尝试 Type value
                var cv=TryConvert(val,dt);
                if(cv!=null) val=cv;
                else throw new RuntimeException($"Type mismatch: cannot assign {val.Type} to {dt}");
            }
            var vr=CurrentScope.DefineOrReplace(v.Name,dt,val);
            if(v.Attrs!=null) foreach(var a in v.Attrs) vr.SetAttr(a);
            if(v.Named && val is RuntimeValue.FunctionVal fn){ fn.Name=v.Name; if(fn.Meta!=null) fn.Meta.Type.Name=v.Name; }
            if(v.IsInit&&v.Name!="init") CurrentScope.DefineOrReplace("init",dt,val);
            if(v.IsOperator&&v.OperatorName!=null&&v.Name!=v.OperatorName) CurrentScope.DefineOrReplace(v.OperatorName,dt,val);
            return ToDone(RuntimeValue.VoidVal.Instance);
        }),
        Assignment a=>Then(EvalExpr(a.Value),val=>{
            try{
                var vr=CurrentScope.Lookup(a.Name);
                if(vr.HasAttr("by")){
                    var prop=vr.Value;
                    var setter=new BoxedValue(prop).GetMember("set").Value;
                    if(setter is RuntimeValue.FunctionVal sf)
                        Step.Run(sf.Trampolined!(new RuntimeValue[]{val}));
                    return ToDone(val);
                }
            }catch{}
            CurrentScope.Assign(a.Name,val); return ToDone(RuntimeValue.VoidVal.Instance);
        }),
        ExpressionStatement es=>Then(EvalExpr(es.Expr),v=>ToDone(v)),
        _=>throw new RuntimeException("Unknown statement")
    };

    // ==== Expr/Call → Interpreter.Expr.cs
    // ======================== 块/Lambda ========================
    Step EvalBlock(BlockExpr block)=>ToDone(new RuntimeValue.BlockVal(block,CurrentScope,this));
    public Step EvalBlockExec(BlockExpr block)=>ToMore(()=>{
        CurrentScope=CurrentScope.Push();
        return EvalBlockStmts(block.Statements,0,RuntimeValue.VoidVal.Instance);
    });
    Step EvalBlockStmts(List<Statement> ss,int i,RuntimeValue last){
        if(i>=ss.Count){ CurrentScope=CurrentScope.Parent!; return ToDone(last); }
        if(ss[i] is ExpressionStatement es) return Then(EvalExpr(es.Expr),v=>EvalBlockStmts(ss,i+1,v));
        return Then(EvalStmt(ss[i]),v=>EvalBlockStmts(ss,i+1,v));
    }
    internal Step EvalBlockStmtsDirect(List<Statement> ss,int i,RuntimeValue last,Action onDone){
        if(i>=ss.Count){ onDone(); return ToDone(last); }
        if(ss[i] is ExpressionStatement es) return Then(EvalExpr(es.Expr),v=>EvalBlockStmtsDirect(ss,i+1,v,onDone));
        return Then(EvalStmt(ss[i]),v=>EvalBlockStmtsDirect(ss,i+1,v,onDone));
    }

    RuntimeValue EvalLambda(LambdaExpr lam){
        var interp=this; RuntimeValue.FunctionVal? self=null;
        var pt=ResolveType(lam.Param.TypeName,CurrentScope);
        var fn=new RuntimeValue.FunctionVal(CurrentScope,(scope,args)=>{
            if(args.Length!=1) return new Error("Lambda expects 1 arg");
            if(!args[0].Type.IsAssignableTo(pt)) return new Error("Type mismatch");
            var ls=scope.Push(); ls.Define("self",RuntimeType.Function,self!);
            ls.Define(lam.Param.Name,pt,args[0]);
            var save=interp.CurrentScope; interp.CurrentScope=ls;
            return ToMore(()=>{ var bs=interp.EvalBlockExec(lam.Body); return Then(bs,v=>{ interp.CurrentScope=save; return ToDone(v); }); });
        });
        self=fn; return fn;
    }

    public Step EvalBlockPublic(BlockExpr b)=>EvalBlock(b);
    public void ExecuteStmtPublic(Statement s){ Step.Run(EvalStmt(s)); }

    static RuntimeValue.BlockVal ExpectBlock(RuntimeValue v,string r)=>v is RuntimeValue.BlockVal b?b:throw new RuntimeException($"{r} must be block");
    RuntimeType ResolveType(string n, Scope? extra=null){
        if(extra!=null){ try{ var v=extra.Lookup(n); if(v.Value is RuntimeValue.TypeVal tv) return tv.Value; }catch{} }
        
        if(TypeRegistry.TryGetValue(n,out var t)) return t;
        try{ var v=CurrentScope.Lookup(n); if(v.Value is RuntimeValue.TypeVal tv) return tv.Value; }catch{}
        throw new RuntimeException($"Unknown type: {n}");
    }
    static bool IsTruthy(RuntimeValue v)=>v is RuntimeValue.BoolVal b?b.Value:v is RuntimeValue.IntVal i&&i.Value!=0||v is RuntimeValue.FloatVal f&&f.Value!=0||v is RuntimeValue.BigIntVal bi&&bi.Value!=0?v is not RuntimeValue.VoidVal&&v is not RuntimeValue.DefaultVal:true;
    /// <summary>统一错误处理：通过 scope 找 Ex.throw，找不到则直接 exit</summary>
    internal Step ThrowRavel(string msg)
    {
        try
        {
            if (_modules.TryGetValue("Ex", out var exMod))
            {
                var throwVar = exMod.ModuleScope.Lookup("throw");
                if (throwVar.Value is RuntimeValue.FunctionVal tf)
                {
                    var exVal = new RuntimeValue.ExceptionVal(msg);
                    return tf.Trampolined(new RuntimeValue[] { exVal });
                }
            }
        }
        catch {}
        // Ex 模块未加载 → 直接 exit
        try
        {
            var exitVar = _global.Lookup("exit");
            if (exitVar.Value is RuntimeValue.FunctionVal ef)
            {
                ef.Direct!(new RuntimeValue[] { new RuntimeValue.StringVal(msg) });
            }
        }
        catch {}
        throw new ExitException(msg);
    }

    internal static void ThrowStatic(string msg)
    {
        var interp = Current;
        if (interp != null) { interp.ThrowRavel(msg); }
        throw new ExitException(msg);
    }

    static void CheckD(RuntimeValue v,string c){ }
    static string Show(RuntimeValue v)=>v.ToString();
}

/// <summary>可变引用——用于闭包捕获可重新赋值的变量</summary>
class Ref<T>
{
    public T Value;
    public Ref(T v) { Value = v; }
}
