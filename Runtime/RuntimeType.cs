namespace Ravel.Runtime;

/// <summary>
/// 运行时类型描述符。类型本身是 object 的值，类型的类型是 type。
/// </summary>
public class RuntimeType
{
    public string Name { get; set; }
    public RuntimeType Parent { get; private set; }

    /// <summary>操作符表：op → FunctionVal（CPS 兼容）</summary>
    public readonly Dictionary<string, RuntimeValue.FunctionVal> Operators = new();
    /// <summary>方法表：方法名 → 实现（self + args → result）</summary>
    private readonly Dictionary<string, Func<RuntimeValue, RuntimeValue[], RuntimeValue>> _methods = new();
    public IEnumerable<string> MethodNames => _methods.Keys;
    /// <summary>原型 scope：类定义时 block 执行结果</summary>
    public Scope? Proto { get; set; }
    internal RuntimeValue.FunctionVal? Initializer { get; set; }
    internal RuntimeType? MetaClass { get; set; }

    private RuntimeType(string name, RuntimeType? parent)
    {
        Name = name;
        Parent = parent!;
    }

    // ============================================================
    //  类型层次 — 静态单例
    // ============================================================

    // 顶类型
    public static readonly RuntimeType Object;

    // 值类型分支（不可变）
    public static readonly RuntimeType ValueType;
    public static readonly RuntimeType Int;
    public static readonly RuntimeType Float;
    public static readonly RuntimeType Bool;
    public static readonly RuntimeType String;
    public static readonly RuntimeType BigInt;
    public static readonly RuntimeType Fraction;
    public static readonly RuntimeType BigFraction;

    // 引用类型分支（可变/有行为）
    public static readonly RuntimeType Class;
    public static readonly RuntimeType Function;
    public static readonly RuntimeType List;
    public static readonly RuntimeType Set;
    public static readonly RuntimeType Dict;

    // void — 唯一值 ()
    public static readonly RuntimeType Void;

    // 元类型 — 所有类型的类型
    public static readonly RuntimeType Type;

    // 底类型 — 不在继承树中，IsAssignableTo 全局特判
    // 底类型 — 为所有类的子类，default 是其唯一实例
    public static readonly RuntimeType Every;
    // 顶类型
    public static readonly RuntimeType Any;
        public static readonly RuntimeType Exception;
        public static readonly RuntimeType Ravel;
        public static readonly RuntimeType ScopeType;
        public static readonly RuntimeType Property;

    // 内置运算符表：(类型, 运算符) → 实现
    private static readonly Dictionary<(RuntimeType, string), Func<RuntimeValue, RuntimeValue, RuntimeValue>> _builtinOps = new();

    // 静态初始化 — 先创建所有 null，再通过反射赋值 Parent
    static RuntimeType()
    {
        // 先创建 object（自引用）
        Object = new RuntimeType("Object", null!);
        Object.Parent = Object;

        // 分支
        ValueType = new RuntimeType("ValueType", Object);
        Class     = new RuntimeType("Class", Object);

        // 值类型
        Int    = new RuntimeType("Integer", ValueType);
        Float  = new RuntimeType("Float", ValueType);
        Bool   = new RuntimeType("Bool", ValueType);
        String = new RuntimeType("String", ValueType);
        BigInt = new RuntimeType("BigInt", ValueType);
        Fraction = new RuntimeType("Fraction", ValueType);
        BigFraction = new RuntimeType("BigFraction", ValueType);

        // 引用类型
        Function = new RuntimeType("Function", Object);
        List     = new RuntimeType("List", Object);
        Set      = new RuntimeType("Set", Object);
        Dict     = new RuntimeType("Dict", Object);

        // void
        Void = new RuntimeType("Void", Object);

        // 元类型
        Type = new RuntimeType("Type", Function);  // type 是函数，可作为构造器
        Class.Parent = Type;  // class <: type <: function <: object

        // 底类型
        Ravel = new RuntimeType("Ravel", Object);
        ScopeType = new RuntimeType("Scope", Object);
        Property = new RuntimeType("Property", Object);
        Exception = new RuntimeType("Exception", Object);
        // 底类型 — default 的类型
        Every = new RuntimeType("Every", null!);
        // 顶类型
        // 顶类型 — 任何类型都可赋值给它，Any 类型变量跳过类型检查
        Any = new RuntimeType("Any", Object);

        // ---- 注册内置运算符 ----
        RegisterBuiltinOps();
    }

