namespace Ravel.Runtime;

internal static partial class BuiltinClasses
{

    // ============================================================
    //  类型构造器 / 转换器
    // ============================================================

    private static void RegisterInitializers()
    {
        // 默认构造器挂在 **object** 的类体上:每个类的祖先链都到它,所以任何类只要没写
        // `init` 就继承这一份(`CollectBodies` 从顶祖先往下跑,谁写了谁覆盖掉)。
        // 没有它的话 `C := class { 0; }` 一调就报「类型 C 没有构造器（init）」——
        // 而"什么都不做、把对象交出来"本来就该是默认,和 `function default` 给一个
        // 空函数(`() => { (); }`)是一个道理。
        // 更具体的层写了就覆盖它:比如 List 的预设类体里那个只认 default 的构造器,
        // 它跑在 object 之后,所以 `MyList default` 走的还是 List 那一份。
        BuiltinCtor(Object, DefaultCtor());
        IntCaster = MakeCaster(CastToInt);
        BuiltinCtor(Int, IntCaster);
        BuiltinCtor(Real, MakeCaster(CastToFloat));
        BuiltinCtor(Bool, MakeCaster(CastToBool));
        BuiltinCtor(String, MakeCaster(CastToString));
        BuiltinCtor(Char, MakeCaster(CastToChar));
        BuiltinCtor(BigInt, MakeCaster(CastToBigInt));
        BuiltinCtor(Fraction, MakeCaster(CastToFraction));
        BuiltinCtor(BigFraction, MakeCaster(CastToBigFraction));
        Exception.ClassBody = ExceptionBody();   // 普通类:字段 + 构造器(见下)
        // 错误那一族各拿**自己的一份**(Message 字段 + 那个构造器)。
        //
        // **不能共用同一个块对象** —— 它们和 `Exception` 在**同一条祖先链**上,
        // 同一个块挂两层会被 `Install` 当成"同一个块重复挂"挡下来(那是对的:
        // 实例化时它会跑两遍,里面那条 `Message := …` 第二次静默盖掉第一次)。
        // 从前这儿是 `var errBody = Exception.ClassBody;` 大家共用一份,靠"类体不可变、
        // 又都平铺进同一个实例作用域"说没关系 —— 那是绕开判据,不是判据不对。
        TypeError.ClassBody = ExceptionBody();
        NameError.ClassBody = ExceptionBody();
        AttributeError.ClassBody = ExceptionBody();
        IndexError.ClassBody = ExceptionBody();
        KeyError.ClassBody = ExceptionBody();
        ZeroDivisionError.ClassBody = ExceptionBody();
        AssertionError.ClassBody = ExceptionBody();
        AccessError.ClassBody = ExceptionBody();
        ArgumentError.ClassBody = ExceptionBody();
        ValueError.ClassBody = ExceptionBody();
        IoError.ClassBody = ExceptionBody();
        RegexError.ClassBody = ExceptionBody();
        BuiltinCtor(Json, MakeCaster(CastToJson));   // 原生值 → Json(见 BuiltinClasses.Json.cs)
        // `function ()` 就是空函数。函数没有"零值"之外的内容,这一格留着;
        // 值类型(`int` / `string` …)那些不留 —— 它们的 `()` 不是默认值,默认值走 `default`。
        BuiltinCtor(Function, FunctionVal.From(v => v is VoidVal
            ? EmptyFunction()
            : throw new RuntimeException($"function 收一枚函数，得到 {v.Type}", ErrorKind.Type)));
        BuiltinCtor(List, MakeDefaultCaster(List));
        // `Continuation f` —— 把一枚函数**当成**续延(调它 = 调那枚函数)。
        // 库里的 `callcc` 靠这条把"先还原控制状态、再跳回去"那层也做成续延:
        // 于是用户手里那枚**类型上**就是 `Continuation`,而不是一个说不清的 lambda。
        // `Continuation ()` 是"还没到手的那一枚"(见 ContinuationVal.Default);这一格留着。
        BuiltinCtor(Continuation, FunctionVal.From(val => val switch
        {
            VoidVal => ContinuationVal.Default,
            ContinuationVal c => c,                                  // 已经是续延,原样(包两层没意义)
            FunctionVal f => new ContinuationVal((Frame?)null, f),
            _ => throw new RuntimeException($"Continuation 收一枚函数，得到 {val.Type}", ErrorKind.Type),
        }));
        // `Pair k v` —— 值类型的构造器一次只收一格(与 `int` / `bool` 同一套口子),
        // 所以第一格交回"还差第二格"的函数。造出来的一对不可变,也没有第二条路。
        BuiltinCtor(Pair, FunctionVal.From(k => FunctionVal.From(v => (RuntimeValue)new PairVal(k, v))));
        BuiltinCtor(Set, MakeDefaultCaster(Set));
        BuiltinCtor(Dict, MakeDefaultCaster(Dict));
        // 建类不在这里:`type` 的 init 由 InstallTypeInit 装 —— 它要用 NativeClosure
        // 看见正在构造的那个对象的 `this`,而且必须在 Object/Function/Type 都挂好之后
    }

