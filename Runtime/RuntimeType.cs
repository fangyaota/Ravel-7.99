namespace Ravel.Runtime;

using System.Linq;

/// <summary>
/// 运行时类型描述符。类型本身是 object 的值，类型的类型是 type。
/// </summary>
public class RuntimeType
{
    public string Name { get; set; }
    public RuntimeType Parent { get; private set; }

    /// <summary>方法表：方法名 → 实现（self + args → result）</summary>
    private readonly Dictionary<string, Func<RuntimeValue, RuntimeValue[], RuntimeValue>> _methods = [];
    public IEnumerable<string> MethodNames => _methods.Keys;
    /// <summary>类型构造器/转换器——调用该类型时执行（如 int(x)）</summary>
    internal FunctionVal? Initializer { get; set; }
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
    public static readonly RuntimeType Block;
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

    /// <summary>所有已注册类型（内置 + 用户定义）</summary>
    internal static readonly List<RuntimeType> AllTypes = [];

    // 静态初始化
    static RuntimeType()
    {
        // 先创建 object（自引用）
        Object = new RuntimeType("Object", null);
        Object.Parent = Object;

        // 分支
        ValueType = new RuntimeType("ValueType", Object);
        Class = new RuntimeType("Class", Object);

        // 值类型
        Int = new RuntimeType("Integer", ValueType);
        Float = new RuntimeType("Float", ValueType);
        Bool = new RuntimeType("Bool", ValueType);
        String = new RuntimeType("String", ValueType);
        BigInt = new RuntimeType("BigInt", ValueType);
        Fraction = new RuntimeType("Fraction", ValueType);
        BigFraction = new RuntimeType("BigFraction", ValueType);

        // 引用类型
        Function = new RuntimeType("Function", Object);
        Block = new RuntimeType("Block", Function);
        List = new RuntimeType("List", Object);
        Set = new RuntimeType("Set", Object);
        Dict = new RuntimeType("Dict", Object);

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
        // 底类型 — default 的类型，自引用（无父）
        Every = new RuntimeType("Every", null);
        Every.Parent = Every;
        // 顶类型 — 任何类型都可赋值给它，Any 类型变量跳过类型检查，自引用（无父）
        Any = new RuntimeType("Any", null);
        Any.Parent = Any;

        // ---- 注册内置运算符（作为方法） ----
        RegisterOperators();
        // ---- 注册内置方法 ----
        RegisterMethods();
        // ---- 注册类型构造器 ----
        RegisterInitializers();
    }

    /// <summary>把二元运算符注册为名字是符号的方法（op 如 "+"、"=="）</summary>
    private static void DefineOp(RuntimeType type, string op, Func<RuntimeValue, RuntimeValue, RuntimeValue> impl)
        => type.DefineMethod(op, (self, args) => impl(self, args[0]));