    static void RegisterBuiltinOps()
    {
        // int 运算符
        _builtinOps[(Int, "+")] = (a, b) => {
            if(b is RuntimeValue.FloatVal fb) return new RuntimeValue.FloatVal(((RuntimeValue.IntVal)a).Value + fb.Value);
            return new RuntimeValue.IntVal(((RuntimeValue.IntVal)a).Value + ((RuntimeValue.IntVal)b).Value);
        };
        _builtinOps[(Int, "-")] = (a, b) => {
            if(b is RuntimeValue.FloatVal fb) return new RuntimeValue.FloatVal(((RuntimeValue.IntVal)a).Value - fb.Value);
            return new RuntimeValue.IntVal(((RuntimeValue.IntVal)a).Value - ((RuntimeValue.IntVal)b).Value);
        };
        _builtinOps[(Int, "*")] = (a, b) => {
            if(b is RuntimeValue.FloatVal fb) return new RuntimeValue.FloatVal(((RuntimeValue.IntVal)a).Value * fb.Value);
            return new RuntimeValue.IntVal(((RuntimeValue.IntVal)a).Value * ((RuntimeValue.IntVal)b).Value);
        };
        _builtinOps[(Int, "/")] = (a, b) => {
            if(b is RuntimeValue.FloatVal fb) return new RuntimeValue.FloatVal(((RuntimeValue.IntVal)a).Value / fb.Value);
            return new RuntimeValue.IntVal(((RuntimeValue.IntVal)a).Value / ((RuntimeValue.IntVal)b).Value);
        };
        _builtinOps[(Int, "%")] = (a, b) => new RuntimeValue.IntVal(((RuntimeValue.IntVal)a).Value % ((RuntimeValue.IntVal)b).Value);

        // float 运算符
        _builtinOps[(Float, "+")] = (a, b) => new RuntimeValue.FloatVal(AsFloat(a) + AsFloat(b));
        _builtinOps[(Float, "-")] = (a, b) => new RuntimeValue.FloatVal(AsFloat(a) - AsFloat(b));
        _builtinOps[(Float, "*")] = (a, b) => new RuntimeValue.FloatVal(AsFloat(a) * AsFloat(b));
        _builtinOps[(Float, "/")] = (a, b) => new RuntimeValue.FloatVal(AsFloat(a) / AsFloat(b));
        _builtinOps[(Float, "%")] = (a, b) => new RuntimeValue.FloatVal(AsFloat(a) % AsFloat(b));

        // BigInt 运算符
        _builtinOps[(BigInt, "+")] = (a, b) => new RuntimeValue.BigIntVal(AsBigInt(a) + AsBigInt(b));
        _builtinOps[(BigInt, "-")] = (a, b) => new RuntimeValue.BigIntVal(AsBigInt(a) - AsBigInt(b));
        _builtinOps[(BigInt, "*")] = (a, b) => new RuntimeValue.BigIntVal(AsBigInt(a) * AsBigInt(b));
        _builtinOps[(BigInt, "/")] = (a, b) => new RuntimeValue.BigIntVal(AsBigInt(a) / AsBigInt(b));
        _builtinOps[(BigInt, "%")] = (a, b) => new RuntimeValue.BigIntVal(AsBigInt(a) % AsBigInt(b));

        // Fraction 运算符
        _builtinOps[(Fraction, "+")] = (a, b) => FractionBinOp(a, b, (na,da,nb,db)=>new RuntimeValue.FractionVal(na*db+nb*da, da*db));
        _builtinOps[(Fraction, "-")] = (a, b) => FractionBinOp(a, b, (na,da,nb,db)=>new RuntimeValue.FractionVal(na*db-nb*da, da*db));
        _builtinOps[(Fraction, "*")] = (a, b) => FractionBinOp(a, b, (na,da,nb,db)=>new RuntimeValue.FractionVal(na*nb, da*db));
        _builtinOps[(Fraction, "/")] = (a, b) => FractionBinOp(a, b, (na,da,nb,db)=>new RuntimeValue.FractionVal(na*db, da*nb));

        // BigFraction 运算符
        _builtinOps[(BigFraction, "+")] = (a, b) => BigFractionBinOp(a, b, (na,da,nb,db)=>new RuntimeValue.BigFractionVal(na*db+nb*da, da*db));
        _builtinOps[(BigFraction, "-")] = (a, b) => BigFractionBinOp(a, b, (na,da,nb,db)=>new RuntimeValue.BigFractionVal(na*db-nb*da, da*db));
        _builtinOps[(BigFraction, "*")] = (a, b) => BigFractionBinOp(a, b, (na,da,nb,db)=>new RuntimeValue.BigFractionVal(na*nb, da*db));
        _builtinOps[(BigFraction, "/")] = (a, b) => BigFractionBinOp(a, b, (na,da,nb,db)=>new RuntimeValue.BigFractionVal(na*db, da*nb));

        // 比较运算符 — 数字
        foreach(var t in new[]{Int, Float, BigInt, Fraction, BigFraction}){
            _builtinOps[(t, "==")] = (a, b) => new RuntimeValue.BoolVal(AsDouble(a) == AsDouble(b));
            _builtinOps[(t, "!=")] = (a, b) => new RuntimeValue.BoolVal(AsDouble(a) != AsDouble(b));
            _builtinOps[(t, "<")] = (a, b) => new RuntimeValue.BoolVal(AsDouble(a) < AsDouble(b));
            _builtinOps[(t, ">")] = (a, b) => new RuntimeValue.BoolVal(AsDouble(a) > AsDouble(b));
            _builtinOps[(t, "<=")] = (a, b) => new RuntimeValue.BoolVal(AsDouble(a) <= AsDouble(b));
            _builtinOps[(t, ">=")] = (a, b) => new RuntimeValue.BoolVal(AsDouble(a) >= AsDouble(b));
        }
        // bool 比较
        _builtinOps[(Bool, "==")] = (a, b) => new RuntimeValue.BoolVal(((RuntimeValue.BoolVal)a).Value == ((RuntimeValue.BoolVal)b).Value);
        _builtinOps[(Bool, "!=")] = (a, b) => new RuntimeValue.BoolVal(((RuntimeValue.BoolVal)a).Value != ((RuntimeValue.BoolVal)b).Value);
        // string 比较
        _builtinOps[(String, "==")] = (a, b) => new RuntimeValue.BoolVal(((RuntimeValue.StringVal)a).Value == ((RuntimeValue.StringVal)b).Value);
        _builtinOps[(String, "!=")] = (a, b) => new RuntimeValue.BoolVal(((RuntimeValue.StringVal)a).Value != ((RuntimeValue.StringVal)b).Value);
        // string 拼接
        _builtinOps[(String, "+")] = (a, b) => new RuntimeValue.StringVal(((RuntimeValue.StringVal)a).Value + ((RuntimeValue.StringVal)b).Value);
        _builtinOps[(Type, "==")] = (a, b) => new RuntimeValue.BoolVal(((RuntimeValue.TypeVal)a).Value == ((RuntimeValue.TypeVal)b).Value);
        _builtinOps[(Type, "!=")] = (a, b) => new RuntimeValue.BoolVal(((RuntimeValue.TypeVal)a).Value != ((RuntimeValue.TypeVal)b).Value);

        // bool 逻辑运算符
        _builtinOps[(Bool, "&")] = (a, b) => new RuntimeValue.BoolVal(((RuntimeValue.BoolVal)a).Value && ((RuntimeValue.BoolVal)b).Value);
        _builtinOps[(Bool, "|")] = (a, b) => new RuntimeValue.BoolVal(((RuntimeValue.BoolVal)a).Value || ((RuntimeValue.BoolVal)b).Value);
        _builtinOps[(Bool, "^")] = (a, b) => new RuntimeValue.BoolVal(((RuntimeValue.BoolVal)a).Value ^ ((RuntimeValue.BoolVal)b).Value);
        // int 位运算符
        _builtinOps[(Int, "&")] = (a, b) => new RuntimeValue.IntVal(((RuntimeValue.IntVal)a).Value & ((RuntimeValue.IntVal)b).Value);
        _builtinOps[(Int, "|")] = (a, b) => new RuntimeValue.IntVal(((RuntimeValue.IntVal)a).Value | ((RuntimeValue.IntVal)b).Value);
        _builtinOps[(Int, "^")] = (a, b) => new RuntimeValue.IntVal(((RuntimeValue.IntVal)a).Value ^ ((RuntimeValue.IntVal)b).Value);
    }

