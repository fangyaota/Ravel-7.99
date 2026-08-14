namespace Ravel.Runtime;

/// <summary>显式持久帧栈求值器:Step() 循环逐帧推进,C# 栈恒平,递归深度=帧链长度</summary>
public partial class Interpreter
{
    /// <summary>隐式转换:失败返回 null</summary>
    private static RuntimeValue? TryConvert(RuntimeValue val, RuntimeType target)
    {
        try
        {
            return RuntimeType.ConvertDirect(target, val);
        }
        catch (RuntimeException)
        {
            return null;
        }
    }
    private Frame _top = null!;
    private RuntimeValue _result = VoidVal.Instance;
    private Scope _rootScope = null!;

    // 续延恢复状态:多发时重跑局部 onDone 到 resumeRoot 完成,然后返回 arg 给调用者
    private AstNode? _resumeStopNode;
    private Frame? _resumeCaller;
    private RuntimeValue _resumeValue = VoidVal.Instance;

    /// <summary>顶层入口:整个程序一个块根帧(帧链包含剩余语句,callcc 续延才能完整恢复)。scope 用 _rootScope(ravel 模块切换会改它)</summary>
    private RuntimeValue RunStack(Program p)
    {
        _rootScope = _global;
        _top = new BlockExecFrame(new BlockExpr(p.Statements)) { Scope = _rootScope };
        _result = VoidVal.Instance;
        while (_top != null) StepOnce();
        return _result;
    }

    /// <summary>切换作用域:顶层改 _rootScope,块内改最近的 BlockExecFrame 的 scope(ravel 模块用)</summary>
    private void SetAmbientScope(Scope scope)
    {
        var f = _top;
        while (f is not BlockExecFrame && f.Parent != null) f = f.Parent;
        f.Scope = scope;
        if (f.Parent == null) _rootScope = scope;
    }

    private void StepOnce()
    {
        CurrentScope = _top.Scope;
        switch (_top)
        {
            case NodeFrame nf: StepNode(nf); break;
            case BlockExecFrame bf: StepBlockExec(bf); break;
            case ControlFrame cf: StepControl(cf); break;
        }
    }

    /// <summary>完成帧:把结果交给父帧(父空=顶层完成)</summary>
    private void Return(Frame f, RuntimeValue v)
    {
        // 续延恢复:resumeRoot(值消费者帧)完成 → 返回 arg 给调用者,不继续程序
        if (_resumeStopNode != null && f is NodeFrame rnf && rnf.Node == _resumeStopNode)
        {
            _resumeStopNode = null;
            _top = _resumeCaller!.WithResult(_resumeValue);
            return;
        }

        _top = f.Parent == null ? null! : f.Parent.WithResult(v);
        _result = f.Parent == null ? v : _result;
    }

    private void PushChild(Frame parent, AstNode child) => _top = new NodeFrame(child) { Parent = parent, Scope = parent.Scope };

    // ======================== 节点状态机 ========================

    private void StepNode(NodeFrame nf)
    {
        switch (nf.Node)
        {
            case NumberLiteral nn: if (nf.Count == 0) Return(nf, nn.IsFloat ? new FloatVal(nn.Value) : new IntVal((int)nn.Value)); break;
            case StringLiteral ss: if (nf.Count == 0) Return(nf, new StringVal(ss.Value)); break;
            case VoidLiteral: if (nf.Count == 0) Return(nf, VoidVal.Instance); break;
            case IdentifierExpr id: StepIdent(nf, id); break;
            case BinaryExpr bin: StepBinary(nf, bin); break;
            case UnaryExpr un: StepUnary(nf, un); break;
            case CallExpr call: StepCall(nf, call); break;
            case MemberAccess ma: StepMemberAccess(nf, ma); break;
            case PipeExpr pipe: StepPipe(nf, pipe); break;
            case ListLiteral l: StepList(nf, l.Elements); break;
            case SetLiteral sl: StepSet(nf, sl.Elements); break;
            case DictLiteral dl: StepDict(nf, dl.Entries); break;
            case BlockExpr b: if (nf.Count == 0) Return(nf, new BlockVal(b, nf.Scope)); break;
            case LambdaExpr lam: if (nf.Count == 0) StepLambda(nf, lam); break;
            case VarDefinition v: StepVarDef(nf, v); break;
            case Assignment a: StepAssign(nf, a); break;
            case ExpressionStatement es: if (nf.Count == 0) PushChild(nf, es.Expr); else Return(nf, nf.Result(0)); break;
            default: throw new RuntimeException("无法求值该表达式");
        }
    }

