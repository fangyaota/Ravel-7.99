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
                if (!Accepts(arg, lam.ParamType))
                    throw new TypeMismatchException($"参数 '{lam.ParamName}' 需要 {lam.ParamType}，得到 {arg.Type}");
                var lamScope = lam.CaptureScope.Push();
                lamScope.Define("self", BuiltinClasses.Function, lam);
                lamScope.Define(lam.ParamName, lam.ParamType, arg);
                _top = new BlockExecFrame(lam.Block) { Parent = sink, Scope = lamScope };
                break;
            case NativeClosure nc:
            {
                // 和 LambdaVal 同款:先查参数类型(交替机制靠它分流),再在**调用点作用域**里跑
                if (!Accepts(arg, nc.ParamType))
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
            case BoundTraitOp bto:
                _top = new ControlFrame(ControlKind.TraitOp,
                    RList<RuntimeValue>.Empty.Add(bto.Self).Add(arg).Add(new StringVal(bto.OpName)),
                    VoidVal.Instance)
                { Parent = sink, Scope = sink.Scope };
                break;
            // `T.GetImplements ()` / `I.GetImplementors ()` —— 类型与接口之间那两个方向的问题。
            // **当场算**:实现是登记在作用域里的,而求值器手里正好有当前作用域(内置方法的体没有),
            // 所以和 BoundClassOp 一样做成"按值分派"的一格。
            case BoundTraitQuery q:
                _top = sink.WithResult(q.Kind == TraitQueryKind.Implements
                    ? BuiltinClasses.Implements(this, q.Self)
                    : BuiltinClasses.Implementors(this, q.Self));
                break;
            case PartialCtor pc:
            {
                var cargs = RList<RuntimeValue>.Empty.Add(pc.Partial).Add(arg).Add(pc.Target);
                _top = new ControlFrame(ControlKind.CtorApply, cargs, VoidVal.Instance) { Parent = sink, Scope = sink.Scope, CallSite = (_top as NodeFrame)?.Node };
                break;
            }
            case ContinuationVal k:
                // 用 `Continuation f` **包**出来的那一枚:调它 = 调它包着的那枚函数
                // (库里 `callcc` 的包装就是这么派的 —— "先还原控制状态、再跳"写在那枚里,
                //  所以这里只是一次普通调用,和从前交个 lambda 出去是一样的走法)。
                if (k.Jump is not null)
                {
                    CallInto(sink, k.Jump, arg);
                    break;
                }
                // 续延 = callcc 之后的剩余计算。调用它:丢弃当前帧链,把 arg 当作
                // callcc 的返回值、从捕获点继续。丢弃当前链正是它能当「跳转」写循环的原因。
                //
                // `default` 那枚(还没到手)没有捕获点可跳,当场报错 —— 让它"什么都不做"
                // 会静默地把控制权留在原地,而调用方还以为跳走了(见 ContinuationVal.Default)。
                if (k.Captured is null)
                    throw new RuntimeException("这枚续延是 default（还没到手的那一枚），调不了 —— "
                                             + "能跳的续延只有 callcc 交出来的那种");
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
            // 剩下的普通函数(内置方法、转换器、C# 造的闭包…):它们的体是同步的,当场算。
            // **这一臂必须排在最后**:ClassVal/BoolVal/BlockVal/… 全是 `FunctionVal`,
            // 排在前面会把上面那些"按类型先分派"的分支全吃掉(症状是所有内置方法调用
            // 都报「值 <function> 不是函数,不能调用」)。
            case FunctionVal fv:
                _top = sink.WithResult(fv.Body(arg));
                break;
            // 非函数的值(原子值、普通对象、容器、模块…)：不能调
            default:
                throw new RuntimeException($"值 {fn} 不是函数，不能调用");
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
            throw new RuntimeException($"类型 {t.DisplayName} 不能作为构造器调用");   // 是类,但没类体(Every/Any/Void/Ravel/Scope/Property…)
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