    static double AsDouble(RuntimeValue v) => v switch {
        RuntimeValue.IntVal i => i.Value,
        RuntimeValue.FloatVal f => f.Value,
        RuntimeValue.BigIntVal bi => (double)bi.Value,
        RuntimeValue.FractionVal fr => (double)fr.Num / fr.Den,
        RuntimeValue.BigFractionVal bf => (double)bf.Num / (double)bf.Den,
        _ => throw new RuntimeException("expected numeric")
    };
    static float AsFloat(RuntimeValue v) => v switch {
        RuntimeValue.IntVal i => i.Value,
        RuntimeValue.FloatVal f => (float)f.Value,
        _ => throw new RuntimeException("expected numeric")
    };
    static System.Numerics.BigInteger AsBigInt(RuntimeValue v) => v switch {
        RuntimeValue.IntVal i => i.Value,
        RuntimeValue.BigIntVal bi => bi.Value,
        _ => throw new RuntimeException("expected bigint")
    };
    static RuntimeValue FractionBinOp(RuntimeValue a, RuntimeValue b, Func<int,int,int,int,RuntimeValue> f){
        int na=a is RuntimeValue.FractionVal fa?fa.Num:((RuntimeValue.IntVal)a).Value;
        int da=a is RuntimeValue.FractionVal fa2?fa2.Den:1;
        int nb=b is RuntimeValue.FractionVal fb?fb.Num:((RuntimeValue.IntVal)b).Value;
        int db=b is RuntimeValue.FractionVal fb2?fb2.Den:1;
        return f(na,da,nb,db);
    }
    static RuntimeValue BigFractionBinOp(RuntimeValue a, RuntimeValue b, Func<System.Numerics.BigInteger,System.Numerics.BigInteger,System.Numerics.BigInteger,System.Numerics.BigInteger,RuntimeValue> f){
        var na=a is RuntimeValue.BigFractionVal bfa?bfa.Num:a is RuntimeValue.BigIntVal bia?bia.Value:((RuntimeValue.IntVal)a).Value;
        var da=a is RuntimeValue.BigFractionVal bfa2?bfa2.Den:1;
        var nb=b is RuntimeValue.BigFractionVal bfb?bfb.Num:b is RuntimeValue.BigIntVal bib?bib.Value:((RuntimeValue.IntVal)b).Value;
        var db=b is RuntimeValue.BigFractionVal bfb2?bfb2.Den:1;
        return f(na,da,nb,db);
    }

