namespace Ravel.Runtime;
using System.IO;
using System.Linq;

public partial class Interpreter
{
    private readonly Scope _global;
    /// <summary>当前作用域——随执行动态变化</summary>
    public Scope CurrentScope { get; set; }
    /// <summary>当前解释器实例（单例模式，方便静态方法访问）</summary>
    public static Interpreter? Current;

    /// <summary>内建类型名 → RuntimeType 速查表</summary>
    private static readonly Dictionary<string, RuntimeType> TypeRegistry = new()
    {
        ["object"]=RuntimeType.Object,["int"]=RuntimeType.Int,["bool"]=RuntimeType.Bool,
        ["string"]=RuntimeType.String,["function"]=RuntimeType.Function,["list"]=RuntimeType.List,
        ["void"]=RuntimeType.Void,["type"]=RuntimeType.Type,["Any"]=RuntimeType.Any,["float"]=RuntimeType.Float,["bigint"]=RuntimeType.BigInt,["fraction"]=RuntimeType.Fraction,["bigfraction"]=RuntimeType.BigFraction,["set"]=RuntimeType.Set,["dict"]=RuntimeType.Dict,
    };

    /// <summary>创建解释器：注册内置、加载预定义模块</summary>
    public Interpreter() { _global=new Scope(); CurrentScope=_global; RegisterBuiltins(); Current=this; LoadPredefined(); }
    readonly Dictionary<string,RuntimeValue.ModuleVal> _modules=[];
    readonly HashSet<string> _loaded=[];
    readonly Stack<string> _loading=new();
    internal bool _callccActive;
    internal int _unsafeDepth;
    /// <summary>所有已注册类型（内置 + 用户定义）</summary>
    internal readonly List<RuntimeType> _allTypes = [];

    /// <summary>从磁盘加载 predefined.rav（别名、导入标准库）</summary>
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
    /// <summary>执行一个程序的全部语句</summary>
    public void Interpret(Program p) { foreach(var s in p.Statements) Step.Run(EvalStmt(s)); }

    // ======================== CPS 基础 ========================

    /// <summary>创建 Done 步骤——表示计算完成，携带结果值</summary>
    internal static Step ToDone(RuntimeValue v) => new Done(v);
    /// <summary>创建 More 步骤——表示计算未完成，需继续执行</summary>
    internal static Step ToMore(Func<Step> f) => new More(f);
    /// <summary>CPS 绑定：执行 first，结果交给 onDone 继续计算。支持 callcc 多发射续延</summary>
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
    /// <summary>CPS 错误恢复：执行 first，如果出错则调用 onError 处理</summary>
    internal static Step OrElse(Step first, Func<Error, Step> onError){
        if(first is Escape e) return e;
        if(first is Error err) return onError(err);
        if(first is Done d) return first;
        var m=(More)first; return ToMore(()=>OrElse(m.Next(),onError));
    }
    /// <summary>CPS finally：无论步骤结果是 Done/Escape/Error，都执行 onFinally</summary>
    internal static Step Finally(Step first, Action onFinally)
    {
        if (first is Escape e) { onFinally(); return e; }
        if (first is Error err) { onFinally(); return err; }
        if (first is Done d) { onFinally(); return d; }
        var m = (More)first;
        return ToMore(() => Finally(m.Next(), onFinally));
    }


    // ======================== 控制流 ========================

