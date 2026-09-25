namespace Ravel.Runtime;

/// <summary>
/// 运行时类型描述符。类型本身是 object 的值，类型的类型是 type。
/// </summary>
public partial class RuntimeType
{
    public string Name { get; set; }
    public RuntimeType Parent { get; private set; }

    /// <summary>方法表：方法名 → 方法值（未绑定的 Curried(self, arg) 函数）</summary>
    private readonly Dictionary<string, FunctionVal> _methods = [];

    public IEnumerable<string> MethodNames => _methods.Keys;

    /// <summary>类型构造器/转换器——调用该类型时执行（如 int(x)）</summary>
    internal FunctionVal? Initializer { get; set; }
    /// <summary>元类——创建本类型的构造器对应的类型；null 表示默认 Type</summary>
    internal RuntimeType? Metaclass { get; set; }
    /// <summary>类体——class 创建时的 block，实例化时执行</summary>
    internal BlockVal? Body { get; set; }

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

        // Function 必须早于 Bool / Block（它们的父类）
        Function = new RuntimeType("Function", Object);

        // 分支
        ValueType = new RuntimeType("ValueType", Object);
        Class = new RuntimeType("Class", Object);

        // 值类型 —— Bool 是函数:true/false 可调用,收两个块返回选中那个的结果(lisp 式)
        Int = new RuntimeType("Integer", ValueType);
        Float = new RuntimeType("Float", ValueType);
        Bool = new RuntimeType("Bool", Function);
        String = new RuntimeType("String", ValueType);
        BigInt = new RuntimeType("BigInt", ValueType);
        Fraction = new RuntimeType("Fraction", ValueType);
        BigFraction = new RuntimeType("BigFraction", ValueType);

        // 引用类型
        Block = new RuntimeType("Block", Function);
        List = new RuntimeType("List", Object);
        Set = new RuntimeType("Set", Object);
        Dict = new RuntimeType("Dict", Object);

        // void
        Void = new RuntimeType("Void", Object);

        // 元类型
        Type = new RuntimeType("Type", Function); // type 是函数，可作为构造器
        Class.Parent = Type; // class <: type <: function <: object

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

        // ---- 收集所有内置类型（AllTypes，供 Subtypes 反射） ----
        foreach (var t in new[]
                 {
                     Object, ValueType, Int, Float, Bool, String, BigInt,
                     Fraction, BigFraction, Class, Function, Block,
                     List, Set, Dict, Void, Type,
                     Ravel, Any, Every, Exception, ScopeType, Property
                 })
            AllTypes.Add(t);
    }

    /// <summary>注册内置同步方法（在该类型上）：BuiltinMethodVal 标记，分派走快速同步路径</summary>
    public void DefineMethod(string name, Func<RuntimeValue, RuntimeValue, RuntimeValue> impl)
    {
        _methods[name] = new BuiltinMethodVal(impl);
    }

    /// <summary>注册类运算符：op 为符号（"+"）。存自绑函数——self 绑定得 BoundClassOp，走 CallInto 推 ClassOp 帧</summary>
    public void DefineClassOperator(string op)
    {
        _methods[op] = FunctionVal.From(self => new BoundClassOp((ObjectVal)self, op));
    }

    /// <summary>沿继承链查找方法，找不到返回 null</summary>
    public FunctionVal? TryLookupMethod(string name)
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
    public FunctionVal LookupMethod(string name)
        => TryLookupMethod(name) ?? throw new RuntimeException($"类型 '{Name}' 没有方法 '{name}'");

    /// <summary>绑定方法 self：方法值存为 Curried(self, arg)，绑 self 得等待 arg 的函数</summary>
    public static FunctionVal BindMethod(FunctionVal methodFn, RuntimeValue self)
        => (FunctionVal)methodFn.Body(self);

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