    private static FunctionVal DefaultCtor()
        => new NativeClosure("_", Object, (scope, _) => scope.Lookup(ObjectVal.ThisMember).Value);

    internal static FunctionVal? IntCaster;

    internal static FunctionVal MakeCaster(Func<RuntimeValue, RuntimeValue> cast)
        => FunctionVal.From(cast);

    private static FunctionVal MakeDefaultCaster(ObjectVal type)
        => MakeCaster(_ => throw new RuntimeException($"类型 {type.DisplayName} 不能作为构造器调用", ErrorKind.Type));

    private static IntVal FromBig(System.Numerics.BigInteger v)
        => v >= int.MinValue && v <= int.MaxValue
            ? IntVal.Of((int)v)
            : throw new RuntimeException($"数值 {v} 超出 int 范围（int 是 32 位，大数用 bigint）", ErrorKind.Value);

    private static IntVal FromDouble(double v)
    {
        if (double.IsNaN(v)) throw new RuntimeException("NaN 不能转换为 int", ErrorKind.Value);
        if (v < int.MinValue || v > int.MaxValue)
            // 插值用 RealVal 而不是裸 double:后者的无穷是"∞",值的形式该是 ASCII
            throw new RuntimeException($"数值 {new RealVal(v)} 超出 int 范围（int 是 32 位，大数用 bigint）", ErrorKind.Value);
        return IntVal.Of((int)v);
    }

    internal static RuntimeValue CastToInt(RuntimeValue val)
    {
        if (val is IntVal i) return i;
        // 字符就是它的码位(`int 'A'` → 65),反过来的 `char 65` 在 CastToChar 那边
        if (val is CharVal ch) return IntVal.Of(ch.Value);
        if (val is StringVal s)
        {
            if (int.TryParse(s.Value, out var n)) return IntVal.Of(n);
            throw new RuntimeException($"无法将字符串 '{s.Value}' 转换为 int（超出 int 范围或不是数字）", ErrorKind.Value);
        }

        if (val is BoolVal b) return IntVal.Of(b.Value ? 1 : 0);
        if (val is RealVal f) return FromDouble(f.Value);
        if (val is BigIntVal bi) return FromBig(bi.Value);
        // 分数先在 BigInteger 里除，免得 int 除法自己先溢出（MinValue / -1）
        if (val is FractionVal fr) return FromBig((System.Numerics.BigInteger)fr.Num / fr.Den);
        if (val is BigFractionVal bfr) return FromBig(bfr.Num / bfr.Den);
        throw new RuntimeException($"无法将 {val.Type} 转换为 int", ErrorKind.Type);
    }

    internal static RuntimeValue CastToFloat(RuntimeValue val)
    {
        if (val is IntVal i) return new RealVal(i.Value);
        if (val is RealVal f) return f;
        // bigint → real 是拓宽,顺手接上(以前 `real (bigint 5)` 报「无法转换为 real」,
        // 而 `bigint 5 + 1.0` 却算得出来,两边对不上)
        if (val is BigIntVal bi) return new RealVal((double)bi.Value);
        if (val is StringVal s)
        {
            if (double.TryParse(s.Value, out var n)) return new RealVal(n);
            throw new RuntimeException($"无法将字符串 '{s.Value}' 转换为 real", ErrorKind.Value);
        }

        throw new RuntimeException($"无法将 {val.Type} 转换为 real", ErrorKind.Type);
    }

