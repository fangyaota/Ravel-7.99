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
                    _top = new ControlFrame(cf.Kind, cf.Args.Add(arg), VoidVal.Instance) { Parent = sink, Scope = sink.Scope };
                else
                    _top = sink.WithResult(cf.Accumulate(arg));
                break;
            case LambdaVal lam:
                if (!arg.Type.IsAssignableTo(lam.ParamType))
                    throw new TypeMismatchException($"参数 '{lam.ParamName}' 需要 {lam.ParamType}，得到 {arg.Type}");
                var lamScope = lam.Scope.Push();
                lamScope.Define("self", BuiltinClasses.Function, lam);
                lamScope.Define(lam.ParamName, lam.ParamType, arg);
                _top = new BlockExecFrame(lam.Block) { Parent = sink, Scope = lamScope };
                break;
            case BlockVal blk:
                _top = new BlockExecFrame(blk.Block) { Parent = sink, Scope = blk.Scope.Push() };
                break;
            case ObjectVal ov:
            {
                // Ravel 层"能不能调用"的判据就是**有没有 `call` 成员** ——
                // 引擎因此不需要知道"什么是类",interface/shape 也可以建在同一套判据上
                if (ov.Scope.LookupField(ObjectVal.CallMember)?.Value is not FunctionVal raw)
                    throw new RuntimeException($"值 {ov} 不是函数，不能调用");
                var bound = ObjectVal.BindMethod(raw, ov);
                // 内置的 call 是 BoundCall,交给实例化那条路;用户自己定义的 call 直接同步调
                if (bound is BoundCall bc) CallClassInto(sink, bc.Self, arg);
                else _top = sink.WithResult(bound.Body(arg));
                break;
            }
            case ComposeVal comp:
            {
                var cargs = RList<RuntimeValue>.Empty.Add(comp.Original).Add(comp.Block).Add(arg).Add(new BoolVal(comp.IsPrepend));
                _top = new ControlFrame(ControlKind.Compose, cargs, VoidVal.Instance) { Parent = sink, Scope = sink.Scope };
                break;
            }
            case BoundClassOp bco:
                PushClassOp(sink, bco.OpName, bco.Self, arg);
                break;
            case PartialCtor pc:
            {
                var cargs = RList<RuntimeValue>.Empty.Add(pc.Partial).Add(arg).Add(pc.Target);
                _top = new ControlFrame(ControlKind.CtorApply, cargs, VoidVal.Instance) { Parent = sink, Scope = sink.Scope };
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
                _top = new BlockExecFrame(chosen.Block) { Parent = sink, Scope = chosen.Scope.Push() };
                break;
            default:
                if (fn is not FunctionVal fv) throw new RuntimeException($"值 {fn} 不是函数，不能调用");
                _top = sink.WithResult(fv.Body(arg));
                break;
        }
    }

    /// <summary>调用一个类对象 = 实例化它。
    ///
    /// 两条路(S1 阶段):
    /// - 有类体(用户类)→ 推 ClassInit 控制帧:建 scope、跑各层类体、调 init
    /// - 只有 Initializer(内置类)→ 同步转换器
    ///
    /// S2 会把两条合成一条:内置类也在预设类体里定义 `init`,构造一律交出 init 的返回值。</summary>
    private void CallClassInto(Frame sink, ObjectVal t, RuntimeValue arg)
    {
        if (t.Body != null)
        {
            _top = new ControlFrame(ControlKind.ClassInit, RList<RuntimeValue>.Empty.Add(t).Add(arg), VoidVal.Instance)
            {
                Parent = sink,
                Scope = sink.Scope
            };
            return;
        }

        var init = t.Initializer;
        // DisplayName 而不是 Name:用户类的名字是空的(`C := class {...}` 没有名字),
        // 直接插 Name 会报成「类型  不能作为构造器调用」,两个空格中间什么都没有
        if (init == null) throw new RuntimeException($"类型 {t.DisplayName} 不能作为构造器调用");
        _top = sink.WithResult(init.Body(arg));
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
