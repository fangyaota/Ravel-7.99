namespace Ravel.Runtime;
using System.IO;
using System.Linq;

public partial class Interpreter
{

    // ======================== 表达式 ========================

    Step EvalIdent(IdentifierExpr id){ var v=CurrentScope.Lookup(id.Name); if(v.HasAttr("unreadable")) throw new RuntimeException("Variable " + id.Name + " is unreadable"); if(v.HasAttr("outdated")) Console.Error.WriteLine("[outdated] " + id.Name); if(v.HasAttr("by")){ var prop=v.Value; var getter=new BoxedValue(prop).GetMember("get").Value; if(getter is RuntimeValue.FunctionVal gf) return ToDone(Step.Run(gf.Trampolined!(new RuntimeValue[] { RuntimeValue.VoidVal.Instance }))); } return ToDone(v.Value); }

    public Step EvalExpr(AstNode n)=>n switch{
        NumberLiteral nn=>ToDone(nn.IsFloat?new RuntimeValue.FloatVal(nn.Value):new RuntimeValue.IntVal((int)nn.Value)),
        StringLiteral ss=>ToDone(new RuntimeValue.StringVal(ss.Value)),
        IdentifierExpr id=>EvalIdent(id),
        VoidLiteral=>ToDone(RuntimeValue.VoidVal.Instance),
        BinaryExpr bin=>EvalBinary(bin),
        UnaryExpr un=>EvalUnary(un),
        CallExpr call=>EvalCall(call),
        MemberAccess ma=>EvalMemberAccess(ma),
        PipeExpr pipe=>EvalPipe(pipe),
        ListLiteral l=>EvalList(l.Elements),
        SetLiteral sl=>EvalSet(sl.Elements),
        DictLiteral dl=>EvalDict(dl.Entries),
        BlockExpr b=>EvalBlock(b),
        LambdaExpr lam=>ToDone(EvalLambda(lam)),
        _=>throw new RuntimeException("Cannot evaluate")
    };

    // ======================== 二元/一元 ========================
    Step EvalList(List<Expression> es)=>EvalListRec(es,0,new List<RuntimeValue>());
    Step EvalListRec(List<Expression> es,int i,List<RuntimeValue> acc){
        if(i>=es.Count) return ToDone(new RuntimeValue.ListVal(acc));
        return Then(EvalExpr(es[i]),v=>{ acc.Add(v); return EvalListRec(es,i+1,acc); });
    }

    Step EvalSet(List<Expression> es)=>EvalSetRec(es,0,new HashSet<RuntimeValue>());
    Step EvalSetRec(List<Expression> es,int i,HashSet<RuntimeValue> acc){
        if(i>=es.Count) return ToDone(new RuntimeValue.SetVal(acc));
        return Then(EvalExpr(es[i]),v=>{ acc.Add(v); return EvalSetRec(es,i+1,acc); });
    }

    Step EvalDict(List<DictEntry> entries)=>EvalDictRec(entries,0,new Dictionary<string,RuntimeValue>());
    Step EvalDictRec(List<DictEntry> entries,int i,Dictionary<string,RuntimeValue> acc){
        if(i>=entries.Count) return ToDone(new RuntimeValue.DictVal(acc));
        return Then(EvalExpr(entries[i].Value),v=>{ acc[entries[i].Key]=v; return EvalDictRec(entries,i+1,acc); });
    }

