namespace Ravel.Runtime;


using System.IO;

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
        ["object"] = RuntimeType.Object,
        ["int"] = RuntimeType.Int,
        ["bool"] = RuntimeType.Bool,
        ["string"] = RuntimeType.String,
        ["function"] = RuntimeType.Function,
        ["list"] = RuntimeType.List,
        ["void"] = RuntimeType.Void,
        ["type"] = RuntimeType.Type,
        ["Any"] = RuntimeType.Any,
        ["float"] = RuntimeType.Float,
        ["bigint"] = RuntimeType.BigInt,
        ["fraction"] = RuntimeType.Fraction,
        ["bigfraction"] = RuntimeType.BigFraction,
        ["set"] = RuntimeType.Set,
        ["dict"] = RuntimeType.Dict,
    };

    /// <summary>创建解释器：注册内置、加载预定义模块</summary>
    public Interpreter() { _global = new Scope(); CurrentScope = _global; RegisterBuiltins(); Current = this; LoadPredefined(); }
    private readonly Dictionary<string, ModuleVal> _modules = [];
    private readonly HashSet<string> _loaded = [];
    private readonly Stack<string> _loading = new();
    internal bool CallccActive;
    internal int UnsafeDepth;
    /// <summary>所有已注册类型（内置 + 用户定义）</summary>
    internal readonly List<RuntimeType> AllTypes = [];

    /// <summary>从磁盘加载 predefined.rav（别名、导入标准库）</summary>
    private void LoadPredefined()
    {
        foreach (var d in new[] { "./", "/workspace/ravel/lib/", "/workspace/ravel/", "lib/" })
        {
            var p = Path.Combine(d, "predefined.rav");
            if (File.Exists(p))
            {
                var src = File.ReadAllText(p);
                var lexer = new Lexer(src); var parser = new Parser(lexer.Tokenize()); var ast = parser.Parse();
                Step.Run(EvalBlockStmts(ast.Statements, 0, VoidVal.Instance, () => { }));
                return;
            }
        }
    }
    /// <summary>执行一个程序的全部语句</summary>
    public void Interpret(Program p) { foreach (var s in p.Statements) Step.Run(EvalStmt(s)); }

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
            Current.CallccActive = true;
            var k = new ContinuationVal(r =>
            {
                if (Current.CallccActive)
                {
                    // fn body 内部调用 → 用 Escape 退出 fn body（一发）
                    return new Escape(r);
                }
                else
                {
                    // fn body 已结束，外部调用 → 直接执行 continuation（多发）
                    var saved = Current.CurrentScope;
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
                var savedOuter = Current.CurrentScope;
                var s = cc.Fn.Trampolined([k]);
                var result = Step.Run(s);
                Current.CallccActive = false;
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
    internal static Step OrElse(Step first, Func<Error, Step> onError)
    {
        if (first is Escape e) return e;
        if (first is Error err) return onError(err);
        if (first is Done) return first;
        var m = (More)first; return ToMore(() => OrElse(m.Next(), onError));
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
    private Step EvalWhile(RuntimeValue[] a)
    {
        var c = ExpectBlock(a[0], "while cond");
        return ToDone(FunctionVal.FromTrampolined(ba =>
        {
            var b = ExpectBlock(ba[0], "while body");
            return Loop(c, b, VoidVal.Instance);
        }));
    }
    /// <summary>While 循环 CPS：重复调用条件块，真则执行体块，以 last 作为最终返回值</summary>
    private Step Loop(BlockVal c, BlockVal b, RuntimeValue last) => Then(c.Invoke(), cv =>
    {
        if (cv is not BoolVal bv) return ThrowRavel("while 条件必须是 bool");
        if (!bv.Value) return ToDone(last);
        return Then(b.Invoke(), bv2 => Loop(c, b, bv2));
    });

    /// <summary>If 内置函数：If { 条件 } → 返回等待 then 的函数 → 返回等待 else 的函数</summary>
    private Step EvalIf(RuntimeValue[] a)
    {
        var c = ExpectBlock(a[0], "if cond");
        return ToDone(FunctionVal.FromTrampolined(ta =>
        {
            var t = ExpectBlock(ta[0], "if then");
            return ToDone(FunctionVal.FromTrampolined(ea =>
            {
                var e = ExpectBlock(ea[0], "if else");
                return Then(c.Invoke(), cv =>
                {
                    if (cv is not BoolVal b) return ThrowRavel("if 条件必须是 bool");
                    return (b.Value ? t : e).Invoke();
                });
            }));
        }));
    }

    // ======================== callcc (多发射续延) ========================

    /// <summary>CallCC 内置函数：callcc fn —— 捕获当前续延 k，调用 fn(k)。k 支持多发</summary>
    private Step EvalCallCC(RuntimeValue[] a)
    {
        if (a.Length != 1 || a[0] is not FunctionVal fn) return ThrowRavel("callcc 需要 1 个函数参数");
        // 返回 CallCC——Then 在此捕获 continuation 并创建多发续延
        return new CallCC(fn);
    }

    // ======================== 语句 ========================

    /// <summary>求值单条语句：变量定义（含隐式转换和属性处理）、赋值、表达式语句</summary>
    private Step EvalStmt(Statement stmt) => stmt switch
    {
        VarDefinition v => Then(EvalExpr(v.Value), val =>
        {
            var dt = v.TypeAnnotation != null ? ResolveType(v.TypeAnnotation) : val.Type;
            if (v.TypeAnnotation != null && !val.Type.IsAssignableTo(dt))
            {
                // 隐式转换：尝试 Type value
                var cv = TryConvert(val, dt);
                if (cv != null) val = cv;
                else return ThrowRavel($"类型不匹配: 无法将 {val.Type} 赋值给 {dt}");
            }
            var vr = CurrentScope.DefineOrReplace(v.Name, dt, val);
            if (v.Attrs != null) foreach (var a in v.Attrs) vr.SetAttr(a);
            if (v.Named && val is FunctionVal fn) { fn.Name = v.Name; }
            if (v.IsInit && v.Name != "init") CurrentScope.DefineOrReplace("init", dt, val);
            if (v is { IsOperator: true, OperatorName: not null } && v.Name != v.OperatorName) CurrentScope.DefineOrReplace(v.OperatorName, dt, val);
            return ToDone(VoidVal.Instance);
        }),
        Assignment a => Then(EvalExpr(a.Value), val =>
        {
            var vr = CurrentScope.TryLookup(a.Name);
            if (vr != null && vr.HasAttr("by"))
            {
                var prop = vr.Value;
                var setter = new BoxedValue(prop).GetMember("set").Value;
                if (setter is FunctionVal sf)
                    Step.Run(sf.Trampolined([val]));
                return ToDone(val);
            }
            CurrentScope.Assign(a.Name, val); return ToDone(VoidVal.Instance);
        }),
        ExpressionStatement es => Then(EvalExpr(es.Expr), ToDone),
        _ => ThrowRavel("未知的语句类型")
    };

    // ==== Expr/Call → Interpreter.Expr.cs
    // ======================== 块/Lambda ========================

    /// <summary>将代码块 AST 包装为 BlockVal 值（惰性，不立即执行）</summary>
    private Step EvalBlock(BlockExpr block) => ToDone(new BlockVal(block, CurrentScope, this));
    /// <summary>立即执行代码块：压入新作用域，执行完后弹出</summary>
    public Step EvalBlockExec(BlockExpr block) => ToMore(() =>
    {
        CurrentScope = CurrentScope.Push();
        return EvalBlockStmts(block.Statements, 0, VoidVal.Instance,
            () => { CurrentScope = CurrentScope.Parent!; });
    });
    /// <summary>递归执行块内语句列表，执行完后回调 onDone（不碰作用域）</summary>
    internal Step EvalBlockStmts(List<Statement> ss, int i, RuntimeValue last, Action onDone)
    {
        if (i >= ss.Count) { onDone(); return ToDone(last); }
        if (ss[i] is ExpressionStatement es) return Then(EvalExpr(es.Expr), v => EvalBlockStmts(ss, i + 1, v, onDone));
        return Then(EvalStmt(ss[i]), v => EvalBlockStmts(ss, i + 1, v, onDone));
    }

    /// <summary>求值 lambda 表达式：创建闭包函数，捕获当前作用域</summary>
    private RuntimeValue EvalLambda(LambdaExpr lam)
    {
        var pt = ResolveType(lam.Param.TypeName, CurrentScope);
        FunctionVal? self = null;

        self = new FunctionVal(CurrentScope, (scope, args) =>
        {
            if (args.Length != 1)
                return new Error("Lambda 需要 1 个参数");
            if (!args[0].Type.IsAssignableTo(pt))
                return new Error("类型不匹配");

            var innerScope = scope.Push();
            innerScope.Define("self", RuntimeType.Function, self!);
            innerScope.Define(lam.Param.Name, pt, args[0]);

            var savedScope = CurrentScope;
            CurrentScope = innerScope;

            return ToMore(() =>
            {
                var bodyStep = EvalBlockExec(lam.Body);
                return Then(bodyStep, v =>
                {
                    CurrentScope = savedScope;
                    return ToDone(v);
                });
            });
        });

        return self;
    }


    /// <summary>断言值是 BlockVal，否则抛异常</summary>
    private static BlockVal ExpectBlock(RuntimeValue v, string r) => v as BlockVal ?? throw new RuntimeException($"{r} 必须是代码块");
    /// <summary>按名称解析类型：先查类型注册表，再查当前作用域</summary>
    private RuntimeType ResolveType(string n, Scope? extra = null)
    {
        if (extra != null)
        {
            var v = extra.TryLookup(n);
            if (v?.Value is TypeVal tv) return tv.Value;
        }

        if (TypeRegistry.TryGetValue(n, out var t)) return t;
        var v2 = CurrentScope.TryLookup(n);
        if (v2?.Value is TypeVal tv2) return tv2.Value;
        throw new RuntimeException($"未知的类型: {n}");
    }
    /// <summary>统一错误处理：通过 Ex.throw 抛出 Ravel 异常，找不到则直接 exit</summary>
    internal Step ThrowRavel(string msg)
    {
        if (_modules.TryGetValue("Ex", out var exMod))
        {
            var throwVar = exMod.ModuleScope.TryLookup("throw");
            if (throwVar?.Value is FunctionVal tf)
            {
                var exVal = new ExceptionVal(msg);
                return tf.Trampolined([exVal]);
            }
        }
        // Ex 模块未加载 → 直接 exit
        var exitVar = _global.TryLookup("exit");
        if (exitVar?.Value is FunctionVal ef)
        {
            Step.Run(ef.Trampolined([new StringVal(msg)]));
        }
        throw new ExitException(msg);
    }

    /// <summary>值转字符串（Ravel 语义）</summary>
    private static string Show(RuntimeValue v) => v.ToString();
}

/// <summary>可变引用盒子——用于闭包中捕获可重新赋值的变量</summary>
internal class Ref<T>(T v)
{
    public T Value = v;
}
