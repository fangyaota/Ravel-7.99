namespace Ravel.Runtime;

using System.IO;
using System.Linq;
using System.Numerics;

public partial class Interpreter
{
    // ======================== 表达式 ========================

    /// <summary>求值标识符：查作用域获取变量值，处理 by 属性 / unreadable / outdated</summary>
    private Step EvalIdent(IdentifierExpr id)
    {
        var v = CurrentScope.Lookup(id.Name);
        if (v.HasAttr("unreadable")) return ThrowRavel("变量 '" + id.Name + "' 不可读取");
        if (v.HasAttr("outdated")) Console.Error.WriteLine("[outdated] " + id.Name);
        if (v.HasAttr("by"))
        {
            var prop = v.Value;
            var getter = new BoxedValue(prop).GetMember("get").Value;
            if (getter is FunctionVal gf)
                return ToDone(Step.Run(gf.Trampolined([VoidVal.Instance])));
        }

        return ToDone(v.Value);
    }

    /// <summary>表达式求值入口：按 AST 节点类型分发</summary>
    public Step EvalExpr(AstNode n) => n switch
    {
        NumberLiteral nn => ToDone(nn.IsFloat
            ? new FloatVal(nn.Value)
            : new IntVal((int)nn.Value)),
        StringLiteral ss => ToDone(new StringVal(ss.Value)),
        IdentifierExpr id => EvalIdent(id),
        VoidLiteral => ToDone(VoidVal.Instance),
        BinaryExpr bin => EvalBinary(bin),
        UnaryExpr un => EvalUnary(un),
        CallExpr call => EvalCall(call),
        MemberAccess ma => EvalMemberAccess(ma),
        PipeExpr pipe => EvalPipe(pipe),
        ListLiteral l => EvalList(l.Elements),
        SetLiteral sl => EvalSet(sl.Elements),
        DictLiteral dl => EvalDict(dl.Entries),
        BlockExpr b => EvalBlock(b),
        LambdaExpr lam => ToDone(EvalLambda(lam)),
        _ => ThrowRavel("无法求值该表达式")
    };

    // ======================== 列表 / 集合 / 字典字面量 ========================

    /// <summary>求值列表字面量 [a b c] → ListVal</summary>
    private Step EvalList(List<Expression> es) => EvalListRec(es, 0, []);

    /// <summary>列表递归求值：逐元素求值后收集</summary>
    private Step EvalListRec(List<Expression> es, int i, List<RuntimeValue> acc)
    {
        if (i >= es.Count) return ToDone(new ListVal(acc));
        return Then(EvalExpr(es[i]), v =>
        {
            acc.Add(v);
            return EvalListRec(es, i + 1, acc);
        });
    }

    /// <summary>求值集合字面量 {a b c} → SetVal</summary>
    private Step EvalSet(List<Expression> es) => EvalSetRec(es, 0, []);

    /// <summary>集合递归求值：逐元素求值后去重收集</summary>
    private Step EvalSetRec(List<Expression> es, int i, HashSet<RuntimeValue> acc)
    {
        if (i >= es.Count) return ToDone(new SetVal(acc));
        return Then(EvalExpr(es[i]), v =>
        {
            acc.Add(v);
            return EvalSetRec(es, i + 1, acc);
        });
    }

    /// <summary>求值字典字面量 {k: v k2: v2} → DictVal</summary>
    private Step EvalDict(List<DictEntry> entries) => EvalDictRec(entries, 0, []);

    /// <summary>字典递归求值：逐条目求值后收集</summary>
    private Step EvalDictRec(List<DictEntry> entries, int i, Dictionary<string, RuntimeValue> acc)
    {
        if (i >= entries.Count) return ToDone(new DictVal(acc));
        return Then(EvalExpr(entries[i].Value), v =>
        {
            acc[entries[i].Key] = v;
            return EvalDictRec(entries, i + 1, acc);
        });
    }

