namespace Ravel.Runtime;

/// <summary>内置转换器（"调用这个类时算出什么值"）的注册。
/// 端口自旧的 RuntimeType.Initializers.cs。
///
/// **没有特设的"转换器"了**：内置类和用户类一样,类体里定义 `init`,
/// 实例化就是"跑类体 → 找 init → 调它 → 交出它的返回值"。`int 42` 得 42,
/// 是因为 `Integer` 的预设类体里写着 `init := <CastToInt>`。
/// 这里只剩下转换函数本身,以及 `ConvertDirect`(隐式转换走它)。</summary>
internal static partial class BuiltinClasses
{

    // ============================================================
    //  类型构造器 / 转换器
    // ============================================================

    /// <summary>给各内建类型装上预设类体（里面只定义 `init`，值是转换函数）</summary>
    private static void RegisterInitializers()
    {
        Int.ClassBody = PresetBody(("init", MakeCaster(CastToInt)));
        Float.ClassBody = PresetBody(("init", MakeCaster(CastToFloat)));
        Bool.ClassBody = PresetBody(("init", MakeCaster(CastToBool)));
        String.ClassBody = PresetBody(("init", MakeCaster(CastToString)));
        BigInt.ClassBody = PresetBody(("init", MakeCaster(CastToBigInt)));
        Fraction.ClassBody = PresetBody(("init", MakeCaster(CastToFraction)));
        BigFraction.ClassBody = PresetBody(("init", MakeCaster(CastToBigFraction)));
        Exception.ClassBody = PresetBody(("init", MakeCaster(CastToException)));
        List.ClassBody = PresetBody(("init", MakeDefaultCaster(List)));
        Set.ClassBody = PresetBody(("init", MakeDefaultCaster(Set)));
        Dict.ClassBody = PresetBody(("init", MakeDefaultCaster(Dict)));
        // 建类不在这里:`type` 的 init 由 InstallTypeInit 装 —— 它要用 NativeClosure
        // 看见正在构造的那个对象的 `this`,而且必须在 Object/Function/Type 都挂好之后
    }

    /// <summary>包装转换函数为单参构造器</summary>
    private static FunctionVal MakeCaster(Func<RuntimeValue, RuntimeValue> cast)
        => FunctionVal.From(cast);

    /// <summary>仅支持 default → 默认值的构造器（集合等不可直接构造的类型）</summary>
    private static FunctionVal MakeDefaultCaster(ObjectVal type)
        => MakeCaster(val => val is DefaultVal
            ? ConvertDirect(type, val)
            : throw new RuntimeException($"类型 {type.DisplayName} 不能作为构造器调用"));

    /// <summary>收进 int32，越界明确报错。以前是直接 `(int)` 硬转，两头都不对：
    /// BigInteger 那条越界会抛 C# 的 OverflowException 一直漏到顶层（`int 9999999999999` 就崩），
    /// double 那条在 .NET Core 里是**饱和**转换，`int 1e30` 静默变成 2147483647、
    /// `int 0/0` 静默变成 0——一个字都不说。
    /// int 是 32 位的类型，装不下就该报，别让调用者以为算出来了。</summary>
    private static IntVal FromBig(System.Numerics.BigInteger v)
        => v >= int.MinValue && v <= int.MaxValue
            ? new IntVal((int)v)
            : throw new RuntimeException($"数值 {v} 超出 int 范围（int 是 32 位，大数用 bigint）");

    private static IntVal FromDouble(double v)
    {
        if (double.IsNaN(v)) throw new RuntimeException("NaN 不能转换为 int");
        if (v < int.MinValue || v > int.MaxValue)
            throw new RuntimeException($"数值 {v} 超出 int 范围（int 是 32 位，大数用 bigint）");
        return new IntVal((int)v);
    }

    private static RuntimeValue CastToInt(RuntimeValue val)
    {
        if (val is DefaultVal) return new IntVal(0);
        if (val is IntVal i) return i;
        if (val is StringVal s)
        {
            if (int.TryParse(s.Value, out var n)) return new IntVal(n);
            throw new RuntimeException($"无法将字符串 '{s.Value}' 转换为 int（超出 int 范围或不是数字）");
        }

        if (val is BoolVal b) return new IntVal(b.Value ? 1 : 0);
        if (val is FloatVal f) return FromDouble(f.Value);
        if (val is BigIntVal bi) return FromBig(bi.Value);
        // 分数先在 BigInteger 里除，免得 int 除法自己先溢出（MinValue / -1）
        if (val is FractionVal fr) return FromBig((System.Numerics.BigInteger)fr.Num / fr.Den);
        if (val is BigFractionVal bfr) return FromBig(bfr.Num / bfr.Den);
        throw new RuntimeException($"无法将 {val.Type} 转换为 int");
    }

    private static RuntimeValue CastToFloat(RuntimeValue val)
    {
        if (val is DefaultVal) return new FloatVal(0);
        if (val is IntVal i) return new FloatVal(i.Value);
        if (val is FloatVal f) return f;
        // bigint → float 是拓宽,顺手接上(以前 `float (bigint 5)` 报「无法转换为 float」,
        // 而 `bigint 5 + 1.0` 却算得出来,两边对不上)
        if (val is BigIntVal bi) return new FloatVal((double)bi.Value);
        if (val is StringVal s)
        {
            if (double.TryParse(s.Value, out var n)) return new FloatVal(n);
            throw new RuntimeException($"无法将字符串 '{s.Value}' 转换为 float");
        }

        throw new RuntimeException($"无法将 {val.Type} 转换为 float");
    }