    static RuntimeValue? TryConvert(RuntimeValue val,RuntimeType target){
        if(target==RuntimeType.Int||target==RuntimeType.Float||target==RuntimeType.String||target==RuntimeType.Bool||target==RuntimeType.BigInt){
            try{ return EvalTypeCastDirect(new RuntimeValue.TypeVal(target),val); }catch{ return null; }
        }
        return null;
    }
    static RuntimeValue EvalTypeCastDirect(RuntimeValue.TypeVal tv,RuntimeValue val){
        if(val is RuntimeValue.DefaultVal){
            if(tv.Value==RuntimeType.Int) return new RuntimeValue.IntVal(0);
            if(tv.Value==RuntimeType.Float) return new RuntimeValue.FloatVal(0);
            if(tv.Value==RuntimeType.Bool) return new RuntimeValue.BoolVal(false);
            if(tv.Value==RuntimeType.String) return new RuntimeValue.StringVal("");
            if(tv.Value==RuntimeType.List) return new RuntimeValue.ListVal(new List<RuntimeValue>());
            if(tv.Value==RuntimeType.Set) return new RuntimeValue.SetVal(new HashSet<RuntimeValue>());
            if(tv.Value==RuntimeType.Dict) return new RuntimeValue.DictVal(new Dictionary<string,RuntimeValue>());
            if(tv.Value==RuntimeType.BigInt) return new RuntimeValue.BigIntVal(0);
            if(tv.Value==RuntimeType.Fraction) return new RuntimeValue.FractionVal(0,1);
            if(tv.Value==RuntimeType.BigFraction) return new RuntimeValue.BigFractionVal(0,1);
            if(tv.Value==RuntimeType.Function) return RuntimeValue.FunctionVal.FromDirect(_=>RuntimeValue.VoidVal.Instance);
            return val;
        }
        if(tv.Value==RuntimeType.Int) return val is RuntimeValue.IntVal i?i:val is RuntimeValue.FloatVal f?new RuntimeValue.IntVal((int)f.Value):val is RuntimeValue.StringVal s?new RuntimeValue.IntVal(int.Parse(s.Value)):val is RuntimeValue.BoolVal b?new RuntimeValue.IntVal(b.Value?1:0):throw new RuntimeException("");
        if(tv.Value==RuntimeType.Float) return val is RuntimeValue.IntVal i2?new RuntimeValue.FloatVal(i2.Value):val is RuntimeValue.FloatVal f2?f2:throw new RuntimeException("");
        if(tv.Value==RuntimeType.String) return val is RuntimeValue.StringVal sv ? sv : new RuntimeValue.StringVal(Show(val));
        if(tv.Value==RuntimeType.Bool) return new RuntimeValue.BoolVal(IsTruthy(val));
        throw new RuntimeException("");
    }
    static void SyncScopeToFields(Scope scope,RuntimeValue.ObjectVal inst){
        var s=scope;
        while(s!=null){
            foreach(var kv in s.Variables)
                inst.Fields[kv.Key]=kv.Value.Value;
            s=s.Parent;
        }
    }
    static Scope? CopyScope(Scope? src){
        if(src==null)return null;
        var dst=new Scope(src.Parent);
        foreach(var kv in src.Variables)
            dst.Define(kv.Key,kv.Value.TypeConstraint,kv.Value.Value);
        return dst;
    }

