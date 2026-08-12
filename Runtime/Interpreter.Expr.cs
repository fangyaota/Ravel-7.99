namespace Ravel.Runtime;

using System.IO;
using System.Linq;

public partial class Interpreter
{

    // ======================== 表达式 ========================

    /// <summary>求值标识符：查作用域获取变量值，处理 by 属性 / unreadable / outdated</summary>
    Step EvalIdent(IdentifierExpr id)
    {
        var v = CurrentScope.Lookup(id.Name);
        if (v.HasAttr("unreadable")) return ThrowRavel("变量 '" + id.Name + "' 不可读取");
        if (v.HasAttr("outdated")) Console.Error.WriteLine("[outdated] " + id.Name);
        if (v.HasAttr("by"))
        {
            var prop = v.Value;
            var getter = new BoxedValue(prop).GetMember("get").Value;
            if (getter is RuntimeValue.FunctionVal gf)
                return ToDone(Step.Run(gf.Trampolined!(new RuntimeValue[] { RuntimeValue.VoidVal.Instance })));
        }
        return ToDone(v.Value);
    }

    /// <summary>表达式求值入口：按 AST 节点类型分发</summary>
    public Step EvalExpr(AstNode n) => n switch
    {
        NumberLiteral nn => ToDone(nn.IsFloat ? new RuntimeValue.FloatVal(nn.Value) : new RuntimeValue.IntVal((int)nn.Value)),
        StringLiteral ss => ToDone(new RuntimeValue.StringVal(ss.Value)),
        IdentifierExpr id => EvalIdent(id),
        VoidLiteral => ToDone(RuntimeValue.VoidVal.Instance),
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
    Step EvalList(List<Expression> es) => EvalListRec(es, 0, []);
    /// <summary>列表递归求值：逐元素求值后收集</summary>
    Step EvalListRec(List<Expression> es, int i, List<RuntimeValue> acc)
    {
        if (i >= es.Count) return ToDone(new RuntimeValue.ListVal(acc));
        return Then(EvalExpr(es[i]), v => { acc.Add(v); return EvalListRec(es, i + 1, acc); });
    }

    /// <summary>求值集合字面量 {a b c} → SetVal</summary>
    Step EvalSet(List<Expression> es) => EvalSetRec(es, 0, []);
    /// <summary>集合递归求值：逐元素求值后去重收集</summary>
    Step EvalSetRec(List<Expression> es, int i, HashSet<RuntimeValue> acc)
    {
        if (i >= es.Count) return ToDone(new RuntimeValue.SetVal(acc));
        return Then(EvalExpr(es[i]), v => { acc.Add(v); return EvalSetRec(es, i + 1, acc); });
    }

    /// <summary>求值字典字面量 {k: v k2: v2} → DictVal</summary>
    Step EvalDict(List<DictEntry> entries) => EvalDictRec(entries, 0, []);
    /// <summary>字典递归求值：逐条目求值后收集</summary>
    Step EvalDictRec(List<DictEntry> entries, int i, Dictionary<string, RuntimeValue> acc)
    {
        if (i >= entries.Count) return ToDone(new RuntimeValue.DictVal(acc));
        return Then(EvalExpr(entries[i].Value), v => { acc[entries[i].Key] = v; return EvalDictRec(entries, i + 1, acc); });
    }

    /// <summary>隐式类型转换：尝试将值转换为目标类型，失败返回 null</summary>
    static RuntimeValue? TryConvert(RuntimeValue val, RuntimeType target)
    {
        if (target == RuntimeType.Int || target == RuntimeType.Float || target == RuntimeType.String || target == RuntimeType.Bool || target == RuntimeType.BigInt)
        {
            try { return EvalTypeCastDirect(new RuntimeValue.TypeVal(target), val); } catch { return null; }
        }
        return null;
    }
    /// <summary>直接类型转换（同步）：TypeVal → 对应 RuntimeValue，含 DefaultVal 默认值处理</summary>
    static RuntimeValue EvalTypeCastDirect(RuntimeValue.TypeVal tv, RuntimeValue val)
    {
        if (val is RuntimeValue.DefaultVal)
        {
            if (tv.Value == RuntimeType.Int) return new RuntimeValue.IntVal(0);
            if (tv.Value == RuntimeType.Float) return new RuntimeValue.FloatVal(0);
            if (tv.Value == RuntimeType.Bool) return new RuntimeValue.BoolVal(false);
            if (tv.Value == RuntimeType.String) return new RuntimeValue.StringVal("");
            if (tv.Value == RuntimeType.List) return new RuntimeValue.ListVal([]);
            if (tv.Value == RuntimeType.Set) return new RuntimeValue.SetVal([]);
            if (tv.Value == RuntimeType.Dict) return new RuntimeValue.DictVal([]);
            if (tv.Value == RuntimeType.BigInt) return new RuntimeValue.BigIntVal(0);
            if (tv.Value == RuntimeType.Fraction) return new RuntimeValue.FractionVal(0, 1);
            if (tv.Value == RuntimeType.BigFraction) return new RuntimeValue.BigFractionVal(0, 1);
            if (tv.Value == RuntimeType.Function) return RuntimeValue.FunctionVal.FromDirect(_ => RuntimeValue.VoidVal.Instance);
            return val;
        }
        if (tv.Value == RuntimeType.Int) return val is RuntimeValue.IntVal i ? i : val is RuntimeValue.FloatVal f ? new RuntimeValue.IntVal((int)f.Value) : val is RuntimeValue.StringVal s ? new RuntimeValue.IntVal(int.Parse(s.Value)) : val is RuntimeValue.BoolVal b ? new RuntimeValue.IntVal(b.Value ? 1 : 0) : throw new RuntimeException("无法转换为 int");
        if (tv.Value == RuntimeType.Float) return val is RuntimeValue.IntVal i2 ? new RuntimeValue.FloatVal(i2.Value) : val is RuntimeValue.FloatVal f2 ? f2 : throw new RuntimeException("无法转换为 float");
        if (tv.Value == RuntimeType.String) return val is RuntimeValue.StringVal sv ? sv : new RuntimeValue.StringVal(Show(val));
        if (tv.Value == RuntimeType.Bool) return new RuntimeValue.BoolVal(IsTruthy(val));
        throw new RuntimeException("无法转换类型");
    }
    /// <summary>将作用域变量同步回对象字段（with 语句结束时调用）</summary>
    static void SyncScopeToFields(Scope scope, RuntimeValue.ObjectVal inst)
    {
        var s = scope;
        while (s != null)
        {
            foreach (var kv in s.Variables)
                inst.Fields[kv.Key] = kv.Value.Value;
            s = s.Parent;
        }
    }
    /// <summary>浅拷贝作用域（with 语句用）</summary>
    static Scope? CopyScope(Scope? src)
    {
        if (src == null) return null;
        var dst = new Scope(src.Parent);
        foreach (var kv in src.Variables)
            dst.Define(kv.Key, kv.Value.TypeConstraint, kv.Value.Value);
        return dst;
    }

    // ======================== 二元运算符 ========================

    /// <summary>求值二元表达式：短路逻辑 / 赋值 / 复合赋值 / 自定义运算符 / 内置运算符 / 位/逻辑</summary>
    Step EvalBinary(BinaryExpr bin)
    {
        if (bin.Op == "&&") return Then(EvalExpr(bin.Left), l => IsTruthy(l) ? EvalExpr(bin.Right) : ToDone(l));
        if (bin.Op == "||") return Then(EvalExpr(bin.Left), l => IsTruthy(l) ? ToDone(l) : EvalExpr(bin.Right));
        if (bin.Op == "=" && bin.Left is MemberAccess ma)
            return Then(EvalExpr(ma.Object), obj =>
            {
                if (obj is RuntimeValue.FunctionVal fn && ma.Member == "name")
                {
                    return Then(EvalExpr(bin.Right), rv =>
                    {
                        fn.Name = ((RuntimeValue.StringVal)rv).Value;
                        return ToDone(rv);
                    });
                }
                if (obj is RuntimeValue.ObjectVal ov)
                {
                    if (ov.InstanceScope != null)
                    {
                        try
                        {
                            var vr = ov.InstanceScope.Lookup(ma.Member);
                            if (vr.HasAttr("by"))
                            {
                                return Then(EvalExpr(bin.Right), rv =>
                                {
                                    var prop = vr.Value;
                                    var setter = new BoxedValue(prop).GetMember("set").Value;
                                    if (setter is RuntimeValue.FunctionVal sf)
                                        Step.Run(sf.Trampolined!(new RuntimeValue[] { rv }));
                                    return ToDone(rv);
                                });
                            }
                        }
                        catch { }
                    }
                    return Then(EvalExpr(bin.Right), rv => { ov.Fields[ma.Member] = rv; if (ov.InstanceScope != null) try { ov.InstanceScope.Lookup(ma.Member).Assign(rv); } catch { } return ToDone(rv); });
                }
                return ThrowRavel("无法给非对象设置字段");
            });
        return Then(EvalExpr(bin.Left), left => Then(EvalExpr(bin.Right), right =>
        {
            // 自定义运算符分发（对象上的 operatorXxx 方法）
            if (left is RuntimeValue.ObjectVal ov && ov.InstanceScope != null)
            {
                var opName = "operator" + bin.Op;
                if (ov.InstanceScope.Contains(opName) && ov.InstanceScope.Lookup(opName).Value is RuntimeValue.FunctionVal ofn)
                {
                    var savedThis = ov.Meta?.ThisVar?.Value;
                    ov.Meta?.ThisVar?.Assign(left);
                    var step = ofn.Trampolined != null ? ofn.Trampolined(new[] { right }) : ToDone(ofn.Direct!(new[] { right }));
                    return Finally(step, () => { if (ov.Meta != null && savedThis != null) ov.Meta.ThisVar!.Assign(savedThis!); });
                }
            }
            if (bin.Op == "=") return ToDone(right);
            if (bin.Op is "+=" or "-=" or "*=" or "/=" or "%=")
            {
                var op = bin.Op[..1];
                var fn = RuntimeType.GetBuiltinOperator(left.Type, op);
                if (fn == null) return ThrowRavel($"类型 {left.Type} 不支持运算符 '{op}'");
                var r = fn(left, right);
                if (bin.Left is IdentifierExpr id) CurrentScope.Assign(id.Name, r);
                else return ThrowRavel("复合赋值目标必须是变量");
                return ToDone(r);
            }
            var builtin = RuntimeType.GetBuiltinOperator(left.Type, bin.Op);
            if (builtin != null) return ToDone(builtin(left, right));
            // | 运算符：bool 逻辑或 / int 按位或 / 函数交替
            if (bin.Op == "|")
            {
                return Then(EvalExpr(bin.Left), lv => Then(EvalExpr(bin.Right), rv =>
                {
                    if (lv is RuntimeValue.BoolVal lb && rv is RuntimeValue.BoolVal rb)
                        return ToDone(new RuntimeValue.BoolVal(lb.Value || rb.Value));
                    if (lv is RuntimeValue.IntVal li && rv is RuntimeValue.IntVal ri)
                        return ToDone(new RuntimeValue.IntVal(li.Value | ri.Value));
                    if (lv is RuntimeValue.FunctionVal lf && rv is RuntimeValue.FunctionVal rf)
                        return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ia =>
                        {
                            var step = lf.Trampolined != null ? lf.Trampolined(ia) : ToDone(lf.Direct!(ia));
                            return OrElse(step, _ => rf.Trampolined != null ? rf.Trampolined(ia) : ToDone(rf.Direct!(ia)));
                        }));
                    return ThrowRavel("| 两边必须是 bool、int 或函数");
                }));
            }
            // & 运算符：bool 逻辑与 / int 按位与
            if (bin.Op == "&")
            {
                return Then(EvalExpr(bin.Left), lv => Then(EvalExpr(bin.Right), rv =>
                {
                    if (lv is RuntimeValue.BoolVal lb && rv is RuntimeValue.BoolVal rb)
                        return ToDone(new RuntimeValue.BoolVal(lb.Value && rb.Value));
                    if (lv is RuntimeValue.IntVal li && rv is RuntimeValue.IntVal ri)
                        return ToDone(new RuntimeValue.IntVal(li.Value & ri.Value));
                    return ThrowRavel("& 两边必须是 bool 或 int");
                }));
            }
            // ^ 运算符：bool 逻辑异或 / int 按位异或
            if (bin.Op == "^")
            {
                return Then(EvalExpr(bin.Left), lv => Then(EvalExpr(bin.Right), rv =>
                {
                    if (lv is RuntimeValue.BoolVal lb && rv is RuntimeValue.BoolVal rb)
                        return ToDone(new RuntimeValue.BoolVal(lb.Value ^ rb.Value));
                    if (lv is RuntimeValue.IntVal li && rv is RuntimeValue.IntVal ri)
                        return ToDone(new RuntimeValue.IntVal(li.Value ^ ri.Value));
                    return ThrowRavel("^ 两边必须是 bool 或 int");
                }));
            }
            return ThrowRavel($"未知的二元运算符: {bin.Op}");
        }));
    }

    // ======================== 一元运算符 ========================

    /// <summary>求值一元表达式：! 逻辑非 / - 负号</summary>
    Step EvalUnary(UnaryExpr un) => Then(EvalExpr(un.Operand), o =>
    {
        return un.Op switch
        {
            "!" => ToDone(new RuntimeValue.BoolVal(!IsTruthy(o))),
            "-" => o is RuntimeValue.IntVal i ? ToDone(new RuntimeValue.IntVal(-i.Value)) : ThrowRavel("一元 '-' 需要 int"),
            _ => ThrowRavel("未知的一元运算符: " + un.Op)
        };
    });

    // ======================== 调用 ========================

    /// <summary>求值函数调用：续延 / 类型构造 / 普通函数</summary>
    Step EvalCall(CallExpr call) => Then(EvalExpr(call.Function), fv =>
    {
        CheckD(fv, "call");
        if (fv is RuntimeValue.ContinuationVal k)
        {
            if (call.Arguments.Count != 1) return ThrowRavel("续延需要 1 个参数");
            return Then(EvalExpr(call.Arguments[0]), av => k.Impl(av));
        }
        if (fv is RuntimeValue.TypeVal tv) return EvalTypeCast(tv, call.Arguments);
        var fn = fv as RuntimeValue.FunctionVal; if (fn == null) ThrowStatic("无法调用: " + fv.Type);
        return EvalArgs(call.Arguments, args =>
        {
            try { return fn.Trampolined != null ? fn.Trampolined(args) : ToDone(fn.Direct!(args)); }
            catch (RuntimeException ex) { return ThrowRavel(ex.Message); }
        });
    });
    /// <summary>类型构造 / 类型转换：int(x) / string(x) / Exception(msg) 等</summary>
    Step EvalTypeCast(RuntimeValue.TypeVal tv, List<Expression> args)
    {
        if (tv.Value.Initializer != null)
        {
            return EvalArgs(args, evaluated =>
            {
                // 创建 fresh 实例，作为第一参数
                var proto = tv.Value.Proto;
                if (proto == null) return ThrowRavel("类型没有原型");
                var fields = new Dictionary<string, RuntimeValue>();
                foreach (var kv in proto.Variables)
                {
                    if (kv.Key == "this" || kv.Key == "base" || kv.Key == "block" || kv.Key == "thistype") continue;
                    fields[kv.Key] = kv.Value.Value;
                }
                var instScope = proto.Push();
                instScope.Define("this", tv.Value, new RuntimeValue.ObjectVal(tv.Value, fields, null, null, instScope));
                var fresh = new RuntimeValue.ObjectVal(tv.Value, fields, null, null, instScope);
                instScope.DefineOrReplace("this", tv.Value, fresh);
                // 逐参数 curry：fresh → arg1 → arg2 → ...
                var init = tv.Value.Initializer!;
                var step = init.Trampolined != null ? init.Trampolined(new[] { fresh }) : ToDone(init.Direct!(new[] { fresh }));
                foreach (var arg in evaluated)
                {
                    step = Then(step, result =>
                    {
                        var next = (RuntimeValue.FunctionVal)result;
                        return next.Trampolined != null ? next.Trampolined(new[] { arg }) : ToDone(next.Direct!(new[] { arg }));
                    });
                }
                return step;
            });
        }
        if (args.Count != 1) return ThrowRavel("类型转换需要 1 个参数");
        return Then(EvalExpr(args[0]), val =>
        {
            if (val is RuntimeValue.DefaultVal) return ToDone(EvalTypeCastDirect(tv, val));
            if (tv.Value == RuntimeType.Int)
            {
                if (val is RuntimeValue.IntVal i) return ToDone(i);
                if (val is RuntimeValue.StringVal s) { if (int.TryParse(s.Value, out var n)) return ToDone(new RuntimeValue.IntVal(n)); ThrowStatic("无法将字符串转换为 int"); }
                if (val is RuntimeValue.BoolVal b) return ToDone(new RuntimeValue.IntVal(b.Value ? 1 : 0));
                if (val is RuntimeValue.FloatVal f) return ToDone(new RuntimeValue.IntVal((int)f.Value));
                if (val is RuntimeValue.BigIntVal bi) return ToDone(new RuntimeValue.IntVal((int)bi.Value));
                if (val is RuntimeValue.FractionVal fr1) return ToDone(new RuntimeValue.IntVal(fr1.Num / fr1.Den));
                if (val is RuntimeValue.BigFractionVal bfr) return ToDone(new RuntimeValue.IntVal((int)(bfr.Num / bfr.Den)));
                ThrowStatic($"无法将 {val.Type} 转换为 int");
            }
            if (tv.Value == RuntimeType.Float)
            {
                if (val is RuntimeValue.IntVal i) return ToDone(new RuntimeValue.FloatVal(i.Value));
                if (val is RuntimeValue.FloatVal f) return ToDone(f);
                if (val is RuntimeValue.StringVal s) { if (double.TryParse(s.Value, out var n)) return ToDone(new RuntimeValue.FloatVal(n)); return ThrowRavel("无法将字符串转换为 float"); }
                ThrowStatic("无法将 {val.Type} 转换为 float");
            }
            if (tv.Value == RuntimeType.BigInt)
            {
                if (val is RuntimeValue.IntVal i) return ToDone(new RuntimeValue.BigIntVal(i.Value));
                if (val is RuntimeValue.BigIntVal bi) return ToDone(bi);
                if (val is RuntimeValue.StringVal s) { if (System.Numerics.BigInteger.TryParse(s.Value, out var n)) return ToDone(new RuntimeValue.BigIntVal(n)); return ThrowRavel("无法将字符串转换为 bigint"); }
                if (val is RuntimeValue.FloatVal ff) return ToDone(new RuntimeValue.BigIntVal((System.Numerics.BigInteger)ff.Value));
                ThrowStatic("无法将 {val.Type} 转换为 bigint");
            }
            if (tv.Value == RuntimeType.Float)
            {
                if (val is RuntimeValue.IntVal i) return ToDone(new RuntimeValue.FloatVal(i.Value));
                if (val is RuntimeValue.FloatVal f) return ToDone(f);
                if (val is RuntimeValue.StringVal s) { if (double.TryParse(s.Value, out var n)) return ToDone(new RuntimeValue.FloatVal(n)); return ThrowRavel("无法将字符串转换为 float"); }
                ThrowStatic("无法将 {val.Type} 转换为 float");
            }
            if (tv.Value == RuntimeType.BigInt)
            {
                if (val is RuntimeValue.IntVal i) return ToDone(new RuntimeValue.BigIntVal(i.Value));
                if (val is RuntimeValue.BigIntVal bi) return ToDone(bi);
                if (val is RuntimeValue.StringVal s) { if (System.Numerics.BigInteger.TryParse(s.Value, out var n)) return ToDone(new RuntimeValue.BigIntVal(n)); return ThrowRavel("无法将字符串转换为 bigint"); }
                if (val is RuntimeValue.FloatVal ff) return ToDone(new RuntimeValue.BigIntVal((System.Numerics.BigInteger)ff.Value));
                ThrowStatic("无法将 {val.Type} 转换为 bigint");
            }
            if (tv.Value == RuntimeType.Fraction)
            {
                if (val is RuntimeValue.IntVal i) return ToDone(RuntimeValue.FunctionVal.FromTrampolined(da =>
                {
                    if (da.Length != 1 || da[0] is not RuntimeValue.IntVal d) return ThrowRavel("分数需要 int 分母");
                    return ToDone(new RuntimeValue.FractionVal(i.Value, d.Value));
                }));
                if (val is RuntimeValue.FractionVal f) return ToDone(f);
                if (val is RuntimeValue.StringVal s) { var p = s.Value.Split('/'); if (p.Length == 2) { if (int.TryParse(p[0], out var n) && int.TryParse(p[1], out var d) && d != 0) return ToDone(new RuntimeValue.FractionVal(n, d)); } return ThrowRavel("无效的分数字符串"); }
                ThrowStatic($"无法将 {val.Type} 转换为 fraction");
            }
            if (tv.Value == RuntimeType.BigFraction)
            {
                System.Numerics.BigInteger getBi(RuntimeValue v) => v is RuntimeValue.IntVal i2 ? i2.Value : ((RuntimeValue.BigIntVal)v).Value;
                if (val is RuntimeValue.IntVal || val is RuntimeValue.BigIntVal) return ToDone(RuntimeValue.FunctionVal.FromTrampolined(da =>
                {
                    if (da.Length != 1 || (da[0] is not RuntimeValue.IntVal && da[0] is not RuntimeValue.BigIntVal)) return ThrowRavel("大分数需要整数分母");
                    return ToDone(new RuntimeValue.BigFractionVal(getBi(val), getBi(da[0])));
                }));
                if (val is RuntimeValue.FractionVal fr) return ToDone(new RuntimeValue.BigFractionVal(fr.Num, fr.Den));
                if (val is RuntimeValue.BigFractionVal bf) return ToDone(bf);
                ThrowStatic($"无法将 {val.Type} 转换为 bigfraction");
            }
            if (tv.Value == RuntimeType.String) return ToDone(new RuntimeValue.StringVal(Show(val)));
            if (tv.Value == RuntimeType.String) return ToDone(EvalTypeCastDirect(tv, val));
            if (tv.Value == RuntimeType.Bool) return ToDone(EvalTypeCastDirect(tv, val));
            if (tv.Value == RuntimeType.Exception)
            {
                if (args.Count != 1) return ThrowRavel("Exception 构造器需要 1 个参数");
                return Then(EvalExpr(args[0]), val => ToDone(new RuntimeValue.ExceptionVal(Show(val))));
            }
            if (tv.Value == RuntimeType.Bool) return ToDone(EvalTypeCastDirect(tv, val));
            if (tv.Value == RuntimeType.String) return ToDone(EvalTypeCastDirect(tv, val));
            return ThrowRavel($"类型 {tv.Value.Name} 不能作为构造器调用");
        });
    }
    /// <summary>求值参数列表，结果放入数组后调用回调 k</summary>
    Step EvalArgs(List<Expression> es, Func<RuntimeValue[], Step> k)
    {
        var r = new RuntimeValue[es.Count]; return EvalArgsRec(es, 0, r, k);
    }
    /// <summary>参数递归求值：逐参数求值，块参数惰性包装为 BlockVal</summary>
    Step EvalArgsRec(List<Expression> es, int i, RuntimeValue[] r, Func<RuntimeValue[], Step> k)
    {
        if (i >= es.Count) return k(r);
        if (es[i] is BlockExpr b) { r[i] = new RuntimeValue.BlockVal(b, CurrentScope, this); return EvalArgsRec(es, i + 1, r, k); }
        return Then(EvalExpr(es[i]), v => { CheckD(v, "arg"); r[i] = v; return EvalArgsRec(es, i + 1, r, k); });
    }

    /// <summary>解析 this 引用：ThisRef 解包为实际值</summary>
    static RuntimeValue ResolveThis(RuntimeValue v) => v is RuntimeValue.ThisRef t ? t.Value : v;
    /// <summary>求值成员访问 obj.member：通过 BoxedValue 统一查找</summary>
    Step EvalMemberAccess(MemberAccess ma) => Then(EvalExpr(ma.Object), obj =>
    {
        CheckD(obj, "member access"); var fv = new BoxedValue(ResolveThis(obj)).GetMember(ma.Member).Value;
        if (fv is RuntimeValue.FunctionVal fnO && obj is RuntimeValue.ObjectVal ov2 && ov2.InstanceScope != null)
        {
            return ToDone(fv);
        }
        return ToDone(fv);
    });
    /// <summary>从 operatorXxx 名字中提取运算符（如 operator+ → +）</summary>
    static string ExtractOp(string name) { return name.StartsWith("operator") ? name[8..] : name; }

    /// <summary>求值管道表达式 fn &lt;| arg —— 将 arg 作为参数调用 fn</summary>
    Step EvalPipe(PipeExpr p) => Then(EvalExpr(p.Right), right => Then(EvalExpr(p.Left), left =>
    {
        if (left is not RuntimeValue.FunctionVal fn) return ThrowRavel("<| 左边必须是函数");
        return fn.Trampolined != null ? fn.Trampolined(new[] { right }) : ToDone(fn.Direct!(new[] { right }));
    }));

}
