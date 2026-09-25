namespace Ravel.Runtime;

/// <summary>调用分派:把「函数值 + 一个参数」变成帧栈上的一步。
/// sink 是结果接收帧——同步值走 sink.WithResult,延迟求值(块/类/控制/续延)把 sink 当作新帧的父。</summary>
public partial class Interpreter
{
    /// <summary>调用函数:结果流向 sink 帧(sink.WithResult 累积,或作为推帧的父)。CallExpr 用 sink=callFrame.Parent</summary>
    private void CallInto(Frame sink, RuntimeValue fn, RuntimeValue arg)
    {
        switch (fn)
        {
            case ControlFunction cf:
                if (cf.IsFinalAfter(1))
                    _top = new ControlFrame(cf.Kind, cf.Args.Add(arg), VoidVal.Instance) { Parent = sink, Scope = sink.Scope, CallSite = (_top as NodeFrame)?.Node };
                else
                    _top = sink.WithResult(cf.Accumulate(arg));
                break;
            case LambdaVal lam:
                if (!arg.Type.IsAssignableTo(lam.ParamType))
                    throw new TypeMismatchException($"参数 '{lam.ParamName}' 需要 {lam.ParamType}，得到 {arg.Type}");
                var lamScope = lam.CaptureScope.Push();
                lamScope.Define("self", BuiltinClasses.Function, lam);
                lamScope.Define(lam.ParamName, lam.ParamType, arg);
                _top = new BlockExecFrame(lam.Block) { Parent = sink, Scope = lamScope };
                break;
            case NativeClosure nc:
            {
                // 和 LambdaVal 同款:先查参数类型(交替机制靠它分流),再在**调用点作用域**里跑
                if (!arg.Type.IsAssignableTo(nc.ParamType))
                    throw new TypeMismatchException($"参数 '{nc.ParamName}' 需要 {nc.ParamType}，得到 {arg.Type}");
                var ncScope = _top.Scope.Push();
                ncScope.Define("self", BuiltinClasses.Function, nc);
                ncScope.Define(nc.ParamName, nc.ParamType, arg);
                _top = sink.WithResult(nc.Fn(ncScope, arg));
                break;
            }
            case BlockVal blk:
                _top = new BlockExecFrame(blk.Block) { Parent = sink, Scope = blk.CaptureScope.Push() };
                break;
            // 类对象 = 实例化它。**这是唯一一条路**:类对象自己就是可调用的东西
            // (ClassVal : FunctionVal),没有 `call` 成员、也没有中间的 BoundCall 转发。
            case ClassVal cv:
                CallClassInto(sink, cv, arg);
                break;
            case ComposeVal comp:
            {
                var cargs = RList<RuntimeValue>.Empty.Add(comp.Original).Add(comp.Block).Add(arg).Add(new BoolVal(comp.IsPrepend));
                _top = new ControlFrame(ControlKind.Compose, cargs, VoidVal.Instance) { Parent = sink, Scope = sink.Scope, CallSite = (_top as NodeFrame)?.Node };
                break;
            }
            case BoundClassOp bco:
                PushClassOp(sink, bco.OpName, bco.Self, arg);
                break;
            case PartialCtor pc:
            {
                var cargs = RList<RuntimeValue>.Empty.Add(pc.Partial).Add(arg).Add(pc.Target);
                _top = new ControlFrame(ControlKind.CtorApply, cargs, VoidVal.Instance) { Parent = sink, Scope = sink.Scope, CallSite = (_top as NodeFrame)?.Node };
                break;
            }
            case ContinuationVal k:
                // 续延 = callcc 之后的剩余计算。调用它:丢弃当前帧链,把 arg 当作
                // callcc 的返回值、从捕获点继续。丢弃当前链正是它能当「跳转」写循环的原因。
                _top = k.Captured.WithResult(arg);
                break;
            case BoolVal bv:
                // true/false 是函数(lisp 式):收两个块,返回选中那个块的结果
                if (arg is not BlockVal thenBlock) throw new RuntimeException("true/false 需要两个代码块");
                _top = sink.WithResult(new PartialBool(bv.Value, thenBlock));
                break;
            case PartialBool pb:
                if (arg is not BlockVal elseBlock) throw new RuntimeException("true/false 需要两个代码块");
                var chosen = pb.Value ? pb.Then : elseBlock;
                _top = new BlockExecFrame(chosen.Block) { Parent = sink, Scope = chosen.CaptureScope.Push() };
                break;
            // 对象实例(带上没被上面接住的一般 FunctionVal)—— 不是函数就是不能调。
            // **必须排在最后、且必须排掉函数**:`FunctionVal` 现在也是 `ObjectVal`,
            // 排在前面会把上面那些函数分支全吃掉(编译器直接报 CS8120),不排掉函数则会把
            // "普通 FunctionVal 走 default: 直接算 Body"这条也吃掉 —— 症状是所有内置方法调用
            // 都报「值 <function> 不是函数,不能调用」。
            case ObjectVal ov when ov is not FunctionVal:
                throw new RuntimeException($"值 {ov} 不是函数，不能调用");
            default:
                if (fn is not FunctionVal fv) throw new RuntimeException($"值 {fn} 不是函数，不能调用");
                _top = sink.WithResult(fv.Body(arg));
                break;
        }
    }