    Step EvalBinary(BinaryExpr bin){
        if(bin.Op=="&&") return Then(EvalExpr(bin.Left),l=>IsTruthy(l)?EvalExpr(bin.Right):ToDone(l));
        if(bin.Op=="||") return Then(EvalExpr(bin.Left),l=>IsTruthy(l)?ToDone(l):EvalExpr(bin.Right));
        if(bin.Op=="="&&bin.Left is MemberAccess ma)
            return Then(EvalExpr(ma.Object),obj=>{
                if(obj is RuntimeValue.FunctionVal fn && ma.Member=="name"){
                    return Then(EvalExpr(bin.Right),rv=>{
                        fn.Name=((RuntimeValue.StringVal)rv).Value;
                        return ToDone(rv);
                    });
                }
                if(obj is RuntimeValue.ObjectVal ov){
                    if(ov.InstanceScope!=null){
                        try{
                            var vr=ov.InstanceScope.Lookup(ma.Member);
                            if(vr.HasAttr("by")){
                                return Then(EvalExpr(bin.Right),rv=>{
                                    var prop=vr.Value;
                                    var setter=new BoxedValue(prop).GetMember("set").Value;
                                    if(setter is RuntimeValue.FunctionVal sf)
                                        Step.Run(sf.Trampolined!(new RuntimeValue[]{rv}));
                                    return ToDone(rv);
                                });
                            }
                        }catch{}
                    }
                    return Then(EvalExpr(bin.Right),rv=>{ ov.Fields[ma.Member]=rv; if(ov.InstanceScope!=null)try{ov.InstanceScope.Lookup(ma.Member).Assign(rv);}catch{} return ToDone(rv); });
                }
                throw new RuntimeException("Cannot set field on non-object");
            });
        return Then(EvalExpr(bin.Left),left=>Then(EvalExpr(bin.Right),right=>{
            // Custom operator dispatch
            if(left is RuntimeValue.ObjectVal ov&&ov.InstanceScope!=null){
                var opName="operator"+bin.Op;
                if(ov.InstanceScope.Contains(opName)&&ov.InstanceScope.Lookup(opName).Value is RuntimeValue.FunctionVal ofn){
                    var savedThis=ov.Meta?.ThisVar?.Value;
                    ov.Meta?.ThisVar?.Assign(left);
                    var step=ofn.Trampolined!=null?ofn.Trampolined(new[]{right}):ToDone(ofn.Direct!(new[]{right}));
                    return Finally(step,()=>{ if(ov.Meta!=null&&savedThis!=null) ov.Meta.ThisVar!.Assign(savedThis!); });
                }
            }
            if(bin.Op=="=") return ToDone(right);
            if(bin.Op is "+=" or "-=" or "*=" or "/=" or "%="){
                var op=bin.Op[..1];
                var fn=RuntimeType.GetBuiltinOperator(left.Type,op)??throw new RuntimeException($"No operator '{op}' for {left.Type}");
                var r=fn(left,right);
                if(bin.Left is IdentifierExpr id) CurrentScope.Assign(id.Name,r);
                else throw new RuntimeException("Compound assignment target must be a variable");
                return ToDone(r);
            }
            var builtin=RuntimeType.GetBuiltinOperator(left.Type,bin.Op);
            if(builtin!=null) return ToDone(builtin(left,right));
            if(bin.Op=="|"){
                return Then(EvalExpr(bin.Left),lv=>Then(EvalExpr(bin.Right),rv=>{
                    // bool OR
                    if(lv is RuntimeValue.BoolVal lb && rv is RuntimeValue.BoolVal rb)
                        return ToDone(new RuntimeValue.BoolVal(lb.Value || rb.Value));
                    // int bitwise OR
                    if(lv is RuntimeValue.IntVal li && rv is RuntimeValue.IntVal ri)
                        return ToDone(new RuntimeValue.IntVal(li.Value | ri.Value));
                    // 函数交替
                    if(lv is RuntimeValue.FunctionVal lf && rv is RuntimeValue.FunctionVal rf)
                        return ToDone(RuntimeValue.FunctionVal.FromTrampolined(ia=>{
                            var step=lf.Trampolined!=null?lf.Trampolined(ia):ToDone(lf.Direct!(ia));
                            return OrElse(step,_=> rf.Trampolined!=null?rf.Trampolined(ia):ToDone(rf.Direct!(ia)));
                        }));
                    return ThrowRavel("Both sides of | must be bool, int, or function");
                }));
            }
            if(bin.Op=="&"){
                return Then(EvalExpr(bin.Left),lv=>Then(EvalExpr(bin.Right),rv=>{
                    if(lv is RuntimeValue.BoolVal lb && rv is RuntimeValue.BoolVal rb)
                        return ToDone(new RuntimeValue.BoolVal(lb.Value && rb.Value));
                    if(lv is RuntimeValue.IntVal li && rv is RuntimeValue.IntVal ri)
                        return ToDone(new RuntimeValue.IntVal(li.Value & ri.Value));
                    return ThrowRavel("Both sides of & must be bool or int");
                }));
            }
            if(bin.Op=="^"){
                return Then(EvalExpr(bin.Left),lv=>Then(EvalExpr(bin.Right),rv=>{
                    if(lv is RuntimeValue.BoolVal lb && rv is RuntimeValue.BoolVal rb)
                        return ToDone(new RuntimeValue.BoolVal(lb.Value ^ rb.Value));
                    if(lv is RuntimeValue.IntVal li && rv is RuntimeValue.IntVal ri)
                        return ToDone(new RuntimeValue.IntVal(li.Value ^ ri.Value));
                    return ThrowRavel("Both sides of ^ must be bool or int");
                }));
            }
            throw new RuntimeException($"Unknown binary: {bin.Op}");
        }));
    }
    Step EvalUnary(UnaryExpr un)=>Then(EvalExpr(un.Operand),o=>{
        CheckD(o,un.Op);
        return ToDone(un.Op switch{
            "!"=>new RuntimeValue.BoolVal(!IsTruthy(o)),
            "-"=>o is RuntimeValue.IntVal i?new RuntimeValue.IntVal(-i.Value):throw new RuntimeException("Unary '-' requires int"),
            _=>throw new RuntimeException("Unknown unary")
        });
    });