    private void StepIdent(NodeFrame nf, IdentifierExpr id)
    {
        if (nf.Count > 0) return;
        var v = nf.Scope.LookupVar(id.Name);
        if (v == null) throw new RuntimeException($"未定义的变量 '{id.Name}'");
        if (v.HasAttr("unreadable")) throw new RuntimeException($"变量 '{id.Name}' 不可读取");
        if (v.HasAttr("outdated")) Console.Error.WriteLine("[outdated] " + id.Name);
        if (v.HasAttr("by"))
        {
            var getter = new BoxedValue(v.Value).GetMember("get").Value;
            if (getter is FunctionVal gf) CallInto(nf.Parent!, gf, VoidVal.Instance);
            else Return(nf, v.Value);
        }
        else
        {
            Return(nf, v.Value);
        }
    }

    private void StepCall(NodeFrame nf, CallExpr call)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, call.Function);
            return;
        }

        if (nf.Count == 1)
        {
            var fn = nf.Result(0);
            var e = call.Arguments[0];
            if (e is BlockExpr b) CallInto(nf.Parent!, fn, new BlockVal(b, nf.Scope));
            else PushChild(nf, e);
            return;
        }

        // count 2:callee + 参数都好了
        CallInto(nf.Parent!, nf.Result(0), nf.Result(1));
    }

    private void StepMemberAccess(NodeFrame nf, MemberAccess ma)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, ma.Object);
            return;
        }

        Return(nf, new BoxedValue(nf.Result(0)).GetMember(ma.Member).Value);
    }

    private void StepPipe(NodeFrame nf, PipeExpr pipe)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, pipe.Right);
            return;
        }

        if (nf.Count == 1)
        {
            PushChild(nf, pipe.Left);
            return;
        }

        if (nf.Result(1) is not FunctionVal fn) throw new RuntimeException("<| 左边必须是函数");
        CallInto(nf.Parent!, fn, nf.Result(0));
    }

    private void StepUnary(NodeFrame nf, UnaryExpr un)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, un.Operand);
            return;
        }

        var o = nf.Result(0);
        if (un.Op == "!")
        {
            if (o is not BoolVal bn) throw new RuntimeException("! 需要 bool 操作数");
            Return(nf, new BoolVal(!bn.Value));
        }
        else if (un.Op == "-")
        {
            if (o is not IntVal i) throw new RuntimeException("一元 '-' 需要 int");
            Return(nf, new IntVal(-i.Value));
        }
        else
        {
            throw new RuntimeException("未知的一元运算符: " + un.Op);
        }
    }

    private void StepList(NodeFrame nf, List<Expression> es)
    {
        if (nf.Count < es.Count)
        {
            PushChild(nf, es[nf.Count]);
            return;
        }

        var elems = new List<RuntimeValue>();
        for (int i = 0; i < nf.Count; i++) elems.Add(nf.Result(i));
        Return(nf, new ListVal(elems));
    }

    private void StepSet(NodeFrame nf, List<Expression> es)
    {
        if (nf.Count < es.Count)
        {
            PushChild(nf, es[nf.Count]);
            return;
        }

        var set = new HashSet<RuntimeValue>();
        for (int i = 0; i < nf.Count; i++) set.Add(nf.Result(i));
        Return(nf, new SetVal(set));
    }

    private void StepDict(NodeFrame nf, List<DictEntry> entries)
    {
        if (nf.Count < entries.Count)
        {
            PushChild(nf, entries[nf.Count].Value);
            return;
        }

        var dict = new Dictionary<string, RuntimeValue>();
        for (int i = 0; i < nf.Count; i++) dict[entries[i].Key] = nf.Result(i);
        Return(nf, new DictVal(dict));
    }

    private void StepLambda(NodeFrame nf, LambdaExpr lam)
    {
        var lam2 = new LambdaVal(lam.Param.Name, ResolveType(lam.Param.TypeName, nf.Scope), lam.Body) { Scope = nf.Scope };
        Return(nf, lam2);
    }

    private void StepVarDef(NodeFrame nf, VarDefinition v)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, v.Value);
            return;
        }

        var val = nf.Result(0);
        var dt = v.TypeAnnotation != null ? ResolveType(v.TypeAnnotation, nf.Scope) : val.Type;
        if (v.TypeAnnotation != null && !val.Type.IsAssignableTo(dt))
        {
            var cv = TryConvert(val, dt);
            if (cv != null) val = cv;
            else throw new RuntimeException($"类型不匹配: 无法将 {val.Type} 赋值给 {dt}");
        }

        var vr = nf.Scope.DefineOrReplace(v.Name, dt, val);
        if (v.Attrs != null)
            foreach (var a in v.Attrs)
                vr.SetAttr(a);
        if (v.Named && val is FunctionVal fn)
        {
            fn.Name = v.Name;
        }

        if (v.IsInit && v.Name != "init") nf.Scope.DefineOrReplace("init", dt, val);
        if (v is { IsOperator: true, OperatorName: not null } && v.Name != v.OperatorName)
            nf.Scope.DefineOrReplace(v.OperatorName, dt, val);
        Return(nf, VoidVal.Instance);
    }

    private void StepAssign(NodeFrame nf, Assignment a)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, a.Value);
            return;
        }

        var val = nf.Result(0);
        var field = nf.Scope.LookupVar(a.Name);
        if (field != null)
        {
            if (field.HasAttr("core") && UnsafeDepth == 0)
                throw new RuntimeException($"字段 '{a.Name}' 是核心字段，需要 unsafe");
            if (field.HasAttr("by"))
            {
                var setter = new BoxedValue(field.Value).GetMember("set").Value;
                if (setter is FunctionVal sf) Step.Run(sf.Trampolined(val));
                Return(nf, val);
                return;
            }

            field.Assign(val);
            Return(nf, VoidVal.Instance);
            return;
        }

        nf.Scope.Assign(a.Name, val);
        Return(nf, VoidVal.Instance);
    }

    // ======================== 二元 ========================

    private void StepBinary(NodeFrame nf, BinaryExpr bin)
    {
        if (bin is { Op: "=", Left: MemberAccess ma })
        {
            StepMemberAssign(nf, bin, ma);
            return;
        }

        if (bin.Op is "&&" or "||")
        {
            StepShortCircuit(nf, bin);
            return;
        }

        if (nf.Count == 0)
        {
            PushChild(nf, bin.Left);
            return;
        }

        if (nf.Count == 1)
        {
            PushChild(nf, bin.Right);
            return;
        }

        StepBinaryOp(nf, bin);
    }

    private void StepShortCircuit(NodeFrame nf, BinaryExpr bin)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, bin.Left);
            return;
        }

        if (nf.Count == 1)
        {
            var left = nf.Result(0);
            if (left is not BoolVal lb) throw new RuntimeException(bin.Op + " 左边必须是 bool");
            if (bin.Op == "&&" && !lb.Value)
            {
                Return(nf, left);
                return;
            }

            if (bin.Op == "||" && lb.Value)
            {
                Return(nf, left);
                return;
            }

            PushChild(nf, bin.Right);
            return;
        }

        var right = nf.Result(1);
        if (right is not BoolVal) throw new RuntimeException(bin.Op + " 右边必须是 bool");
        Return(nf, right);
    }

    private void StepBinaryOp(NodeFrame nf, BinaryExpr bin)
    {
        var left = nf.Result(0);
        var right = nf.Result(1);
        if (bin.Op == "=")
        {
            Return(nf, right);
            return;
        }

        if (bin.Op is "+=" or "-=" or "*=" or "/=" or "%=")
        {
            var op = bin.Op[..1];
            var fn = left.Type.TryLookupMethod(op);
            if (fn == null) throw new RuntimeException($"类型 {left.Type} 不支持运算符 '{op}'");
            var r = Step.Run(RuntimeType.BindMethod(fn, left).Trampolined(right));
            if (bin.Left is IdentifierExpr id) nf.Scope.Assign(id.Name, r);
            else throw new RuntimeException("复合赋值目标必须是变量");
            Return(nf, r);
            return;
        }

        var builtin = left.Type.TryLookupMethod(bin.Op);
        if (builtin == null) throw new RuntimeException($"未知的二元运算符: {bin.Op}");
        Return(nf, Step.Run(RuntimeType.BindMethod(builtin, left).Trampolined(right)));
    }

    private void StepMemberAssign(NodeFrame nf, BinaryExpr bin, MemberAccess ma)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, ma.Object);
            return;
        }

        if (nf.Count == 1)
        {
            var obj = nf.Result(0);
            if (obj is FunctionVal fn && ma.Member == "name")
            {
                PushChild(nf, bin.Right);
                return;
            }

            if (obj is not ObjectVal ov) throw new RuntimeException("无法给非对象设置字段");
            var field = ov.Scope.LookupField(ma.Member);
            if (field == null) throw new RuntimeException($"对象没有字段 '{ma.Member}'");
            if (field.HasAttr("core") && UnsafeDepth == 0)
                throw new RuntimeException($"字段 '{ma.Member}' 是核心字段，需要 unsafe");
            if (!CheckFieldAccess(field, ov))
                throw new RuntimeException($"字段 '{ma.Member}' 是{(field.HasAttr("private") ? "私有的" : "受保护的")}");
            PushChild(nf, bin.Right);
            return;
        }

        var obj2 = nf.Result(0);
        var rv = nf.Result(1);
        if (obj2 is FunctionVal fn2 && ma.Member == "name")
        {
            fn2.Name = ((StringVal)rv).Value;
            Return(nf, rv);
            return;
        }

        var ov2 = (ObjectVal)obj2;
        var field2 = ov2.Scope.LookupField(ma.Member);
        if (field2.HasAttr("by"))
        {
            var setter = new BoxedValue(field2.Value).GetMember("set").Value;
            if (setter is FunctionVal sf) Step.Run(sf.Trampolined(rv));
            Return(nf, rv);
            return;
        }

        field2.Assign(rv);
        Return(nf, rv);
    }

    // ======================== 块执行 ========================

    private void StepBlockExec(BlockExecFrame bf)
    {
        if (bf.Count < bf.Block.Statements.Count)
        {
            _top = new NodeFrame(bf.Block.Statements[bf.Count]) { Parent = bf, Scope = bf.Scope };
            return;
        }

        Return(bf, bf.Count == 0 ? VoidVal.Instance : bf.Last);
    }

    // ======================== 调用分派 ========================

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
                if (!arg.Type.IsAssignableTo(lam.ParamType)) throw new TypeMismatchException();
                var lamScope = lam.Scope.Push();
                lamScope.Define("self", RuntimeType.Function, lam);
                lamScope.Define(lam.ParamName, lam.ParamType, arg);
                _top = new BlockExecFrame(lam.Body) { Parent = sink, Scope = lamScope };
                break;
            case BlockVal blk:
                _top = new BlockExecFrame(blk.Block) { Parent = sink, Scope = blk.Scope.Push() };
                break;
            case TypeVal t:
                CallTypeInto(sink, t, arg);
                break;
            case ComposeVal comp:
            {
                var cargs = RList<RuntimeValue>.Empty.Add(comp.Original).Add(comp.Block).Add(arg).Add(new BoolVal(comp.IsPrepend));
                _top = new ControlFrame(ControlKind.Compose, cargs, VoidVal.Instance) { Parent = sink, Scope = sink.Scope };
                break;
            }
            case ContinuationVal k:
                if (CallccActive)
                {
                    // 单发:abort body,续延返回 arg 给 callcc 的消费者
                    CallccActive = false;
                    _top = k.Captured.WithResult(arg);
                }
                else
                {
                    ResumeContinuation(k, arg);
                }

                break;
            default:
                var s = ((FunctionVal)fn).Trampolined(arg);
                if (s is not Done d) throw new RuntimeException("同步调用返回了未完成步骤");
                _top = sink.WithResult(d.Value);
                break;
        }
    }

    private void CallTypeInto(Frame sink, TypeVal t, RuntimeValue arg)
    {
        // 用户 class:Body 非空 → 推类构造控制帧(StepClassInit)
        if (t.Value.Body != null)
        {
            _top = new ControlFrame(ControlKind.ClassInit, RList<RuntimeValue>.Empty.Add(t).Add(arg), VoidVal.Instance)
            {
                Parent = sink,
                Scope = sink.Scope
            };
            return;
        }

        // 内建类型:同步构造器(转换器)
        var init = t.Value.Initializer;
        if (init == null) throw new RuntimeException($"类型 {t.Value.Name} 不能作为构造器调用");
        var s = init.Trampolined(arg);
        if (s is not Done d) throw new RuntimeException("类型构造返回了未完成步骤");
        _top = sink.WithResult(d.Value);
    }

    // ======================== 控制帧 ========================

    private void StepControl(ControlFrame cf)
    {
        switch (cf.Kind)
        {
            case ControlKind.While: StepWhile(cf); break;
            case ControlKind.If: StepIf(cf); break;
            case ControlKind.With: StepWith(cf); break;
            case ControlKind.Foreach: StepForeach(cf); break;
            case ControlKind.CallCC: StepCallCC(cf); break;
            case ControlKind.Using: StepUsing(cf); break;
            case ControlKind.Eval: StepEval(cf); break;
            case ControlKind.Alternate: StepAlternate(cf); break;
            case ControlKind.ClassInit: StepClassInit(cf); break;
            case ControlKind.Compose: StepCompose(cf); break;
        }
    }

    private static BlockVal GetBlock(RuntimeValue v) => (BlockVal)v;

    private void StepWhile(ControlFrame cf)
    {
        var cond = GetBlock(cf.Args.At(0));
        var body = GetBlock(cf.Args.At(1));
        if (cf.Count == 0)
        {
            _top = new BlockExecFrame(cond.Block) { Parent = cf, Scope = cond.Scope.Push() };
            return;
        }

        if (cf.Count == 1)
        {
            if (cf.Result(0) is not BoolVal bv) throw new RuntimeException("while 条件必须是 bool");
            if (bv.Value)
                _top = new BlockExecFrame(body.Block) { Parent = cf, Scope = body.Scope.Push() };
            else
                Return(cf, cf.State);
            return;
        }

        // count 2:body 完成 → 循环
        var newCf = cf with { State = cf.Result(1), Results = RList<RuntimeValue>.Empty };
        _top = new BlockExecFrame(cond.Block) { Parent = newCf, Scope = cond.Scope.Push() };
    }

    private void StepIf(ControlFrame cf)
    {
        var c = GetBlock(cf.Args.At(0));
        var t = GetBlock(cf.Args.At(1));
        var e = GetBlock(cf.Args.At(2));
        if (cf.Count == 0)
        {
            _top = new BlockExecFrame(c.Block) { Parent = cf, Scope = c.Scope.Push() };
            return;
        }

        if (cf.Count == 1)
        {
            if (cf.Result(0) is not BoolVal b) throw new RuntimeException("if 条件必须是 bool");
            var branch = b.Value ? t : e;
            _top = new BlockExecFrame(branch.Block) { Parent = cf, Scope = branch.Scope.Push() };
            return;
        }

        Return(cf, cf.Result(1));
    }

    private void StepWith(ControlFrame cf)
    {
        var obj = cf.Args.At(0);
        var body = GetBlock(cf.Args.At(1));
        if (cf.Count == 0)
        {
            var copy = obj switch
            {
                ObjectVal ov => new ObjectVal(ov.ClassType, RuntimeType.CopyScope(ov.Scope)),
                ListVal lv => new ListVal([.. lv.Elements]),
                SetVal sv => new SetVal([.. sv.Elements]),
                DictVal dv => new DictVal(new Dictionary<string, RuntimeValue>(dv.Entries)),
                _ => obj
            };
            if (copy is ObjectVal copyObj)
                copyObj.Scope.DefineOrReplace("this", copyObj.ClassType, copyObj);
            var newCf = cf with { State = copy };
            var bodyScope = copy is ObjectVal ov2 ? ov2.Scope.Push() : body.Scope.Push();
            _top = new BlockExecFrame(body.Block) { Parent = newCf, Scope = bodyScope };
            return;
        }

        Return(cf, cf.State);
    }

    private void StepForeach(ControlFrame cf)
    {
        var items = ((ListVal)cf.Args.At(0)).Elements;
        var fn = cf.Args.At(1);
        if (cf.Count < items.Count)
        {
            CallInto(cf, fn, items[cf.Count]);
            return;
        }

        Return(cf, cf.Count == 0 ? VoidVal.Instance : cf.Last);
    }

    private void StepCallCC(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            CallccActive = true;
            var fn = cf.Args.At(0);
            var k = new ContinuationVal(cf);
            CallInto(cf, fn, k);
            return;
        }

        CallccActive = false;
        Return(cf, cf.Result(0));
    }

    /// <summary>多发续延:重跑局部 onDone(resumeRoot 消费 arg)到其完成,再把 arg 返回给 k 的调用者</summary>
    private void ResumeContinuation(ContinuationVal k, RuntimeValue arg)
    {
        var resumeRoot = k.Captured.Parent;
        if (resumeRoot == null)
        {
            _top = null!;
            _result = arg;
            return;
        }

        _resumeStopNode = (resumeRoot as NodeFrame)?.Node;
        _resumeCaller = _top.Parent;
        _resumeValue = arg;
        _top = resumeRoot.WithResult(arg);
    }

    private void StepUsing(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var path = ((StringVal)cf.Args.At(0)).Value;
            var ast = LoadModuleAst(path);
            if (ast == null)
            {
                Return(cf, VoidVal.Instance);
                return;
            }

            _top = new BlockExecFrame(ast) { Parent = cf, Scope = cf.Scope };
            return;
        }

        Return(cf, VoidVal.Instance);
    }

    private void StepEval(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var code = ((StringVal)cf.Args.At(0)).Value;
            var lexer = new Lexer(code);
            var ast = new Parser(lexer.Tokenize()).Parse();
            _top = new BlockExecFrame(new BlockExpr(ast.Statements)) { Parent = cf, Scope = cf.Scope };
            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>函数交替:先试 f,类型不匹配则试 g</summary>
    private void StepAlternate(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var f = cf.Args.At(0);
            var g = cf.Args.At(1);
            var arg = cf.Args.At(2);
            try
            {
                CallInto(cf, f, arg);
            }
            catch (TypeMismatchException)
            {
                CallInto(cf, g, arg);
            }

            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>类实例化:跑类体收集字段 → 打包 ObjectVal 绑 this/base → 调 init</summary>
    private void StepClassInit(ControlFrame cf)
    {
        var typeVal = (TypeVal)cf.Args.At(0);
        var arg = cf.Args.At(1);
        var type = typeVal.Value;
        var body = type.Body!;
        if (cf.Count == 0)
        {
            var instanceScope = new Scope(body.Scope);
            var newCf = cf with { State = new ScopeVal(instanceScope) };
            _top = new BlockExecFrame(body.Block) { Parent = newCf, Scope = instanceScope };
            return;
        }

        if (cf.Count == 1)
        {
            var instanceScope = ((ScopeVal)cf.State).Scope;
            var obj = new ObjectVal(type, instanceScope);
            instanceScope.Define("this", type, obj);
            RuntimeType.DefineBase(instanceScope, type);
            var init = RuntimeType.CollectInit(instanceScope);
            if (init != null)
            {
                var newCf = cf with { State = obj };
                CallInto(newCf, init, arg);
                return;
            }

            throw new RuntimeException($"类型 {type.Name} 没有构造器（init）");
        }

        // count 2:init 完成 → 返回对象
        Return(cf, cf.State);
    }

    /// <summary>prepend/append 合成:先跑块再调原函数,或先调原函数再跑块</summary>
    private void StepCompose(ControlFrame cf)
    {
        var original = cf.Args.At(0);
        var block = GetBlock(cf.Args.At(1));
        var arg = cf.Args.At(2);
        var isPrepend = ((BoolVal)cf.Args.At(3)).Value;
        if (isPrepend)
        {
            if (cf.Count == 0)
            {
                _top = new BlockExecFrame(block.Block) { Parent = cf, Scope = block.Scope.Push() };
                return;
            }

            if (cf.Count == 1)
            {
                CallInto(cf, original, arg);
                return;
            }

            Return(cf, cf.Result(1));
            return;
        }

        // append
        if (cf.Count == 0)
        {
            CallInto(cf, original, arg);
            return;
        }

        if (cf.Count == 1)
        {
            _top = new BlockExecFrame(block.Block) { Parent = cf, Scope = block.Scope.Push() };
            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>加载模块文件,解析为 BlockExpr;找不到/循环/已加载时返回 null(已加载→返回空块)</summary>
    private BlockExpr? LoadModuleAst(string path)
    {
        var refs = new List<string> { "/workspace/ravel/lib/", "/workspace/ravel/", "./", "lib/" };
        var rv = _global.TryLookup("references");
        if (rv?.Value is ListVal lv) refs = [.. lv.Elements.Select(e => ((StringVal)e).Value), .. refs];
        string? full = null;
        foreach (var d in refs)
        {
            var p = System.IO.Path.Combine(d, path);
            if (System.IO.File.Exists(p)) { full = p; break; }
        }

        if (full == null)
            foreach (var d in refs)
            {
                var p = System.IO.Path.Combine(d, path + ".rav");
                if (System.IO.File.Exists(p)) { full = p; break; }
            }

        if (full == null) throw new RuntimeException("找不到文件: " + path);
        full = System.IO.Path.GetFullPath(full);
        if (_loading.Contains(full)) throw new RuntimeException("检测到循环引用: " + path);
        if (_loaded.Contains(full)) return null;
        _loaded.Add(full);
        _loading.Push(full);
        try
        {
            var src = System.IO.File.ReadAllText(full);
            var lexer = new Lexer(src);
            var ast = new Parser(lexer.Tokenize()).Parse();
            return new BlockExpr(ast.Statements);
        }
        finally
        {
            _loading.Pop();
        }
    }
}