    /// <summary>沿继承链查找内置运算符（不含自定义类 Operators）</summary>
    internal static Func<RuntimeValue, RuntimeValue, RuntimeValue>? GetBuiltinOperator(RuntimeType type, string op)
    {
        var current = type;
        while (true)
        {
            if (_builtinOps.TryGetValue((current, op), out var fn)) return fn;
            if (current.Parent == current) break;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>注册方法（在该类型上）</summary>
    public void DefineMethod(string name, Func<RuntimeValue, RuntimeValue[], RuntimeValue> impl)
    {
        _methods[name] = impl;
    }

    /// <summary>沿继承链查找方法</summary>
    public Func<RuntimeValue, RuntimeValue[], RuntimeValue> LookupMethod(string name)
    {
        var current = this;
        while (true)
        {
            if (current._methods.TryGetValue(name, out var m))
                return m;
            if (current.Parent == current) break; // object reached
            current = current.Parent;
        }
        throw new RuntimeException($"Type '{Name}' has no method '{name}'");
    }

    // ============================================================
    //  类型检查
    // ============================================================

    /// <summary>this 是否兼容 target？即 this &lt;: target</summary>
    public bool IsAssignableTo(RuntimeType target)
    {
        if (this == Every) return true;
        if (target == Any) return true;
        var current = this;
        while (true)
        {
            if (current == target) return true;
            if (current.Parent == current) return false; // object reached
            current = current.Parent;
        }
    }

    // ============================================================
    //  用户自定义类型
    // ============================================================

    /// <summary>运行时创建新类型（class Foo : Parent { ... } 执行时调用）</summary>
    public static RuntimeType Define(string name, RuntimeType? parent = null)
    {
        return new RuntimeType(name, parent ?? Object);
    }

    /// <summary>重建原型链：从父 Proto push，跑 block 后存入 Proto</summary>
    public void Rebuild(Scope blockResult)
    {
        Proto = blockResult;
    }

    public override string ToString() => Name;
}
