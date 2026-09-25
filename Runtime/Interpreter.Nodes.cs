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

    /// <summary>数字文字 → 值。整数按字面量文本定类型:装得下 int 就是 int,装不下退化成 bigint。
    /// 以前一律 (int) 硬转,2147483648 静默绕成 -2147483648,而且 double 中转还会丢精度。</summary>
    private static RuntimeValue MakeNumber(NumberLiteral nn)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (nn.IsFloat) return new FloatVal(double.Parse(nn.Lexeme, ci));
        return int.TryParse(nn.Lexeme, System.Globalization.NumberStyles.None, ci, out var i)
            ? new IntVal(i)
            : new BigIntVal(System.Numerics.BigInteger.Parse(nn.Lexeme, ci));
    }

    private void StepNode(NodeFrame nf)
    {
        switch (nf.Node)
        {
            case NumberLiteral nn: if (nf.Count == 0) Return(nf, MakeNumber(nn)); break;
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
        BoxedValue.GateRead(v, id.Name, this);   // 和成员访问共用一套门禁,别各抄一份
        if (v.HasAttr(Attr.By))
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

    /// <summary>一元负号。每种数值类型自己翻,不走 `0 - x`——Int 的 '-' 只特判了 Float,
    /// `0 - bigint` 会撞上「运算符 '-' 不支持 BigInt 操作数」。</summary>
    private static RuntimeValue Negate(RuntimeValue v) => v switch
    {
        IntVal i => new IntVal(-i.Value),
        FloatVal f => new FloatVal(-f.Value),
        BigIntVal b => new BigIntVal(-b.Value),
        FractionVal fr => new FractionVal(-fr.Num, fr.Den),
        BigFractionVal bf => new BigFractionVal(-bf.Num, bf.Den),
        _ => throw new RuntimeException($"一元 '-' 不支持 {v.Type}"),
    };

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
            Return(nf, Negate(o));
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

        // 运算符没有别名可言:符号本身就是变量名(`+ := f` 定义的就是 `+`)
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
            if (field.HasAttr(Attr.Core) && UnsafeDepth == 0)
                throw new RuntimeException($"字段 '{a.Name}' 是核心字段，需要 unsafe");
            if (field.HasAttr(Attr.By))
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

}