    // ======================== 调用 ========================
    Step EvalCall(CallExpr call)=>Then(EvalExpr(call.Function),fv=>{
        CheckD(fv,"call");
        if(fv is RuntimeValue.ContinuationVal k){
            if(call.Arguments.Count!=1) throw new RuntimeException("continuation expects 1 arg");
            return Then(EvalExpr(call.Arguments[0]),av=>k.Impl(av));
        }
        if(fv is RuntimeValue.TypeVal tv) return EvalTypeCast(tv,call.Arguments);
        var fn = fv as RuntimeValue.FunctionVal; if(fn==null) ThrowStatic("Cannot call: "+fv.Type);
        return EvalArgs(call.Arguments,args=>{
            try{ return fn.Trampolined!=null?fn.Trampolined(args):ToDone(fn.Direct!(args)); }
            catch(RuntimeException ex){ return ThrowRavel(ex.Message); }
        });
    });
    Step EvalTypeCast(RuntimeValue.TypeVal tv,List<Expression> args){
        if(tv.Value.Initializer!=null){
            return EvalArgs(args,evaluated=>{
                // 创建 fresh 实例，作为第一参数
                var proto=tv.Value.Proto;
                if(proto==null) throw new RuntimeException("Type has no proto");
                var fields=new Dictionary<string,RuntimeValue>();
                foreach(var kv in proto.Variables){
                    if(kv.Key=="this"||kv.Key=="base"||kv.Key=="block"||kv.Key=="thistype")continue;
                    fields[kv.Key]=kv.Value.Value;
                }
                var instScope=proto.Push();
                instScope.Define("this",tv.Value,new RuntimeValue.ObjectVal(tv.Value,fields,null,null,instScope));
                var fresh=new RuntimeValue.ObjectVal(tv.Value,fields,null,null,instScope);
                instScope.DefineOrReplace("this",tv.Value,fresh);
                // 逐参数 curry：fresh → arg1 → arg2 → ...
                var init=tv.Value.Initializer!;
                var step=init.Trampolined!=null?init.Trampolined(new[]{fresh}):ToDone(init.Direct!(new[]{fresh}));
                foreach(var arg in evaluated){
                    step=Then(step,result=>{
                        var next=(RuntimeValue.FunctionVal)result;
                        return next.Trampolined!=null?next.Trampolined(new[]{arg}):ToDone(next.Direct!(new[]{arg}));
                    });
                }
                return step;
            });
        }
        if(args.Count!=1) throw new RuntimeException("type cast expects 1 argument");
        return Then(EvalExpr(args[0]),val=>{
            if(val is RuntimeValue.DefaultVal) return ToDone(EvalTypeCastDirect(tv,val));
            if(tv.Value==RuntimeType.Int){
                if(val is RuntimeValue.IntVal i) return ToDone(i);
                if(val is RuntimeValue.StringVal s){ if(int.TryParse(s.Value,out var n)) return ToDone(new RuntimeValue.IntVal(n)); ThrowStatic("Cannot convert string to int"); }
                if(val is RuntimeValue.BoolVal b) return ToDone(new RuntimeValue.IntVal(b.Value?1:0));
                if(val is RuntimeValue.FloatVal f) return ToDone(new RuntimeValue.IntVal((int)f.Value));
                if(val is RuntimeValue.BigIntVal bi) return ToDone(new RuntimeValue.IntVal((int)bi.Value));
                if(val is RuntimeValue.FractionVal fr1) return ToDone(new RuntimeValue.IntVal(fr1.Num/fr1.Den));
                if(val is RuntimeValue.BigFractionVal bfr) return ToDone(new RuntimeValue.IntVal((int)(bfr.Num/bfr.Den)));
                ThrowStatic("Cannot convert {val.Type} to int");
            }
            if(tv.Value==RuntimeType.Float){
                if(val is RuntimeValue.IntVal i) return ToDone(new RuntimeValue.FloatVal(i.Value));
                if(val is RuntimeValue.FloatVal f) return ToDone(f);
                if(val is RuntimeValue.StringVal s){ if(double.TryParse(s.Value,out var n)) return ToDone(new RuntimeValue.FloatVal(n)); throw new RuntimeException("Cannot convert string to float"); }
                ThrowStatic("Cannot convert {val.Type} to float");
            }
            if(tv.Value==RuntimeType.BigInt){
                if(val is RuntimeValue.IntVal i) return ToDone(new RuntimeValue.BigIntVal(i.Value));
                if(val is RuntimeValue.BigIntVal bi) return ToDone(bi);
                if(val is RuntimeValue.StringVal s){ if(System.Numerics.BigInteger.TryParse(s.Value,out var n)) return ToDone(new RuntimeValue.BigIntVal(n)); throw new RuntimeException("Cannot convert string to bigint"); }
                if(val is RuntimeValue.FloatVal ff) return ToDone(new RuntimeValue.BigIntVal((System.Numerics.BigInteger)ff.Value));
                ThrowStatic("Cannot convert {val.Type} to bigint");
            }
            if(tv.Value==RuntimeType.Float){
                if(val is RuntimeValue.IntVal i) return ToDone(new RuntimeValue.FloatVal(i.Value));
                if(val is RuntimeValue.FloatVal f) return ToDone(f);
                if(val is RuntimeValue.StringVal s){ if(double.TryParse(s.Value,out var n)) return ToDone(new RuntimeValue.FloatVal(n)); throw new RuntimeException("Cannot convert string to float"); }
                ThrowStatic("Cannot convert {val.Type} to float");
            }
            if(tv.Value==RuntimeType.BigInt){
                if(val is RuntimeValue.IntVal i) return ToDone(new RuntimeValue.BigIntVal(i.Value));
                if(val is RuntimeValue.BigIntVal bi) return ToDone(bi);
                if(val is RuntimeValue.StringVal s){ if(System.Numerics.BigInteger.TryParse(s.Value,out var n)) return ToDone(new RuntimeValue.BigIntVal(n)); throw new RuntimeException("Cannot convert string to bigint"); }
                if(val is RuntimeValue.FloatVal ff) return ToDone(new RuntimeValue.BigIntVal((System.Numerics.BigInteger)ff.Value));
                ThrowStatic("Cannot convert {val.Type} to bigint");
            }
            if(tv.Value==RuntimeType.Fraction){
                if(val is RuntimeValue.IntVal i) return ToDone(RuntimeValue.FunctionVal.FromTrampolined(da=>{
                    if(da.Length!=1||da[0] is not RuntimeValue.IntVal d) throw new RuntimeException("fraction expects int denominator");
                    return ToDone(new RuntimeValue.FractionVal(i.Value,d.Value));
                }));
                if(val is RuntimeValue.FractionVal f) return ToDone(f);
                if(val is RuntimeValue.StringVal s){ var p=s.Value.Split('/'); if(p.Length==2){ if(int.TryParse(p[0],out var n)&&int.TryParse(p[1],out var d)&&d!=0) return ToDone(new RuntimeValue.FractionVal(n,d)); } throw new RuntimeException("Invalid fraction string"); }
                ThrowStatic("Cannot convert {val.Type} to fraction");
            }
            if(tv.Value==RuntimeType.BigFraction){
                System.Numerics.BigInteger getBi(RuntimeValue v)=>v is RuntimeValue.IntVal i2?i2.Value:((RuntimeValue.BigIntVal)v).Value;
                if(val is RuntimeValue.IntVal||val is RuntimeValue.BigIntVal) return ToDone(RuntimeValue.FunctionVal.FromTrampolined(da=>{
                    if(da.Length!=1||(da[0] is not RuntimeValue.IntVal&&da[0] is not RuntimeValue.BigIntVal)) throw new RuntimeException("bigfraction expects int denominator");
                    return ToDone(new RuntimeValue.BigFractionVal(getBi(val),getBi(da[0])));
                }));
                if(val is RuntimeValue.FractionVal fr) return ToDone(new RuntimeValue.BigFractionVal(fr.Num,fr.Den));
                if(val is RuntimeValue.BigFractionVal bf) return ToDone(bf);
                ThrowStatic("Cannot convert {val.Type} to bigfraction");
            }
            if(tv.Value==RuntimeType.String) return ToDone(new RuntimeValue.StringVal(Show(val)));
            if(tv.Value==RuntimeType.String) return ToDone(EvalTypeCastDirect(tv,val));
            if(tv.Value==RuntimeType.Bool) return ToDone(EvalTypeCastDirect(tv,val));
            if(tv.Value==RuntimeType.Exception){
                if(args.Count!=1) throw new RuntimeException("Exception constructor expects 1 arg");
                return Then(EvalExpr(args[0]),val=>ToDone(new RuntimeValue.ExceptionVal(Show(val))));
            }
            if(tv.Value==RuntimeType.Bool) return ToDone(EvalTypeCastDirect(tv,val));
            if(tv.Value==RuntimeType.String) return ToDone(EvalTypeCastDirect(tv,val));
            throw new RuntimeException($"Type {tv.Value.Name} is not callable as constructor");
        });
    }
    Step EvalArgs(List<Expression> es,Func<RuntimeValue[],Step> k){
        var r=new RuntimeValue[es.Count]; return EvalArgsRec(es,0,r,k);
    }
    Step EvalArgsRec(List<Expression> es,int i,RuntimeValue[] r,Func<RuntimeValue[],Step> k){
        if(i>=es.Count) return k(r);
        if(es[i] is BlockExpr b){ r[i]=new RuntimeValue.BlockVal(b,CurrentScope,this); return EvalArgsRec(es,i+1,r,k); }
        return Then(EvalExpr(es[i]),v=>{ CheckD(v,"arg"); r[i]=v; return EvalArgsRec(es,i+1,r,k); });
    }

    static RuntimeValue ResolveThis(RuntimeValue v)=>v is RuntimeValue.ThisRef t?t.Value:v;
    Step EvalMemberAccess(MemberAccess ma)=>Then(EvalExpr(ma.Object),obj=>{
        CheckD(obj,"member access"); var fv=new BoxedValue(ResolveThis(obj)).GetMember(ma.Member).Value;
        if(fv is RuntimeValue.FunctionVal fnO&&obj is RuntimeValue.ObjectVal ov2&&ov2.InstanceScope!=null){
            return ToDone(fv);
        }
        return ToDone(fv);
    });
        static string ExtractOp(string name){ return name.StartsWith("operator")?name[8..]:name; }

    Step EvalPipe(PipeExpr p)=>Then(EvalExpr(p.Right),right=>Then(EvalExpr(p.Left),left=>{
        if(left is not RuntimeValue.FunctionVal fn) throw new RuntimeException("Left of <| must be function");
        return fn.Trampolined!=null?fn.Trampolined(new[]{right}):ToDone(fn.Direct!(new[]{right}));
    }));

}
