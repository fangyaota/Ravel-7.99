namespace Ravel.Runtime;

public partial class RuntimeType
{
    // ============================================================
    //  类型构造器 / 转换器
    // ============================================================

    /// <summary>注册各内建类型的 Initializer（调用该类型时执行转换）</summary>
    private static void RegisterInitializers()
    {
        Int.Initializer = MakeCaster(CastToInt);
        Float.Initializer = MakeCaster(CastToFloat);
        Bool.Initializer = MakeCaster(CastToBool);
        String.Initializer = MakeCaster(CastToString);
        BigInt.Initializer = MakeCaster(CastToBigInt);
        Fraction.Initializer = MakeCaster(CastToFraction);
        BigFraction.Initializer = MakeCaster(CastToBigFraction);
        Exception.Initializer = MakeCaster(CastToException);
        List.Initializer = MakeDefaultCaster(List);
        Set.Initializer = MakeDefaultCaster(Set);
        Dict.Initializer = MakeDefaultCaster(Dict);
        Class.Initializer = MakeClassInitializer();
    }

    /// <summary>包装转换函数为单参构造器</summary>
    private static FunctionVal MakeCaster(Func<RuntimeValue, RuntimeValue> cast)
        => FunctionVal.From(cast);

    /// <summary>仅支持 default → 默认值的构造器（集合等不可直接构造的类型）</summary>
    private static FunctionVal MakeDefaultCaster(RuntimeType type)
        => MakeCaster(val => val is DefaultVal
            ? ConvertDirect(type, val)
            : throw new RuntimeException($"类型 {type.DisplayName} 不能作为构造器调用"));

    private static RuntimeValue CastToInt(RuntimeValue val)
    {
        if (val is DefaultVal) return new IntVal(0);
        if (val is IntVal i) return i;
        if (val is StringVal s)
        {
            if (int.TryParse(s.Value, out var n)) return new IntVal(n);
            throw new RuntimeException("无法将字符串转换为 int");
        }

        if (val is BoolVal b) return new IntVal(b.Value ? 1 : 0);
        if (val is FloatVal f) return new IntVal((int)f.Value);
        if (val is BigIntVal bi) return new IntVal((int)bi.Value);
        if (val is FractionVal fr) return new IntVal(fr.Num / fr.Den);
        if (val is BigFractionVal bfr) return new IntVal((int)(bfr.Num / bfr.Den));
        throw new RuntimeException($"无法将 {val.Type} 转换为 int");
    }

    private static RuntimeValue CastToFloat(RuntimeValue val)
    {
        if (val is DefaultVal) return new FloatVal(0);
        if (val is IntVal i) return new FloatVal(i.Value);
        if (val is FloatVal f) return f;
        if (val is StringVal s)
        {
            if (double.TryParse(s.Value, out var n)) return new FloatVal(n);
            throw new RuntimeException("无法将字符串转换为 float");
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
            if (target == Function) return FunctionVal.From(_ => VoidVal.Instance);
            return val;
        }

        if (target == Int)
            return val is IntVal i ? i :
                val is FloatVal f ? new IntVal((int)f.Value) :
                val is StringVal s ? new IntVal(int.TryParse(s.Value, out var sn) ? sn : throw new RuntimeException($"无法将字符串 '{s.Value}' 转换为 int")) :
                val is BoolVal b ? new IntVal(b.Value ? 1 : 0) :
                throw new RuntimeException("无法转换为 int");
        if (target == Float)
            return val is IntVal i2 ? new FloatVal(i2.Value) :
                val is FloatVal f2 ? f2 :
                val is StringVal fs ? new FloatVal(double.TryParse(fs.Value, out var fn) ? fn : throw new RuntimeException($"无法将字符串 '{fs.Value}' 转换为 float")) :
                throw new RuntimeException("无法转换为 float");
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
        throw new RuntimeException("无法转换类型");
    }

    /// <summary>class 构造器：class block（默认父类 object）或 class parent block（柯里化）</summary>
    private static FunctionVal MakeClassInitializer()
        => FunctionVal.From(a =>
        {
            if (a is BlockVal block)
                return CreateClass(Object, block);
            if (a is TypeVal parent)
                return FunctionVal.From(b =>
                    b is BlockVal bb
                        ? CreateClass(parent.Value, bb)
                        : throw new RuntimeException("class 需要代码块参数"));
            throw new RuntimeException("class 参数必须是类型或代码块");
        });

    /// <summary>创建类：建 RuntimeType，存 body，扫描用符号定义的运算符注册到方法表</summary>
    private static TypeVal CreateClass(RuntimeType parent, BlockVal block)
    {
        var newType = Define("", parent);
        newType.Metaclass = Class;
        newType.Body = block;
        // 类体里用符号定义的运算符(`+ := f` 定义、`+ = f` 覆盖)注册到类型的方法表
        foreach (var stmt in block.Block.Statements)
        {
            var op = stmt switch
            {
                VarDefinition v when v.IsOperator => v.Name,
                Assignment a when OperatorSymbols.IsSymbol(a.Name) => a.Name,
                _ => null,
            };
            if (op != null) newType.DefineClassOperator(op);
        }
        return new TypeVal(newType);
    }

    /// <summary>沿 Parent 链收集各层类体，返回「顶祖先 → 自身」。Body == null 的层(内建类型)不入列且到此为止。
    /// Parent/Body 创建后不可变,所以这是纯函数,可随帧推进反复调用。</summary>
    internal static List<BlockVal> CollectBodies(RuntimeType type)
    {
        var layers = new List<BlockVal>();
        for (var t = type; ; t = t.Parent)
        {
            var body = t.Body;
            if (body == null) break;
            layers.Add(body);
            if (t.Parent == t) break;
        }

        layers.Reverse();
        return layers;
    }
}
