namespace Ravel.Runtime;

/// <summary>节点状态机:每个 AST 节点一个 NodeFrame,按 Results.Count 分阶段推进(求子节点 → 汇总)。
/// 需要「调用函数拿结果」的节点把 sink 传 nf.Parent(结果直接流进父帧),不再回到本帧。</summary>
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
            var getter = new BoxedValue(v.Value, this).GetMember("get").Value;
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

        var obj = nf.Result(0);
        var byGetter = BoxedValue.TryGetByGetter(this, obj, ma.Member);
        if (byGetter != null)
        {
            CallInto(nf.Parent!, byGetter, VoidVal.Instance);
            return;
        }

        Return(nf, new BoxedValue(obj, this).GetMember(ma.Member).Value);
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
                var setter = new BoxedValue(field.Value, this).GetMember("set").Value;
                if (setter is FunctionVal sf)
                {
                    PushCallReturn(nf.Parent!, sf, val, val);
                    return;
                }

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
            var bound = RuntimeType.BindMethod(fn, left);
            if (fn is not BuiltinMethodVal)
            {
                var target = bin.Left is IdentifierExpr id2 ? id2.Name : null;
                if (target == null) throw new RuntimeException("复合赋值目标必须是变量");
                PushCallAssign(nf, bound, right, target);
                return;
            }

            var r = bound.Body(right);
            if (bin.Left is IdentifierExpr id) nf.Scope.Assign(id.Name, r);
            else throw new RuntimeException("复合赋值目标必须是变量");
            Return(nf, r);
            return;
        }

        var builtin = left.Type.TryLookupMethod(bin.Op);
        if (builtin == null) throw new RuntimeException($"未知的二元运算符: {bin.Op}");
        var bound2 = RuntimeType.BindMethod(builtin, left);
        if (builtin is BuiltinMethodVal)
            Return(nf, bound2.Body(right));
        else
            CallInto(nf.Parent!, bound2, right);
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
        // count==1 已查过字段存在,这里再兜一次:字段在右侧求值期间被删掉时不至于 NRE
        var field2 = ov2.Scope.LookupField(ma.Member)
                     ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'");
        if (field2.HasAttr("by"))
        {
            var setter = new BoxedValue(field2.Value, this).GetMember("set").Value;
            if (setter is FunctionVal sf)
            {
                PushCallReturn(nf.Parent!, sf, rv, rv);
                return;
            }

            Return(nf, rv);
            return;
        }

        field2.Assign(rv);
        Return(nf, rv);
    }
}
