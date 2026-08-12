namespace Ravel.Runtime;

public partial class RuntimeType
{
    // ============================================================
    //  类型构造器 / 转换器
    // ============================================================

    /// <summary>注册各内建类型的 Initializer（调用该类型时执行转换）</summary>
    private static void RegisterInitializers()
    {
        Int.Initializer = MakeCaster(Int, CastToInt);
        Float.Initializer = MakeCaster(Float, CastToFloat);
        Bool.Initializer = MakeCaster(Bool, CastToBool);
        String.Initializer = MakeCaster(String, CastToString);
        BigInt.Initializer = MakeCaster(BigInt, CastToBigInt);
        Fraction.Initializer = MakeCaster(Fraction, CastToFraction);
        BigFraction.Initializer = MakeCaster(BigFraction, CastToBigFraction);
        Exception.Initializer = MakeCaster(Exception, CastToException);
        List.Initializer = MakeDefaultCaster(List);
        Set.Initializer = MakeDefaultCaster(Set);
        Dict.Initializer = MakeDefaultCaster(Dict);
    }

    /// <summary>包装转换函数为单参构造器（含参数个数检查）</summary>
    private static FunctionVal MakeCaster(RuntimeType type, Func<RuntimeValue, Step> cast)
        => FunctionVal.FromTrampolined(args =>
        {
            if (args.Length != 1)
                throw new RuntimeException($"{type.Name} 需要 1 个参数");
            return cast(args[0]);
        });

    /// <summary>仅支持 default → 默认值的构造器（集合等不可直接构造的类型）</summary>
    private static FunctionVal MakeDefaultCaster(RuntimeType type)
        => MakeCaster(type, val => val is DefaultVal
            ? new Done(ConvertDirect(type, val))
            : throw new RuntimeException($"类型 {type.Name} 不能作为构造器调用"));

    private static Step CastToInt(RuntimeValue val)
    {
        if (val is DefaultVal) return new Done(new IntVal(0));
        if (val is IntVal i) return new Done(i);
        if (val is StringVal s)
        {
            if (int.TryParse(s.Value, out var n)) return new Done(new IntVal(n));
            throw new RuntimeException("无法将字符串转换为 int");
        }
        if (val is BoolVal b) return new Done(new IntVal(b.Value ? 1 : 0));
        if (val is FloatVal f) return new Done(new IntVal((int)f.Value));
        if (val is BigIntVal bi) return new Done(new IntVal((int)bi.Value));
        if (val is FractionVal fr) return new Done(new IntVal(fr.Num / fr.Den));
        if (val is BigFractionVal bfr) return new Done(new IntVal((int)(bfr.Num / bfr.Den)));
        throw new RuntimeException($"无法将 {val.Type} 转换为 int");
    }

    private static Step CastToFloat(RuntimeValue val)
    {
        if (val is DefaultVal) return new Done(new FloatVal(0));
        if (val is IntVal i) return new Done(new FloatVal(i.Value));
        if (val is FloatVal f) return new Done(f);
        if (val is StringVal s)
        {
            if (double.TryParse(s.Value, out var n)) return new Done(new FloatVal(n));
            throw new RuntimeException("无法将字符串转换为 float");
        }
        throw new RuntimeException($"无法将 {val.Type} 转换为 float");
    }

    private static Step CastToBool(RuntimeValue val)
    {
        if (val is DefaultVal) return new Done(new BoolVal(false));
        return new Done(ConvertDirect(Bool, val));
    }

    private static Step CastToString(RuntimeValue val)
    {
        if (val is DefaultVal) return new Done(new StringVal(""));
        return new Done(ConvertDirect(String, val));
    }