    /// <summary>隐式类型转换：尝试将值转换为目标类型，失败返回 null</summary>
    private static RuntimeValue? TryConvert(RuntimeValue val, RuntimeType target)
    {
        if (target == RuntimeType.Int || target == RuntimeType.Float || target == RuntimeType.String ||
            target == RuntimeType.Bool || target == RuntimeType.BigInt)
        {
            try
            {
                return RuntimeType.ConvertDirect(target, val);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>浅拷贝作用域（with / Copy 用）</summary>
    private static Scope CopyScope(Scope src)
    {
        var dst = new Scope(src.Parent);
        foreach (var kv in src.Variables)
            dst.Define(kv.Key, kv.Value.TypeConstraint, kv.Value.Value);
        return dst;
    }

    // ======================== 二元运算符 ========================

    /// <summary>求值二元表达式：短路逻辑 / 赋值 / 复合赋值 / 自定义运算符 / 内置运算符 / 位/逻辑</summary>
    private Step EvalBinary(BinaryExpr bin)
    {
        if (bin.Op == "&&")
            return Then(EvalExpr(bin.Left), left =>
            {
                if (left is not BoolVal lb) return ThrowRavel("&& 左边必须是 bool");
                if (!lb.Value) return ToDone(left);
                return Then(EvalExpr(bin.Right), right =>
                {
                    if (right is not BoolVal) return ThrowRavel("&& 右边必须是 bool");
                    return ToDone(right);
                });
            });
        if (bin.Op == "||")
            return Then(EvalExpr(bin.Left), left =>
            {
                if (left is not BoolVal lb) return ThrowRavel("|| 左边必须是 bool");
                if (lb.Value) return ToDone(left);
                return Then(EvalExpr(bin.Right), right =>
                {
                    if (right is not BoolVal) return ThrowRavel("|| 右边必须是 bool");
                    return ToDone(right);
                });
            });
        if (bin is { Op: "=", Left: MemberAccess ma })
            return Then(EvalExpr(ma.Object), obj =>
            {
                if (obj is FunctionVal fn && ma.Member == "name")
                {
                    return Then(EvalExpr(bin.Right), rv =>
                    {
                        fn.Name = ((StringVal)rv).Value;
                        return ToDone(rv);
                    });
                }

                if (obj is not ObjectVal ov) return ThrowRavel("无法给非对象设置字段");

                Variable? field = null;
                if (ov.Scope.Contains(ma.Member))
                {
                    field = ov.Scope.Lookup(ma.Member);
                }

                if (field == null) return ThrowRavel($"对象没有字段 '{ma.Member}'");
                if (field.HasAttr("by"))
                {
                    return Then(EvalExpr(bin.Right), rv =>
                    {
                        var prop = field.Value;
                        var setter = new BoxedValue(prop).GetMember("set").Value;
                        if (setter is FunctionVal sf)
                            Step.Run(sf.Trampolined([rv]));
                        return ToDone(rv);
                    });
                }

                return Then(EvalExpr(bin.Right), rv =>
                {
                    field.Assign(rv);
                    return ToDone(rv);
                });
            });
        return Then(EvalExpr(bin.Left), left => Then(EvalExpr(bin.Right), right =>
        {
            // 自定义运算符分发（对象上的 operatorXxx 方法）
            if (bin.Op == "=") return ToDone(right);
            if (bin.Op is "+=" or "-=" or "*=" or "/=" or "%=")
            {
                var op = bin.Op[..1];
                var fn = left.Type.TryLookupMethod(op);
                if (fn == null) return ThrowRavel($"类型 {left.Type} 不支持运算符 '{op}'");
                var r = fn(left, [right]);
                if (bin.Left is IdentifierExpr id) CurrentScope.Assign(id.Name, r);
                else return ThrowRavel("复合赋值目标必须是变量");
                return ToDone(r);
            }

            var builtin = left.Type.TryLookupMethod(bin.Op);
            if (builtin != null) return ToDone(builtin(left, [right]));

            return ThrowRavel($"未知的二元运算符: {bin.Op}");
        }));
    }

    // ======================== 一元运算符 ========================

    /// <summary>求值一元表达式：! 逻辑非 / - 负号</summary>
    private Step EvalUnary(UnaryExpr un) => Then(EvalExpr(un.Operand), o =>
    {
        return un.Op switch
        {
            "!" => o is BoolVal bn
                ? ToDone(new BoolVal(!bn.Value))
                : ThrowRavel("! 需要 bool 操作数"),
            "-" => o is IntVal i ? ToDone(new IntVal(-i.Value)) : ThrowRavel("一元 '-' 需要 int"),
            _ => ThrowRavel("未知的一元运算符: " + un.Op)
        };
    });

    // ======================== 调用 ========================

    /// <summary>求值函数调用：续延 / 类型构造 / 普通函数</summary>
    private Step EvalCall(CallExpr call) => Then(EvalExpr(call.Function), fv =>
    {
        if (fv is ContinuationVal k)
        {
            if (call.Arguments.Count != 1) return ThrowRavel("续延需要 1 个参数");
            return Then(EvalExpr(call.Arguments[0]), av => k.Impl(av));
        }

        if (fv is not FunctionVal fn) return ThrowRavel("无法调用: " + fv.Type);
        return EvalArgs(call.Arguments, args =>
        {
            try
            {
                return fn.Trampolined(args);
            }
            catch (RuntimeException ex)
            {
                return ThrowRavel(ex.Message);
            }
        });
    });


    /// <summary>求值参数列表，结果放入数组后调用回调 k</summary>
    private Step EvalArgs(List<Expression> es, Func<RuntimeValue[], Step> k)
    {
        var r = new RuntimeValue[es.Count];
        return EvalArgsRec(es, 0, r, k);
    }

    /// <summary>参数递归求值：逐参数求值，块参数惰性包装为 BlockVal</summary>
    private Step EvalArgsRec(List<Expression> es, int i, RuntimeValue[] r, Func<RuntimeValue[], Step> k)
    {
        if (i >= es.Count) return k(r);
        if (es[i] is BlockExpr b)
        {
            r[i] = new BlockVal(b, CurrentScope);
            return EvalArgsRec(es, i + 1, r, k);
        }

        return Then(EvalExpr(es[i]), v =>
        {
            r[i] = v;
            return EvalArgsRec(es, i + 1, r, k);
        });
    }

    /// <summary>求值成员访问 obj.member：通过 BoxedValue 统一查找</summary>
    private Step EvalMemberAccess(MemberAccess ma) => Then(EvalExpr(ma.Object), obj =>
    {
        var fv = new BoxedValue(obj).GetMember(ma.Member).Value;


        return ToDone(fv);
    });

    /// <summary>求值管道表达式 fn &lt;| arg —— 将 arg 作为参数调用 fn</summary>
    private Step EvalPipe(PipeExpr p) => Then(EvalExpr(p.Right), right => Then(EvalExpr(p.Left), left =>
    {
        if (left is not FunctionVal fn) return ThrowRavel("<| 左边必须是函数");
        return fn.Trampolined([right]);
    }));
}