    /// <summary>调用一个类对象 = 实例化它。**只有一条路**：推 ClassInit 控制帧,
    /// 建 scope、跑各层类体、调用类体里定义的 `init`,并交出 init 的返回值。
    ///
    /// 内置类和用户类走的是同一条(S1 那条 "有 Initializer 就同步转换" 的分叉没有了)——
    /// `int 42` 得 42,是因为 `Integer` 的预设类体里写着 `init := <CastToInt>`。
    /// 没有类体的类型(Every/Any/Ravel/Scope/Property/Object…)照旧不能当构造器调。</summary>
    private void CallClassInto(Frame sink, ClassVal t, RuntimeValue arg)
    {
        if (t.ClassBody == null)
            throw new RuntimeException($"类型 {t.DisplayName} 不能作为构造器调用");   // 是类,但没类体(Every/Any/Object/…)
        _top = new ControlFrame(ControlKind.ClassInit, RList<RuntimeValue>.Empty.Add(t).Add(arg), VoidVal.Instance)
        {
            Parent = sink,
            Scope = sink.Scope,
            CallSite = (_top as NodeFrame)?.Node,
        };
    }

    // ======================== 合成控制帧的推帧助手 ========================

    /// <summary>推 ClassOp 帧:到实例里找同名成员(符号就是成员名)并调,结果返回 parent 帧。
    /// 作用域取当前帧(调用发起处)的,不是 parent 的——运算符体应按调用点作用域求值。</summary>
    private void PushClassOp(Frame parent, string op, ObjectVal self, RuntimeValue arg)
    {
        var callerScope = _top.Scope;
        var args = RList<RuntimeValue>.Empty.Add(self).Add(arg).Add(new StringVal(op));
        _top = new ControlFrame(ControlKind.ClassOp, args, VoidVal.Instance) { Parent = parent, Scope = callerScope };
    }

    /// <summary>复合赋值通用帧:CallInto 算运算符,完成时把结果赋回目标变量</summary>
    private void PushCallAssign(NodeFrame nf, FunctionVal bound, RuntimeValue arg, string target)
    {
        var args = RList<RuntimeValue>.Empty.Add(bound).Add(arg).Add(new StringVal(target));
        _top = new ControlFrame(ControlKind.CallAssign, args, VoidVal.Instance) { Parent = nf.Parent, Scope = nf.Scope };
    }

    /// <summary>调用副作用函数(setter),完成后返回固定结果。Args=[fn, arg, result]</summary>
    private void PushCallReturn(Frame parent, FunctionVal fn, RuntimeValue arg, RuntimeValue result)
    {
        var args = RList<RuntimeValue>.Empty.Add(fn).Add(arg).Add(result);
        _top = new ControlFrame(ControlKind.CallReturn, args, VoidVal.Instance) { Parent = parent, Scope = parent.Scope };
    }
}
