namespace Ravel.Runtime;

public partial class Interpreter
{
    // 一切"这一格收不下"都走这条路:**这一层自己摆着 `reject` 就叫它**(`|` 每次调用会
    // 用 `Prepend` 往被接函数那一层塞一枚),没摆就抛 `TypeMismatchException` ——
    // 于是它成了一枚普通错误,谁接错误就是谁接(`try`)。
    private void Reject(Frame sink, Scope? scope, string message)
    {
        // 这一层**自己**摆着 `reject` 吗(`|` 摆的就是这一格)?摆了就直接叫它 ——
        // 参数类型对不上是**同步**发生的,当场换支最省事。没摆就走错误那条路。
        if (scope?.OwnField("reject")?.Value is FunctionVal fn)
        {
            CallInto(sink, fn, BuiltinClasses.NewException(message, ErrorKind.Type));
            return;
        }

        throw new TypeMismatchException(message);
    }

    private void CallInto(Frame sink, RuntimeValue fn, RuntimeValue arg, Scope? callScope = null)
    {
        switch (fn)
        {
            case ControlFunction cf:
                // **声明过类型的那几格**照下面 `NativeClosure` 那一支的查法:收不下就当场拒收。
                // 没声明的格子照旧 —— 控制帧本来就是按**个数**收的。
                if (cf.Declared is { } decls && cf.Args.Count < decls.Count
                    && decls[cf.Args.Count] is { } d && !Accepts(arg, d.Type))
                {
                    Reject(sink, null, $"参数 '{d.Name}' 需要 {d.Type}，得到 {arg.Type}");
                    return;
                }

                if (cf.IsFinalAfter(1))
                    _top = new ControlFrame(cf.Kind, cf.Args.Add(arg), VoidVal.Instance) { Parent = sink, Scope = sink.Scope, CallSite = (_top as NodeFrame)?.Node };
                else
                    _top = sink.WithResult(cf.Accumulate(arg));
                break;
            case LambdaVal lam:
                // 参数的类型注解**不在这里判** —— 它是一次普通的 `x : T`,由 `Lowering` 发在
                // 体最前头(`(x: int) => …` 展开成 `{ x : int | reject; … }`)。所以引擎里只有
                // 一份判据(`:` 自己),绑参数这步只管绑。
                var lamScope = callScope ?? lam.CaptureScope.Push();
                lamScope.Define("self", lam);
                var paramVar = lamScope.Define(lam.ParamName, arg);
                // 修饰符装到**那个绑定**上 —— 和变量定义一个待遇(`Interpreter.Nodes` 里
                // VarDefinition 那段)。于是 `readonly` 真有检查:下面体里给这个参数赋值,
                // 走的是同一个 `CheckWritable`,报「无法给只读变量 'y' 赋值」。
                if (lam.ParamAttrs != null)
                    foreach (var a in lam.ParamAttrs)
                        paramVar.SetAttr(a);
                // **参数默认是 `private` 的** —— 它是这个函数的私事(普通定义默认 `public`)。
                // 判据是"用户**没明写**这三个里的任何一个":写了一个(哪怕写的是 `public`)
                // 就以他写的为准,不是叠上去。
                //
                // 这条**读得到**((currentScope ()).Lookup "x" 那枚 property 的 `.Attrs ()`
                // 就是出口),但**不拦谁** —— `BoxedValue` 那条"模块外面读不到"走的是
                // **成员查找**,而参数不是成员。"和变量定义一个待遇"这条规矩盖到它,
                // 于是 `(z: int) => …` 体里读得出 `[private]`。见 CONTEXT.md。
                if (lam.ParamAttrs is null
                    || !lam.ParamAttrs.Any(a => a is Attr.Public or Attr.Private or Attr.Protected))
                    paramVar.SetAttr(Attr.Private);
                // 柯里化的体跑完交回的是**内层那个 lambda** —— 那是个半成品(还等着实参),
                // 收尾时打个标(见 BlockExecFrame.Curried / FunctionVal.IsPartial)
                _top = new BlockExecFrame(lam.Block) { Parent = sink, Scope = lamScope, Curried = lam.Block.Curried };
                break;
            case NativeClosure nc:
            {
                // 和 LambdaVal 同款:先查参数类型(交替机制靠它分流),再在**调用点作用域**里跑
                if (!Accepts(arg, nc.ParamType))
                {
                    Reject(sink, null, $"参数 '{nc.ParamName}' 需要 {nc.ParamType}，得到 {arg.Type}");
                    return;
                }
                var ncScope = _top.Scope.Push();
                ncScope.Define("self", nc);
                ncScope.Define(nc.ParamName, arg);
                _top = sink.WithResult(nc.Fn(ncScope, arg));
                break;
            }
            // **裸块不推层**(试验):`{ … }` 是**写在原地**的一截,不是闭包 ——
            // 没有参数要绑(lambda 那条路推完层要 `Define ("self")` + `Define (参数名)`,
            // 那一层省不掉),于是它就是唯一有资格不要自己那层的。
            // 直接在**捕获作用域**里跑:块里 `:=` 的定义落在外层。
            case BlockVal blk:
                _top = new BlockExecFrame(blk.Block) { Parent = sink, Scope = callScope ?? blk.CaptureScope };
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
            case BoundTraitOp bto:
                _top = new ControlFrame(ControlKind.TraitOp,
                    RList<RuntimeValue>.Empty.Add(bto.Self).Add(arg).Add(new StringVal(bto.OpName)),
                    VoidVal.Instance)
                { Parent = sink, Scope = sink.Scope };
                break;
            // `T.GetImplements ()` / `I.GetImplementors ()` —— 类型与接口之间那两个方向的问题。
            // **当场算**:实现是登记在作用域里的,而求值器手里正好有当前作用域(内置方法的体没有),
            // 所以做成"按值分派"的一格。
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
                                             + "能跳的续延只有 callcc 交出来的那种", ErrorKind.Value);
                // 引擎自己那份控制状态(模块加载栈)盖回捕获那一刻 —— 见 `ContinuationVal.Loading`。
                // **放在跳之前**:和库里原来那两句的顺序一样(先还原、再 `k x`)。
                RestoreLoadingIfNeeded(k.Loading);
                _top = k.Captured.WithResult(arg);
                break;
            case BoolVal bv:
                // true/false 是函数(lisp 式):收两个块,返回选中那个块的结果
                if (arg is not BlockVal thenBlock) throw new RuntimeException("true/false 需要两个代码块", ErrorKind.Argument);
                _top = sink.WithResult(new PartialBool(bv.Value, thenBlock));
                break;
            case PartialBool pb:
                if (arg is not BlockVal elseBlock) throw new RuntimeException("true/false 需要两个代码块", ErrorKind.Argument);
                var chosen = pb.Value ? pb.Then : elseBlock;
                // 同上:`cond { A } { B }` 选中那支是**裸块**,不推层
                _top = new BlockExecFrame(chosen.Block) { Parent = sink, Scope = chosen.CaptureScope };
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
                throw new RuntimeException($"值 {fn} 不是函数，不能调用", ErrorKind.Type);
        }
    }

    private void CallClassInto(Frame sink, ClassVal t, RuntimeValue arg)
    {
        if (t.ClassBody == null)
            throw new RuntimeException($"类型 {t.DisplayName} 不能作为构造器调用", ErrorKind.Type);   // 是类,但没类体(Every/Any/Void/Ravel/Scope/Property…)
        _top = new ControlFrame(ControlKind.ClassInit, RList<RuntimeValue>.Empty.Add(t).Add(arg), VoidVal.Instance)
        {
            Parent = sink,
            Scope = sink.Scope,
            CallSite = (_top as NodeFrame)?.Node,
        };
    }

    // ======================== 合成控制帧的推帧助手 ========================

    private void PushCallAssign(NodeFrame nf, FunctionVal bound, RuntimeValue arg, string target)
    {
        var args = RList<RuntimeValue>.Empty.Add(bound).Add(arg).Add(new StringVal(target));
        _top = new ControlFrame(ControlKind.CallAssign, args, VoidVal.Instance) { Parent = nf.Parent, Scope = nf.Scope };
    }

    private void PushCallReturn(Frame parent, FunctionVal fn, RuntimeValue arg, RuntimeValue result)
    {
        var args = RList<RuntimeValue>.Empty.Add(fn).Add(arg).Add(result);
        _top = new ControlFrame(ControlKind.CallReturn, args, VoidVal.Instance) { Parent = parent, Scope = parent.Scope };
    }
}
