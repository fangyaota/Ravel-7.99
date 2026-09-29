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
        // 默认构造器挂在 **object** 的类体上:每个类的祖先链都到它,所以任何类只要没写
        // `init` 就继承这一份(`CollectBodies` 从顶祖先往下跑,谁写了谁覆盖掉)。
        // 没有它的话 `C := class { 0; }` 一调就报「类型 C 没有构造器（init）」——
        // 而"什么都不做、把对象交出来"本来就该是默认,和 `function default` 给一个
        // 空函数(`() => { (); }`)是一个道理。
        // 更具体的层写了就覆盖它:比如 List 的预设类体里那个只认 default 的构造器,
        // 它跑在 object 之后,所以 `MyList default` 走的还是 List 那一份。
        Object.ClassBody = PresetCtor(DefaultCtor());
        Int.ClassBody = PresetCtor(MakeCaster(CastToInt));
        Float.ClassBody = PresetCtor(MakeCaster(CastToFloat));
        Bool.ClassBody = PresetCtor(MakeCaster(CastToBool));
        String.ClassBody = PresetCtor(MakeCaster(CastToString));
        Char.ClassBody = PresetCtor(MakeCaster(CastToChar));
        BigInt.ClassBody = PresetCtor(MakeCaster(CastToBigInt));
        Fraction.ClassBody = PresetCtor(MakeCaster(CastToFraction));
        BigFraction.ClassBody = PresetCtor(MakeCaster(CastToBigFraction));
        Exception.ClassBody = PresetCtor(MakeCaster(CastToException));
        List.ClassBody = PresetCtor(MakeDefaultCaster(List));
        Set.ClassBody = PresetCtor(MakeDefaultCaster(Set));
        Dict.ClassBody = PresetCtor(MakeDefaultCaster(Dict));
        // 建类不在这里:`type` 的 init 由 InstallTypeInit 装 —— 它要用 NativeClosure
        // 看见正在构造的那个对象的 `this`,而且必须在 Object/Function/Type 都挂好之后
    }

    /// <summary>默认构造器 `init := () => { this; }`:什么都不做,把正在建的那个对象交回去。
    ///
    /// 必须是 <see cref="NativeClosure"/>:要交出的 `this` 只在**调用点作用域**里
    /// (那正是实例作用域),C# 侧的普通 `FunctionVal` 拿不到作用域。
    /// 参数标 `Any`(什么都收):`Bare ()` 传进来的是 void 值、`Bare default` 是 default。</summary>
    private static FunctionVal DefaultCtor()
        => new NativeClosure("_", Any, (scope, _) => scope.Lookup(ObjectVal.ThisMember).Value);

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
            // 插值用 FloatVal 而不是裸 double:后者的无穷是"∞",值的形式该是 ASCII
            throw new RuntimeException($"数值 {new FloatVal(v)} 超出 int 范围（int 是 32 位，大数用 bigint）");
        return new IntVal((int)v);
    }

    private static RuntimeValue CastToInt(RuntimeValue val)
    {
        if (val is DefaultVal) return new IntVal(0);
        if (val is IntVal i) return i;
        // 字符就是它的码位(`int 'A'` → 65),反过来的 `char 65` 在 CastToChar 那边
        if (val is CharVal ch) return new IntVal(ch.Value);
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

    // bool/string 不写成 `ConvertDirect(...)`,那是**互相递归**(ConvertDirect 正是按类型
    // 分发到这几个 CastToXxx 上),会转不出来。
    private static RuntimeValue CastToBool(RuntimeValue val)
        => val is DefaultVal ? new BoolVal(false)
            : val is BoolVal b ? b
            : throw new RuntimeException($"无法将 {val.Type} 转换为 bool");

    private static RuntimeValue CastToString(RuntimeValue val)
        => val is DefaultVal ? new StringVal("")
            : val is StringVal s ? s
            : new StringVal(val.ToString());

    /// <summary>`char x` —— 从数(码位)、字符串(长度必须是 1)或者另一个 char 转过来。
    /// 越界/长度不对**当场报错**,不做截断 —— 悄悄给个错字符比报错难查得多。</summary>
    private static RuntimeValue CastToChar(RuntimeValue val) => val switch
    {
        CharVal c => c,
        DefaultVal => new CharVal('\0'),
        IntVal i => i.Value is >= 0 and <= char.MaxValue
            ? new CharVal((char)i.Value)
            : throw new RuntimeException($"char: 码位 {i.Value} 超出范围（0..65535）"),
        StringVal s when s.Value.Length == 1 => new CharVal(s.Value[0]),
        StringVal s => throw new RuntimeException($"char: 字符串得正好一个字符，得到 {s.Value.Length} 个"),
        _ => throw new RuntimeException($"char: 转不了 {val.Type}"),
    };

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

        if (val is FloatVal f)
            // `(BigInteger)double` 对 NaN/Inf 抛的是 C# 的 OverflowException ——
            // 它不是 RuntimeException,Ravel 的 try 接不住,会一路把程序打掉。
            return double.IsNaN(f.Value) || double.IsInfinity(f.Value)
                ? throw new RuntimeException($"数值 {f} 不能转换为 bigint（NaN 和无穷没有对应的整数）")
                : new BigIntVal((System.Numerics.BigInteger)f.Value);
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

    /// <summary>同步类型转换（隐式转换用）：失败抛异常，仅支持安全转换。
    ///
    /// **只做分发** —— 每个类型"能收什么"写在各类型的 `CastToXxx` 里,这里按目标类选一个。
    /// 从前每个类型在这又抄了一遍,于是显式 `int (fraction 7 2)` 走得通而
    /// `x: int = fraction 7 2` 报「类型不匹配」、`bigint 1.5` 走得通而隐式不行:
    /// 同一件事两条路两种结果(见 `tests/159`)。隐式失败会被 `TryConvert` 吞掉变成
    /// 「类型不匹配」,所以越界也照常走同一套检查、报同一句话。
    ///
    /// 这里的失败是**正常**的:`TryConvert` 靠它决定"不值一换",
    /// 不像 `CastToXxx` 由构造器调用时那样等于直接报错。</summary>
    internal static RuntimeValue ConvertDirect(ObjectVal target, RuntimeValue val)
    {
        if (target == Int) return CastToInt(val);
        if (target == Float) return CastToFloat(val);
        if (target == Bool) return CastToBool(val);
        if (target == String) return CastToString(val);
        if (target == BigInt) return CastToBigInt(val);
        // 容器和函数没有"从一个值转换过来"这回事,只认 default(空容器 / 空函数)。
        // 分数那两条也在这一格:**它们对别的值有别的意思**(`fraction 3` 是"还等一个分母",
        // 交出的是半个构造器),不能像上面那样按目标类整批委派。
        if (val is DefaultVal)
        {
            if (target == List) return new ListVal([]);
            if (target == Set) return new SetVal([]);
            if (target == Dict) return new DictVal([]);
            if (target == Function) return FunctionVal.From(_ => VoidVal.Instance);
            if (target == Fraction) return new FractionVal(0, 1);
            if (target == BigFraction) return new BigFractionVal(0, 1);
            return val;
        }

        throw new RuntimeException($"无法将 {val.Type} 转换为 {target.DisplayName}");
    }

}