    // bool/string 不写成 `ConvertDirect(...)`,那是**互相递归**(ConvertDirect 正是按类型
    // 分发到这几个 CastToXxx 上),会转不出来。
    internal static RuntimeValue CastToBool(RuntimeValue val)
        => val is BoolVal b ? b
            : throw new RuntimeException($"无法将 {val.Type} 转换为 bool", ErrorKind.Type);

    internal static RuntimeValue CastToString(RuntimeValue val)
        => val is StringVal s ? s
            : new StringVal(val.ToString());

    internal static RuntimeValue CastToChar(RuntimeValue val) => val switch
    {
        CharVal c => c,
        IntVal i => i.Value is >= 0 and <= char.MaxValue
            ? new CharVal((char)i.Value)
            : throw new RuntimeException($"char: 码位 {i.Value} 超出范围（0..65535）", ErrorKind.Value),
        StringVal s when s.Value.Length == 1 => new CharVal(s.Value[0]),
        StringVal s => throw new RuntimeException($"char: 字符串得正好一个字符，得到 {s.Value.Length} 个", ErrorKind.Value),
        _ => throw new RuntimeException($"char: 转不了 {val.Type}", ErrorKind.Type),
    };

    internal static RuntimeValue CastToBigInt(RuntimeValue val)
    {
        if (val is IntVal i) return new BigIntVal(i.Value);
        if (val is BigIntVal bi) return bi;
        if (val is StringVal s)
        {
            if (System.Numerics.BigInteger.TryParse(s.Value, out var n)) return new BigIntVal(n);
            throw new RuntimeException("无法将字符串转换为 bigint", ErrorKind.Value);
        }

        if (val is RealVal f)
            // `(BigInteger)double` 对 NaN/Inf 抛的是 C# 的 OverflowException ——
            // 它不是 RuntimeException,Ravel 的 try 接不住,会一路把程序打掉。
            return double.IsNaN(f.Value) || double.IsInfinity(f.Value)
                ? throw new RuntimeException($"数值 {f} 不能转换为 bigint（NaN 和无穷没有对应的整数）", ErrorKind.Value)
                : new BigIntVal((System.Numerics.BigInteger)f.Value);
        throw new RuntimeException($"无法将 {val.Type} 转换为 bigint", ErrorKind.Type);
    }

    internal static RuntimeValue CastToFraction(RuntimeValue val)
    {
        // int 当分子,再收一个 int 分母
        if (val is IntVal i)
            return FunctionVal.From(d =>
                d is not IntVal dd ? throw new RuntimeException("分数需要 int 分母", ErrorKind.Type)
                : dd.Value != 0 ? new FractionVal(i.Value, dd.Value)
                : throw new RuntimeException("分数的分母不能为零", ErrorKind.ZeroDivision));
        if (val is FractionVal f) return f;
        if (val is StringVal s)
        {
            var p = s.Value.Split('/');
            if (p.Length == 2 && int.TryParse(p[0], out var n) && int.TryParse(p[1], out var d) && d != 0)
                return new FractionVal(n, d);
            throw new RuntimeException("无效的分数字符串", ErrorKind.Value);
        }

        throw new RuntimeException($"无法将 {val.Type} 转换为 fraction", ErrorKind.Type);
    }

    internal static RuntimeValue CastToBigFraction(RuntimeValue val)
    {
        static System.Numerics.BigInteger GetBi(RuntimeValue v) => v is IntVal i ? i.Value : ((BigIntVal)v).Value;
        if (val is IntVal || val is BigIntVal)
            return FunctionVal.From(d =>
                d is not (IntVal or BigIntVal) ? throw new RuntimeException("大分数需要整数分母", ErrorKind.Type)
                : GetBi(d).IsZero ? throw new RuntimeException("大分数的分母不能为零", ErrorKind.ZeroDivision)
                : new BigFractionVal(GetBi(val), GetBi(d)));
        if (val is FractionVal fr) return new BigFractionVal(fr.Num, fr.Den);
        if (val is BigFractionVal bf) return bf;
        throw new RuntimeException($"无法将 {val.Type} 转换为 bigfraction", ErrorKind.Type);
    }