    private static void RegisterOperators()
    {
        // int 运算符
        DefineOp(Int, "+", (a, b) =>
        {
            if (b is FloatVal fb) return new FloatVal(((IntVal)a).Value + fb.Value);
            return new IntVal(((IntVal)a).Value + ((IntVal)b).Value);
        });
        DefineOp(Int, "-", (a, b) =>
        {
            if (b is FloatVal fb) return new FloatVal(((IntVal)a).Value - fb.Value);
            return new IntVal(((IntVal)a).Value - ((IntVal)b).Value);
        });
        DefineOp(Int, "*", (a, b) =>
        {
            if (b is FloatVal fb) return new FloatVal(((IntVal)a).Value * fb.Value);
            return new IntVal(((IntVal)a).Value * ((IntVal)b).Value);
        });
        DefineOp(Int, "/", (a, b) =>
        {
            if (b is FloatVal fb) return new FloatVal(((IntVal)a).Value / fb.Value);
            return new IntVal(((IntVal)a).Value / ((IntVal)b).Value);
        });
        DefineOp(Int, "%", (a, b) => new IntVal(((IntVal)a).Value % ((IntVal)b).Value));

        // float 运算符
        DefineOp(Float, "+", (a, b) => new FloatVal(AsFloat(a) + AsFloat(b)));
        DefineOp(Float, "-", (a, b) => new FloatVal(AsFloat(a) - AsFloat(b)));
        DefineOp(Float, "*", (a, b) => new FloatVal(AsFloat(a) * AsFloat(b)));
        DefineOp(Float, "/", (a, b) => new FloatVal(AsFloat(a) / AsFloat(b)));
        DefineOp(Float, "%", (a, b) => new FloatVal(AsFloat(a) % AsFloat(b)));

        // BigInt 运算符
        DefineOp(BigInt, "+", (a, b) => new BigIntVal(AsBigInt(a) + AsBigInt(b)));
        DefineOp(BigInt, "-", (a, b) => new BigIntVal(AsBigInt(a) - AsBigInt(b)));
        DefineOp(BigInt, "*", (a, b) => new BigIntVal(AsBigInt(a) * AsBigInt(b)));
        DefineOp(BigInt, "/", (a, b) => new BigIntVal(AsBigInt(a) / AsBigInt(b)));
        DefineOp(BigInt, "%", (a, b) => new BigIntVal(AsBigInt(a) % AsBigInt(b)));

        // Fraction 运算符
        DefineOp(Fraction, "+", (a, b) => FractionBinOp(a, b, (na, da, nb, db) => new FractionVal(na * db + nb * da, da * db)));
        DefineOp(Fraction, "-", (a, b) => FractionBinOp(a, b, (na, da, nb, db) => new FractionVal(na * db - nb * da, da * db)));
        DefineOp(Fraction, "*", (a, b) => FractionBinOp(a, b, (na, da, nb, db) => new FractionVal(na * nb, da * db)));
        DefineOp(Fraction, "/", (a, b) => FractionBinOp(a, b, (na, da, nb, db) => new FractionVal(na * db, da * nb)));

        // BigFraction 运算符
        DefineOp(BigFraction, "+", (a, b) => BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * db + nb * da, da * db)));
        DefineOp(BigFraction, "-", (a, b) => BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * db - nb * da, da * db)));
        DefineOp(BigFraction, "*", (a, b) => BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * nb, da * db)));
        DefineOp(BigFraction, "/", (a, b) => BigFractionBinOp(a, b, (na, da, nb, db) => new BigFractionVal(na * db, da * nb)));

        // 比较运算符 — 数字
        foreach (var t in new[] { Int, Float, BigInt, Fraction, BigFraction })
        {
            DefineOp(t, "==", (a, b) => new BoolVal(AsDouble(a) == AsDouble(b)));
            DefineOp(t, "!=", (a, b) => new BoolVal(AsDouble(a) != AsDouble(b)));
            DefineOp(t, "<", (a, b) => new BoolVal(AsDouble(a) < AsDouble(b)));
            DefineOp(t, ">", (a, b) => new BoolVal(AsDouble(a) > AsDouble(b)));
            DefineOp(t, "<=", (a, b) => new BoolVal(AsDouble(a) <= AsDouble(b)));
            DefineOp(t, ">=", (a, b) => new BoolVal(AsDouble(a) >= AsDouble(b)));
        }
        // bool 比较
        DefineOp(Bool, "==", (a, b) => new BoolVal(((BoolVal)a).Value == ((BoolVal)b).Value));
        DefineOp(Bool, "!=", (a, b) => new BoolVal(((BoolVal)a).Value != ((BoolVal)b).Value));
        // string 比较
        DefineOp(String, "==", (a, b) => new BoolVal(((StringVal)a).Value == ((StringVal)b).Value));
        DefineOp(String, "!=", (a, b) => new BoolVal(((StringVal)a).Value != ((StringVal)b).Value));
        // string 拼接
        DefineOp(String, "+", (a, b) => new StringVal(((StringVal)a).Value + ((StringVal)b).Value));
        DefineOp(Type, "==", (a, b) => new BoolVal(((TypeVal)a).Value == ((TypeVal)b).Value));
        DefineOp(Type, "!=", (a, b) => new BoolVal(((TypeVal)a).Value != ((TypeVal)b).Value));

        // bool 逻辑运算符
        DefineOp(Bool, "&", (a, b) => new BoolVal(((BoolVal)a).Value && ((BoolVal)b).Value));
        DefineOp(Bool, "|", (a, b) => new BoolVal(((BoolVal)a).Value || ((BoolVal)b).Value));
        DefineOp(Bool, "^", (a, b) => new BoolVal(((BoolVal)a).Value ^ ((BoolVal)b).Value));
        // int 位运算符
        DefineOp(Int, "&", (a, b) => new IntVal(((IntVal)a).Value & ((IntVal)b).Value));
        DefineOp(Int, "|", (a, b) => new IntVal(((IntVal)a).Value | ((IntVal)b).Value));
        DefineOp(Int, "^", (a, b) => new IntVal(((IntVal)a).Value ^ ((IntVal)b).Value));

        // 函数交替 |（左失败则右）
        DefineOp(Function, "|", (a, b) =>
        {
            var lf = (FunctionVal)a;
            var rf = (FunctionVal)b;
            return FunctionVal.FromTrampolined(ia => Interpreter.OrElse(lf.Trampolined(ia), _ => rf.Trampolined(ia)));
        });
    }

    /// <summary>注册各内置类型的方法</summary>
    private static void RegisterMethods()
    {
        Object.DefineMethod("ToString", (s, _) => new StringVal(s.ToString()));
        Object.DefineMethod("Copy", (s, _) =>
        {
            switch (s)
            {
                case IntVal v: return new IntVal(v.Value);
                case FloatVal v: return new FloatVal(v.Value);
                case BoolVal v: return new BoolVal(v.Value);
                case StringVal v: return new StringVal(v.Value);
                case BigIntVal v: return new BigIntVal(v.Value);
                case FractionVal v: return new FractionVal(v.Num, v.Den);
                case BigFractionVal v: return new BigFractionVal(v.Num, v.Den);
                case ListVal v: return new ListVal([.. v.Elements]);
                case SetVal v: return new SetVal([.. v.Elements]);
                case DictVal v: return new DictVal(new Dictionary<string, RuntimeValue>(v.Entries));
                case ObjectVal v:
                {
                    return new ObjectVal(v.ClassType, CopyScope(v.Scope), v.Parent);
                }
                default: return s;
            }
        });
        Object.DefineMethod("Fields", (s, _) =>
        {
            var all = new List<RuntimeValue>();
            var seen = new HashSet<string>();
            // 加类型方法（含继承链）
            var t = s.Type;
            while (true)
            {
                foreach (var n in t.MethodNames)
                    if (seen.Add(n))
                        all.Add(new StringVal(n));
                if (t == t.Parent) break;
                t = t.Parent;
            }

            return new ListVal(all);
        });
        Int.DefineMethod("ToString", (s, _) => new StringVal(((IntVal)s).Value.ToString()));
        String.DefineMethod("Length", (s, _) => new IntVal(((StringVal)s).Value.Length));
        List.DefineMethod("Count", (s, _) => new IntVal(((ListVal)s).Elements.Count));
        List.DefineMethod("At", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.At 需要 int 参数");
            var lst = (ListVal)s;
            if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return lst.Elements[i.Value];
        });
        List.DefineMethod("Add", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("list.Add 需要 1 个参数");
            ((ListVal)s).Elements.Add(a[0]);
            return VoidVal.Instance;
        });
        List.DefineMethod("Remove", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.Remove 需要 int 参数");
            var lst = (ListVal)s;
            if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            var v = lst.Elements[i.Value];
            lst.Elements.RemoveAt(i.Value);
            return v;
        });
        List.DefineMethod("Insert", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.Insert 需要 int 参数");
            var lst = (ListVal)s;
            var idx = i.Value;
            if (idx < 0 || idx > lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("list.Insert 需要值参数");
                lst.Elements.Insert(idx, va[0]);
                return VoidVal.Instance;
            });
        });
        List.DefineMethod("Set", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.Set 需要 int 参数");
            var lst = (ListVal)s;
            var idx = i.Value;
            if (idx < 0 || idx >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("list.Set 需要值参数");
                lst.Elements[idx] = va[0];
                return VoidVal.Instance;
            });
        });

        // ---- Set 方法 ----
        Set.DefineMethod("Count", (s, _) => new IntVal(((SetVal)s).Elements.Count));
        Set.DefineMethod("Add", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Add 需要 1 个参数");
            ((SetVal)s).Elements.Add(a[0]);
            return VoidVal.Instance;
        });
        Set.DefineMethod("Remove", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Remove 需要 1 个参数");
            ((SetVal)s).Elements.Remove(a[0]);
            return VoidVal.Instance;
        });
        Set.DefineMethod("Contains", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Contains 需要 1 个参数");
            return new BoolVal(((SetVal)s).Elements.Contains(a[0]));
        });

        // ---- Dict 方法 ----
        Dict.DefineMethod("Count", (s, _) => new IntVal(((DictVal)s).Entries.Count));
        Dict.DefineMethod("Get", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal key) throw new RuntimeException("dict.Get 需要 string 参数");
            var d = (DictVal)s;
            if (d.Entries.TryGetValue(key.Value, out var v)) return v;
            throw new RuntimeException($"键不存在: {key.Value}");
        });
        Dict.DefineMethod("Set", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal key) throw new RuntimeException("dict.Set 需要 string 键");
            return FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("dict.Set 需要值参数");
                ((DictVal)s).Entries[key.Value] = va[0];
                return VoidVal.Instance;
            });
        });
        Dict.DefineMethod("Has", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal key) throw new RuntimeException("dict.Has 需要 string 参数");
            return new BoolVal(((DictVal)s).Entries.ContainsKey(key.Value));
        });
        Dict.DefineMethod("Keys", (s, _) =>
        {
            var keys = new List<RuntimeValue>();
            foreach (var k in ((DictVal)s).Entries.Keys)
                keys.Add(new StringVal(k));
            return new ListVal(keys);
        });
        Dict.DefineMethod("Values", (s, _) =>
        {
            var vals = new List<RuntimeValue>();
            foreach (var v in ((DictVal)s).Entries.Values)
                vals.Add(v);
            return new ListVal(vals);
        });

        // type 类型方法
        Type.DefineMethod("name", (s, _) => new StringVal(((TypeVal)s).Value.Name));
        Type.DefineMethod("Parent", (s, _) =>
        {
            var p = ((TypeVal)s).Value.Parent;
            if (p == ((TypeVal)s).Value) return s;
            return new TypeVal(p);
        });
        Type.DefineMethod("Is", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not TypeVal other) throw new RuntimeException("type.Is 需要 type 参数");
            return new BoolVal(((TypeVal)s).Value.IsAssignableTo(other.Value));
        });
        Type.DefineMethod("Default", (s, _) =>
        {
            var tv = (TypeVal)s;
            return ConvertDirect(tv.Value, DefaultVal.Instance);
        });
        Function.DefineMethod("Name", (s, a) =>
        {
            var fn = (FunctionVal)s;
            if (a.Length > 0 && a[0] is StringVal sv)
            {
                fn.Name = sv.Value;
                return VoidVal.Instance;
            }

            return fn.Name != null ? new StringVal(fn.Name) : VoidVal.Instance;
        });
        Type.DefineMethod("Subtypes", (s, _) =>
        {
            var tv = (TypeVal)s;
            var subs = new List<RuntimeValue>();
            foreach (var t in AllTypes)
            {
                if (t != tv.Value && t.IsAssignableTo(tv.Value))
                    subs.Add(new TypeVal(t));
            }

            return new ListVal(subs);
        });

        // ---- Scope 方法 ----
        ScopeType.DefineMethod("Push", (s, _) =>
        {
            var scope = ((ScopeVal)s).Scope;
            return new ScopeVal(scope.Push());
        });
        ScopeType.DefineMethod("Define", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal name)
                throw new RuntimeException("scope.Define 需要字符串名称");
            var scope = ((ScopeVal)s).Scope;
            return FunctionVal.FromDirect(b =>
            {
                if (b.Length != 1 || b[0] is not TypeVal tv)
                    throw new RuntimeException("scope.Define 需要 type 参数");
                scope.Define(name.Value, tv.Value, VoidVal.Instance);
                return VoidVal.Instance;
            });
        });
        ScopeType.DefineMethod("Lookup", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal name)
                throw new RuntimeException("scope.Lookup 需要字符串参数");
            var scope = ((ScopeVal)s).Scope;
            var vr = scope.Lookup(name.Value);
            return new PropertyVal(
                FunctionVal.FromDirect(_ => vr.Value),
                FunctionVal.FromDirect(args =>
                {
                    vr.Assign(args[0]);
                    return VoidVal.Instance;
                }),
                [.. vr.Attrs]
            );
        });
        ScopeType.DefineMethod("Variables", (s, _) =>
        {
            var scope = ((ScopeVal)s).Scope;
            var d = new Dictionary<string, RuntimeValue>();
            foreach (var kv in scope.Variables)
            {
                if (kv.Key == "this" || kv.Key == "base" || kv.Key == "block" || kv.Key == "thistype") continue;
                var vr = scope.Lookup(kv.Key);
                var getter = FunctionVal.FromDirect(_ => vr.Value);
                var setter = FunctionVal.FromDirect(a =>
                {
                    vr.Assign(a[0]);
                    return VoidVal.Instance;
                });
                d[kv.Key] = new PropertyVal(getter, setter, [.. kv.Value.Attrs]);
            }

            return new DictVal(d);
        });

        // ---- Property 方法 ----
        Property.DefineMethod("Attrs", (s, _) =>
        {
            var pv = (PropertyVal)s;
            return pv.Attrs != null
                ? new ListVal([.. pv.Attrs.Select(a => new StringVal(a))])
                : new ListVal([]);
        });

        // ---- Function 方法 ----
        Function.DefineMethod("scope", (s, _) =>
        {
            var fn = (FunctionVal)s;
            return new ScopeVal(fn.Scope ?? new Scope());
        });
        Function.DefineMethod("setScope", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not ScopeVal sv) throw new RuntimeException("setScope 需要 Scope 参数");
            ((FunctionVal)s).Scope = sv.Scope;
            return VoidVal.Instance;
        });
        Function.DefineMethod("prepend", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not BlockVal p) throw new RuntimeException("prepend 需要代码块参数");
            return ((FunctionVal)s).Prepend(p);
        });
        Function.DefineMethod("append", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not BlockVal p) throw new RuntimeException("append 需要代码块参数");
            return ((FunctionVal)s).Append(p);
        });

        // ---- Type 方法 ----
        Type.DefineMethod("Instantiate", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not ScopeVal sv) throw new RuntimeException("Instantiate 需要 Scope 参数");
            var rt = ((TypeVal)s).Value;
            return new ObjectVal(rt, sv.Scope, null);
        });
    }

    /// <summary>浅拷贝作用域（with / Copy 用）</summary>
    internal static Scope CopyScope(Scope src)
    {
        var dst = new Scope(src.Parent);
        foreach (var kv in src.Variables)
            dst.Define(kv.Key, kv.Value.TypeConstraint, kv.Value.Value);
        return dst;
    }

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

    private static double AsDouble(RuntimeValue v) => v switch
    {
        IntVal i => i.Value,
        FloatVal f => f.Value,
        BigIntVal bi => (double)bi.Value,
        FractionVal fr => (double)fr.Num / fr.Den,
        BigFractionVal bf => (double)bf.Num / (double)bf.Den,
        _ => throw new RuntimeException("需要数值类型")
    };
    private static float AsFloat(RuntimeValue v) => v switch
    {
        IntVal i => i.Value,
        FloatVal f => (float)f.Value,
        _ => throw new RuntimeException("需要数值类型")
    };
    private static System.Numerics.BigInteger AsBigInt(RuntimeValue v) => v switch
    {
        IntVal i => i.Value,
        BigIntVal bi => bi.Value,
        _ => throw new RuntimeException("需要 bigint 类型")
    };
    private static RuntimeValue FractionBinOp(RuntimeValue a, RuntimeValue b, Func<int, int, int, int, RuntimeValue> f)
    {
        int na = a is FractionVal fa ? fa.Num : ((IntVal)a).Value;
        int da = a is FractionVal fa2 ? fa2.Den : 1;
        int nb = b is FractionVal fb ? fb.Num : ((IntVal)b).Value;
        int db = b is FractionVal fb2 ? fb2.Den : 1;
        return f(na, da, nb, db);
    }
    private static RuntimeValue BigFractionBinOp(RuntimeValue a, RuntimeValue b, Func<System.Numerics.BigInteger, System.Numerics.BigInteger, System.Numerics.BigInteger, System.Numerics.BigInteger, RuntimeValue> f)
    {
        var na = a is BigFractionVal bfa ? bfa.Num : a is BigIntVal bia ? bia.Value : ((IntVal)a).Value;
        var da = a is BigFractionVal bfa2 ? bfa2.Den : 1;
        var nb = b is BigFractionVal bfb ? bfb.Num : b is BigIntVal bib ? bib.Value : ((IntVal)b).Value;
        var db = b is BigFractionVal bfb2 ? bfb2.Den : 1;
        return f(na, da, nb, db);
    }

    /// <summary>注册方法（在该类型上）</summary>
    public void DefineMethod(string name, Func<RuntimeValue, RuntimeValue[], RuntimeValue> impl)
    {
        _methods[name] = impl;
    }

    /// <summary>沿继承链查找方法，找不到返回 null</summary>
    public Func<RuntimeValue, RuntimeValue[], RuntimeValue>? TryLookupMethod(string name)
    {
        var current = this;
        while (true)
        {
            if (current._methods.TryGetValue(name, out var m))
                return m;
            if (current.Parent == current) break; // object reached
            current = current.Parent;
        }
        return null;
    }

    /// <summary>沿继承链查找方法，找不到抛异常</summary>
    public Func<RuntimeValue, RuntimeValue[], RuntimeValue> LookupMethod(string name)
        => TryLookupMethod(name) ?? throw new RuntimeException($"类型 '{Name}' 没有方法 '{name}'");

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

    public override string ToString() => Name;
}