    private static Step CastToBigInt(RuntimeValue val)
    {
        if (val is DefaultVal) return new Done(new BigIntVal(0));
        if (val is IntVal i) return new Done(new BigIntVal(i.Value));
        if (val is BigIntVal bi) return new Done(bi);
        if (val is StringVal s)
        {
            if (System.Numerics.BigInteger.TryParse(s.Value, out var n)) return new Done(new BigIntVal(n));
            throw new RuntimeException("无法将字符串转换为 bigint");
        }
        if (val is FloatVal f) return new Done(new BigIntVal((System.Numerics.BigInteger)f.Value));
        throw new RuntimeException($"无法将 {val.Type} 转换为 bigint");
    }

    private static Step CastToFraction(RuntimeValue val)
    {
        if (val is DefaultVal) return new Done(new FractionVal(0, 1));
        if (val is IntVal i)
            return new Done(FunctionVal.FromTrampolined(da =>
            {
                if (da.Length != 1 || da[0] is not IntVal d) throw new RuntimeException("分数需要 int 分母");
                return new Done(new FractionVal(i.Value, d.Value));
            }));
        if (val is FractionVal f) return new Done(f);
        if (val is StringVal s)
        {
            var p = s.Value.Split('/');
            if (p.Length == 2 && int.TryParse(p[0], out var n) && int.TryParse(p[1], out var d) && d != 0)
                return new Done(new FractionVal(n, d));
            throw new RuntimeException("无效的分数字符串");
        }
        throw new RuntimeException($"无法将 {val.Type} 转换为 fraction");
    }

    private static Step CastToBigFraction(RuntimeValue val)
    {
        if (val is DefaultVal) return new Done(new BigFractionVal(0, 1));
        static System.Numerics.BigInteger GetBi(RuntimeValue v) => v is IntVal i ? i.Value : ((BigIntVal)v).Value;
        if (val is IntVal || val is BigIntVal)
            return new Done(FunctionVal.FromTrampolined(da =>
            {
                if (da.Length != 1 || (da[0] is not IntVal && da[0] is not BigIntVal))
                    throw new RuntimeException("大分数需要整数分母");
                return new Done(new BigFractionVal(GetBi(val), GetBi(da[0])));
            }));
        if (val is FractionVal fr) return new Done(new BigFractionVal(fr.Num, fr.Den));
        if (val is BigFractionVal bf) return new Done(bf);
        throw new RuntimeException($"无法将 {val.Type} 转换为 bigfraction");
    }

    private static Step CastToException(RuntimeValue val)
        => new Done(new ExceptionVal(val.ToString()));

    /// <summary>同步类型转换（隐式转换用）：失败抛异常，仅支持安全转换</summary>
    internal static RuntimeValue ConvertDirect(RuntimeType target, RuntimeValue val)
    {
        if (val is DefaultVal)
        {
            if (target == Int) return new IntVal(0);
            if (target == Float) return new FloatVal(0);
            if (target == Bool) return new BoolVal(false);
            if (target == String) return new StringVal("");
            if (target == List) return new ListVal([]);
            if (target == Set) return new SetVal([]);
            if (target == Dict) return new DictVal([]);
            if (target == BigInt) return new BigIntVal(0);
            if (target == Fraction) return new FractionVal(0, 1);
            if (target == BigFraction) return new BigFractionVal(0, 1);
            if (target == Function) return FunctionVal.FromDirect(_ => VoidVal.Instance);
            return val;
        }
        if (target == Int)
            return val is IntVal i ? i :
                val is FloatVal f ? new IntVal((int)f.Value) :
                val is StringVal s ? new IntVal(int.Parse(s.Value)) :
                val is BoolVal b ? new IntVal(b.Value ? 1 : 0) :
                throw new RuntimeException("无法转换为 int");
        if (target == Float)
            return val is IntVal i2 ? new FloatVal(i2.Value) :
                val is FloatVal f2 ? f2 : throw new RuntimeException("无法转换为 float");
        if (target == String)
            return val is StringVal sv ? sv : new StringVal(val.ToString());
        if (target == Bool)
            return val is BoolVal b3 ? b3 : throw new RuntimeException("无法转换为 bool");
        throw new RuntimeException("无法转换类型");
    }
}