    private static BlockVal ExceptionBody()
    {
        var ctor = new NativeClosure("msg", Object, (scope, val) =>
        {
            var self = (ObjectVal)scope.Lookup(ObjectVal.ThisMember).Value;
            var msg = val switch
            {
                VoidVal => "",
                StringVal s => s.Value,
                _ => val.ToString() ?? "",
            };
            self.Scope.DefineOrReplace(MessageMember, new StringVal(msg));
            return self;                    // ← 交回 this:子类不写 init 也保住自己
        });

        return new BlockVal(
            new BlockExpr([
                // 注解省了:`""` 自己就把类型定成 String(预设类体的 scope 是**孤立**的,
                // 写 `new IdentifierExpr("string")` 会因为找不到 `string` 而报错)
                new VarDefinition(MessageMember, null, new StringLiteral(""))
                    { Line = 1, Column = 1, Preset = true },
                new VarDefinition(ObjectVal.InitMember, null, new LiteralExpr(ctor))
                    { Line = 1, Column = 1, Preset = true },
            ]) { Line = 1, Column = 1, Source = "<preset>" },
            new Scope());
    }

    internal const string MessageMember = "Message";

    internal static string? ExceptionMessage(RuntimeValue v)
        => v is ObjectVal { IsClass: false } o && o.ClassType.IsAssignableTo(Exception)
            ? o.Scope.LookupField(MessageMember)?.Value.ToString()
            : null;

    internal static RuntimeValue ConvertDirect(ObjectVal target, RuntimeValue val)
    {
        if (target == Int) return CastToInt(val);
        if (target == Real) return CastToFloat(val);
        if (target == Bool) return CastToBool(val);
        if (target == String) return CastToString(val);
        if (target == Json) return CastToJson(val);      // `x: Json = …` 的隐式转换
        if (target == BigInt) return CastToBigInt(val);
        // 容器、分数这些没有"从一个值转换过来"这回事;要空的那只走 `default`
        // (`BuiltinClasses.DefaultValue`)。函数和续延的 `()` 例外,留着。
        // 分数那两条也别按目标类整批委派:**它们对别的值有别的意思**
        // (`fraction 3` 是"还等一个分母",交出的是半个构造器)。
        if (val is VoidVal)
        {
            if (target == Function) return EmptyFunction();
            // 续延的"空值"不是"什么都不做",而是"还没到手的那一枚" —— 它一调就报错
            // (续延调用是跳转,没有目的地就该响;见 ContinuationVal.Default)
            if (target == Continuation) return ContinuationVal.Default;
        }

        throw new RuntimeException($"无法将 {val.Type} 转换为 {target.DisplayName}", ErrorKind.Type);
    }

    // `default T` 的全部内容:每个"有零值"的类型造一个空的。
    //   · 只有这些有零值;别的类型(用户类 / `object` / 异常族…)没有,直接报错 ——
    //     `()` 不是空值,拿 `()` 去调一个值类型也不再是"给我默认值"。
    internal static RuntimeValue DefaultValue(ObjectVal type)
    {
        if (type == Int) return IntVal.Of(0);
        if (type == Real) return new RealVal(0);
        if (type == Bool) return new BoolVal(false);
        if (type == String) return new StringVal("");
        if (type == Char) return new CharVal('\0');
        if (type == BigInt) return new BigIntVal(0);
        if (type == Fraction) return new FractionVal(0, 1);
        if (type == BigFraction) return new BigFractionVal(0, 1);
        if (type == List) return new ListVal([]);
        if (type == Set) return new SetVal([]);
        if (type == Dict) return new DictVal([]);
        if (type == Function) return EmptyFunction();
        if (type == Continuation) return ContinuationVal.Default;
        throw new RuntimeException($"类型 {type.DisplayName} 没有默认值", ErrorKind.Type);
    }

}