    /// <summary>While 内置函数：While { 条件 } → 返回等待 body 的函数</summary>
    Step EvalWhile(RuntimeValue[] a){
        var c=ExpectBlock(a[0],"while cond");
        return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ba=>{
            var b=ExpectBlock(ba[0],"while body");
            return Loop(c,b,RuntimeValue.VoidVal.Instance);
        }));
    }
    /// <summary>While 循环 CPS：重复调用条件块，真则执行体块，以 last 作为最终返回值</summary>
    Step Loop(RuntimeValue.BlockVal c,RuntimeValue.BlockVal b,RuntimeValue last)=>Then(c.Invoke(),cv=>{
        if(cv is not RuntimeValue.BoolVal bv) return ThrowRavel("while 条件必须是 bool");
        if(!bv.Value) return ToDone(last);
        return Then(b.Invoke(),bv2=>Loop(c,b,bv2));
    });

    /// <summary>If 内置函数：If { 条件 } → 返回等待 then 的函数 → 返回等待 else 的函数</summary>
    Step EvalIf(RuntimeValue[] a){
        var c=ExpectBlock(a[0],"if cond");
        return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ta=>{
            var t=ExpectBlock(ta[0],"if then");
            return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ea=>{
                var e=ExpectBlock(ea[0],"if else");
                return Then(c.Invoke(),cv=>{
                    if(cv is not RuntimeValue.BoolVal b) return ThrowRavel("if 条件必须是 bool");
                    return (b.Value?t:e).Invoke();
                });
            }));
        }));
    }

    // ======================== callcc (多发射续延) ========================

    /// <summary>CallCC 内置函数：callcc fn —— 捕获当前续延 k，调用 fn(k)。k 支持多发</summary>
    Step EvalCallCC(RuntimeValue[] a){
        if(a.Length!=1||a[0] is not RuntimeValue.FunctionVal fn) return ThrowRavel("callcc 需要 1 个函数参数");
        // 返回 CallCC——Then 在此捕获 continuation 并创建多发续延
        return new CallCC(fn);
    }

    // ======================== 语句 ========================

    /// <summary>求值单条语句：变量定义（含隐式转换和属性处理）、赋值、表达式语句</summary>
    Step EvalStmt(Statement stmt)=>stmt switch{
        VarDefinition v=>Then(EvalExpr(v.Value),val=>{
            var dt=v.TypeAnnotation!=null?ResolveType(v.TypeAnnotation):val.Type;
            if(v.TypeAnnotation!=null&&!val.Type.IsAssignableTo(dt)){
                // 隐式转换：尝试 Type value
                var cv=TryConvert(val,dt);
                if(cv!=null) val=cv;
                else return ThrowRavel($"类型不匹配: 无法将 {val.Type} 赋值给 {dt}");
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
        _=>ThrowRavel("未知的语句类型")
    };

    // ==== Expr/Call → Interpreter.Expr.cs
    // ======================== 块/Lambda ========================

    /// <summary>将代码块 AST 包装为 BlockVal 值（惰性，不立即执行）</summary>
    Step EvalBlock(BlockExpr block)=>ToDone(new RuntimeValue.BlockVal(block,CurrentScope,this));
    /// <summary>立即执行代码块：压入新作用域，逐条执行语句</summary>
    public Step EvalBlockExec(BlockExpr block)=>ToMore(()=>{
        CurrentScope=CurrentScope.Push();
        return EvalBlockStmts(block.Statements,0,RuntimeValue.VoidVal.Instance);
    });
    /// <summary>递归执行块内语句列表（带新作用域），执行完后弹出作用域</summary>
    Step EvalBlockStmts(List<Statement> ss,int i,RuntimeValue last){
        if(i>=ss.Count){ CurrentScope=CurrentScope.Parent!; return ToDone(last); }
        if(ss[i] is ExpressionStatement es) return Then(EvalExpr(es.Expr),v=>EvalBlockStmts(ss,i+1,v));
        return Then(EvalStmt(ss[i]),v=>EvalBlockStmts(ss,i+1,v));
    }
    /// <summary>直接执行块语句（不额外压入作用域），执行完后回调 onDone</summary>
    internal Step EvalBlockStmtsDirect(List<Statement> ss,int i,RuntimeValue last,Action onDone){
        if(i>=ss.Count){ onDone(); return ToDone(last); }
        if(ss[i] is ExpressionStatement es) return Then(EvalExpr(es.Expr),v=>EvalBlockStmtsDirect(ss,i+1,v,onDone));
        return Then(EvalStmt(ss[i]),v=>EvalBlockStmtsDirect(ss,i+1,v,onDone));
    }

    /// <summary>求值 lambda 表达式：创建闭包函数，捕获当前作用域</summary>
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

    /// <summary>公开接口：求值代码块（供 BlockVal 等调用）</summary>
    public Step EvalBlockPublic(BlockExpr b)=>EvalBlock(b);
    /// <summary>公开接口：执行单条语句（供外部调用）</summary>
    public void ExecuteStmtPublic(Statement s){ Step.Run(EvalStmt(s)); }

    /// <summary>断言值是 BlockVal，否则抛异常</summary>
    static RuntimeValue.BlockVal ExpectBlock(RuntimeValue v,string r)=>v is RuntimeValue.BlockVal b?b:throw new RuntimeException($"{r} 必须是代码块");
    /// <summary>按名称解析类型：先查类型注册表，再查当前作用域</summary>
    RuntimeType ResolveType(string n, Scope? extra=null){
        if(extra!=null){ try{ var v=extra.Lookup(n); if(v.Value is RuntimeValue.TypeVal tv) return tv.Value; }catch{} }

        if(TypeRegistry.TryGetValue(n,out var t)) return t;
        try{ var v=CurrentScope.Lookup(n); if(v.Value is RuntimeValue.TypeVal tv) return tv.Value; }catch{}
        throw new RuntimeException($"未知的类型: {n}");
    }
    /// <summary>判断值是否为"真"：bool true / 非零数字 / 非 void 非 default</summary>
    static bool IsTruthy(RuntimeValue v)=>v is RuntimeValue.BoolVal b?b.Value:v is RuntimeValue.IntVal i&&i.Value!=0||v is RuntimeValue.FloatVal f&&f.Value!=0||v is RuntimeValue.BigIntVal bi&&bi.Value!=0?v is not RuntimeValue.VoidVal&&v is not RuntimeValue.DefaultVal:true;
    /// <summary>统一错误处理：通过 Ex.throw 抛出 Ravel 异常，找不到则直接 exit</summary>
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

    /// <summary>静态错误抛出：尝试走 Ravel 错误处理，失败则抛 ExitException</summary>
    internal static void ThrowStatic(string msg)
    {
        var interp = Current;
        if (interp != null) { interp.ThrowRavel(msg); }
        throw new ExitException(msg);
    }

    /// <summary>值检查占位（调试用，当前为空）</summary>
    static void CheckD(RuntimeValue v,string c){ }
    /// <summary>值转字符串（Ravel 语义）</summary>
    static string Show(RuntimeValue v)=>v.ToString();
}

/// <summary>可变引用盒子——用于闭包中捕获可重新赋值的变量</summary>
class Ref<T>
{
    public T Value;
    public Ref(T v) { Value = v; }
}
