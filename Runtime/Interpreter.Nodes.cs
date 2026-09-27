namespace Ravel.Runtime;

/// <summary>节点状态机:每个 AST 节点一个 NodeFrame,按 Results.Count 分阶段推进(求子节点 → 汇总)。
/// 需要「调用函数拿结果」的节点把 sink 传 nf.Parent(结果直接流进父帧),不再回到本帧。</summary>
public partial class Interpreter
{
    /// <summary>隐式转换:失败返回 null,把失败原因从 out 带走。
    /// 原因要带——`x: int = bigint 9999999999999` 光说「无法将 BigInt 赋值给 Integer」,
    /// 看不出是「类型根本不支持转换」还是「转得动但这个值超出 int 范围」,后者才是真话。</summary>
    private static RuntimeValue? TryConvert(RuntimeValue val, ObjectVal target, out string? why)
    {
        try
        {
            why = null;
            return BuiltinClasses.ConvertDirect(target, val);
        }
        catch (RuntimeException ex)
        {
            why = ex.Message;
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
            case LiteralExpr le: if (nf.Count == 0) Return(nf, le.Value); break;
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
            case LambdaExpr lam: StepLambda(nf, lam); break;   // 分相推进(注解要先求值),别加 Count==0 的守卫
            case VarDefinition v: StepVarDef(nf, v); break;
            case Assignment a: StepAssign(nf, a); break;
            case ExpressionStatement es: if (nf.Count == 0) PushChild(nf, es.Expr); else Return(nf, nf.Result(0)); break;
            // 走到这里说明 AST 里有个节点类型没接上状态机——报出节点类型才查得下去
            default: throw new RuntimeException($"无法求值的节点类型: {nf.Node.GetType().Name}");
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
            var getter = new BoxedValue(v.Value, this).GetMember("Get").Value;
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
            // 块参数就地包上**调用点**的作用域,不必再走一个子帧
            // (块要晚到被调用时才跑,所以作用域得在这里定下来)
            if (call.Argument is BlockExpr b) CallInto(nf.Parent!, nf.Result(0), new BlockVal(b, nf.Scope));
            else PushChild(nf, call.Argument);
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
        // -int.MinValue 翻不过来(2147483648 装不下),别静默回绕成它自己
        IntVal i => BuiltinClasses.Narrow(-(long)i.Value, $"-({i.Value})"),
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

    /// <summary>lambda 的参数注解是个**表达式**,在建这个 lambda 的时候求值一次。
    /// 常见写法就是一个类型名(`int`),也可以是算出来的(`(pickType ())`)。</summary>
    private void StepLambda(NodeFrame nf, LambdaExpr lam)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, lam.Param.Type);
            return;
        }

        var pt = AsClass(nf.Result(0), lam.Param.Name);
        Return(nf, new LambdaVal(lam.Param.Name, lam.Param.Type, pt, lam.Body) { CaptureScope = nf.Scope });
    }

    /// <summary>注解求出来的值得是个类对象。
    ///
    /// 注解是表达式,所以"名字打错"会在求值那一步就报(未定义的变量),到这里只剩
    /// "算出来的东西不是类型"这一种失败。</summary>
    private static ObjectVal AsClass(RuntimeValue v, string what)
        => v as ObjectVal is { IsClass: true } cls
            ? cls
            : throw new RuntimeException($"'{what}' 的类型注解要是个类型，得到 {v.Type}");

    private void StepVarDef(NodeFrame nf, VarDefinition v)
    {
        // 有注解就先求注解(它是个表达式,可能算出一个类),再求值 —— 阶段由 Count 推进
        var hasType = v.TypeAnnotation != null;
        if (hasType && nf.Count == 0)
        {
            PushChild(nf, v.TypeAnnotation!);
            return;
        }

        var valueAt = hasType ? 1 : 0;
        if (nf.Count == valueAt)
        {
            PushChild(nf, v.Value);
            return;
        }

        var val = nf.Result(valueAt);
        var dt = hasType ? AsClass(nf.Result(0), v.Name) : val.Type;
        // 没写注解时 dt 就是值自己的类型,一定"可赋值" —— 不必再判一次注解在不在
        if (!val.Type.IsAssignableTo(dt))
        {
            var cv = TryConvert(val, dt, out var why);
            if (cv != null) val = cv;
            // 用 `—` 而不是括号:why 自己常带括号(「超出 int 范围(大数用 bigint)」),
            // 套起来会变成双层括号
            else throw new RuntimeException($"类型不匹配: 无法将 {val.Type} 赋值给 {dt} — {why}");
        }

        var vr = nf.Scope.DefineOrReplace(v.Name, dt, val);
        if (v.Attrs != null)
            foreach (var a in v.Attrs)
                vr.SetAttr(a);
        // `::=` 同时给函数和**类对象**命名:两者的 Name 都是成员表里的 `name` 成员
        if (v.Named && val is ObjectVal named)
        {
            named.Name = v.Name;
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
            if (field.HasAttr(Attr.Core) && !IsUnsafe)
                throw new RuntimeException($"字段 '{a.Name}' 是核心字段，需要 unsafe");
            if (field.HasAttr(Attr.By))
            {
                var setter = new BoxedValue(field.Value, this).GetMember("Set").Value;
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