    private static RuntimeValue CastToBool(RuntimeValue val)
        => val is DefaultVal ? new BoolVal(false) : ConvertDirect(Bool, val);

    private static RuntimeValue CastToString(RuntimeValue val)
        => val is DefaultVal ? new StringVal("") : ConvertDirect(String, val);

    private static RuntimeValue CastToBigInt(RuntimeValue val)
    {
        if (val is DefaultVal) return new BigIntVal(0);
        if (val is IntVal i) return new BigIntVal(i.Value);
        if (val is BigIntVal bi) return bi;
        if (val is StringVal s)
        {
            if (System.Numerics.BigInteger.TryParse(s.Value, out var n)) return new BigIntVal(n);
            throw new RuntimeException("无法将字符串转换为 bigint");
        }

        if (val is FloatVal f) return new BigIntVal((System.Numerics.BigInteger)f.Value);
        throw new RuntimeException($"无法将 {val.Type} 转换为 bigint");
    }

    private static RuntimeValue CastToFraction(RuntimeValue val)
    {
        if (val is DefaultVal) return new FractionVal(0, 1);
        // int 当分子,再收一个 int 分母
        if (val is IntVal i)
            return FunctionVal.From(d =>
                d is not IntVal dd ? throw new RuntimeException("分数需要 int 分母")
                : dd.Value != 0 ? new FractionVal(i.Value, dd.Value)
                : throw new RuntimeException("分数的分母不能为零"));
        if (val is FractionVal f) return f;
        if (val is StringVal s)
        {
            var p = s.Value.Split('/');
            if (p.Length == 2 && int.TryParse(p[0], out var n) && int.TryParse(p[1], out var d) && d != 0)
                return new FractionVal(n, d);
            throw new RuntimeException("无效的分数字符串");
        }

        throw new RuntimeException($"无法将 {val.Type} 转换为 fraction");
    }

    private static RuntimeValue CastToBigFraction(RuntimeValue val)
    {
        if (val is DefaultVal) return new BigFractionVal(0, 1);
        static System.Numerics.BigInteger GetBi(RuntimeValue v) => v is IntVal i ? i.Value : ((BigIntVal)v).Value;
        if (val is IntVal || val is BigIntVal)
            return FunctionVal.From(d =>
                d is not (IntVal or BigIntVal) ? throw new RuntimeException("大分数需要整数分母")
                : GetBi(d).IsZero ? throw new RuntimeException("大分数的分母不能为零")
                : new BigFractionVal(GetBi(val), GetBi(d)));
        if (val is FractionVal fr) return new BigFractionVal(fr.Num, fr.Den);
        if (val is BigFractionVal bf) return bf;
        throw new RuntimeException($"无法将 {val.Type} 转换为 bigfraction");
    }

    private static RuntimeValue CastToException(RuntimeValue val)
        => new ExceptionVal(val.ToString());

    /// <summary>同步类型转换（隐式转换用）：失败抛异常，仅支持安全转换</summary>
    internal static RuntimeValue ConvertDirect(ObjectVal target, RuntimeValue val)
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
            if (target == Function) return FunctionVal.From(_ => VoidVal.Instance);
            return val;
        }

        // 隐式和显式(`int x`)该收同一批东西,差别只在失败时报什么:
        // 这里失败会被 TryConvert 吞掉变成「类型不匹配」,所以越界也走同一套检查
        if (target == Int)
            return val is IntVal i ? i :
                val is FloatVal f ? FromDouble(f.Value) :
                val is StringVal s ? new IntVal(int.TryParse(s.Value, out var sn)
                    ? sn : throw new RuntimeException($"无法将字符串 '{s.Value}' 转换为 int（超出 int 范围或不是数字）")) :
                val is BoolVal b ? new IntVal(b.Value ? 1 : 0) :
                val is BigIntVal bi ? FromBig(bi.Value) :
                throw new RuntimeException($"无法将 {val.Type} 转换为 int");
        if (target == Float)
            return val is IntVal i2 ? new FloatVal(i2.Value) :
                val is FloatVal f2 ? f2 :
                val is BigIntVal bi2 ? new FloatVal((double)bi2.Value) :
                val is StringVal fs ? new FloatVal(double.TryParse(fs.Value, out var fn)
                    ? fn : throw new RuntimeException($"无法将字符串 '{fs.Value}' 转换为 float")) :
                throw new RuntimeException($"无法将 {val.Type} 转换为 float");
        if (target == BigInt)
            // 和 CastToBigInt 对齐:显式 `bigint "123"` 走得通,隐式 `x: bigint = "123"` 也该走得通
            return val is BigIntVal bi2 ? bi2 :
                val is IntVal ib ? new BigIntVal(ib.Value) :
                val is StringVal bs ? new BigIntVal(System.Numerics.BigInteger.TryParse(bs.Value, out var bn) ? bn : throw new RuntimeException($"无法将字符串 '{bs.Value}' 转换为 bigint")) :
                throw new RuntimeException("无法转换为 bigint");
        if (target == String)
            return val is StringVal sv ? sv : new StringVal(val.ToString());
        if (target == Bool)
            return val is BoolVal b3 ? b3 : throw new RuntimeException("无法转换为 bool");
        throw new RuntimeException($"无法将 {val.Type} 转换为 {target.DisplayName}");
    }

}
