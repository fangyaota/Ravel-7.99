namespace Ravel.Runtime;

/// <summary>内置类对象。
///
/// **每个类型都是一个 <see cref="ObjectVal"/>** —— 和用户写的类用的是同一种值。
/// 类性靠 Scope 里的成员表达：`parent`（父类对象）+ `block`（类体）。
/// 内置类没有用户写的类体，所以 S1 阶段它们的成员靠 <see cref="ObjectVal.DefineMethod"/>
/// 直接注册进 Scope（S2 会改成"预设类体里定义"，那时"内置类也有类体"就名副其实了）。
///
/// 静态字段名沿用旧 `RuntimeType` 的名字（`Int`/`Float`/…），三个注册文件因此可以原样搬运。
///
/// 建树分两趟：第一趟把所有类对象建出来（`ClassType` 先自指），第二趟回填元类并挂
/// `parent`/`name` —— 因为建 `Object` 的时候 `Type` 还不存在，而 `Object` 的元类是 `Type`。</summary>
internal static partial class BuiltinClasses
{
    // 顶类型
    public static readonly ObjectVal Object;

    // 值类型分支（不可变）
    public static readonly ObjectVal ValueType;
    public static readonly ObjectVal Int;
    public static readonly ObjectVal Float;
    public static readonly ObjectVal Bool;
    public static readonly ObjectVal String;
    public static readonly ObjectVal BigInt;
    public static readonly ObjectVal Fraction;
    public static readonly ObjectVal BigFraction;

    // 引用类型分支（可变/有行为）
    public static readonly ObjectVal Function;
    public static readonly ObjectVal Block;
    public static readonly ObjectVal List;
    public static readonly ObjectVal Set;
    public static readonly ObjectVal Dict;

    // void — 唯一值 ()
    public static readonly ObjectVal Void;

    /// <summary>元类型 —— 所有类型的类型，也是"建类"这个动作本身（`class` 是它的别名）。
    /// 它的元类是**它自己**（自指，元类链的起点）。用户类直接挂在它下面。</summary>
    public static readonly ObjectVal Type;

    // 底类型 — 为所有类的子类，default 是其唯一实例；不在继承树里，IsAssignableTo 全局特判
    public static readonly ObjectVal Every;

    // 顶类型 — 任何类型都可赋值给它
    public static readonly ObjectVal Any;

    public static readonly ObjectVal Exception;
    public static readonly ObjectVal Ravel;
    public static readonly ObjectVal ScopeType;
    public static readonly ObjectVal Property;

    /// <summary>`call` 成员的值——**新建实例的责任**。所有类对象装的都是它，只是绑的 self 不同：
    /// `C ()` 拿 self=C 造 C 的实例；`type { body }` 拿 self=type，而它的实例就是类。
    ///
    /// 这是"可调用"的唯一判据（见 `ObjectVal.HasCall`），于是引擎不必知道"什么是类"。
    /// 也正因为它是普通成员，用户/未来的 interface 可以自己定义 `call` 来造可调用的东西。</summary>
    internal static readonly FunctionVal Call = FunctionVal.From(self => new BoundCall((ObjectVal)self));

    /// <summary>所有已注册的类对象（内置 + 用户定义），供 Subtypes 反射</summary>
    internal static readonly List<ObjectVal> AllTypes = [];

    /// <summary>AllTypes 里内置类占多少——静态构造器填完后定下来，Reset 时按它切</summary>
    private static int _builtinCount;

    static BuiltinClasses()
    {
        // ---- 第一趟：建出所有类对象，ClassType 先自指 ----
        Object = New("Object");
        Function = New("Function");
        ValueType = New("ValueType");
        Int = New("Integer");           // 名字是 Integer，字段名沿用旧名 Int
        Float = New("Float");
        Bool = New("Bool");
        String = New("String");
        BigInt = New("BigInt");
        Fraction = New("Fraction");
        BigFraction = New("BigFraction");
        Block = New("Block");
        List = New("List");
        Set = New("Set");
        Dict = New("Dict");
        Void = New("Void");
        Type = New("Type");
        Ravel = New("Ravel");
        ScopeType = New("Scope");
        Property = New("Property");
        Exception = New("Exception");
        Every = New("Every");
        Any = New("Any");

        // ---- 第二趟：挂 parent 链 + 回填元类 ----
        // Function 必须早于 Bool / Block（它们的父类）
        Link(Object, Object, Type);         // parent 自引用(链到头);元类是 type
        Link(Function, Object, Type);
        Link(ValueType, Object, Type);

        // 值类型 —— Bool 是函数:true/false 可调用,收两个块返回选中那个的结果(lisp 式)
        Link(Int, ValueType, Type);
        Link(Float, ValueType, Type);
        Link(Bool, Function, Type);
        Link(String, ValueType, Type);
        Link(BigInt, ValueType, Type);
        Link(Fraction, ValueType, Type);
        Link(BigFraction, ValueType, Type);

        // 引用类型
        Link(Block, Function, Type);
        Link(List, Object, Type);
        Link(Set, Object, Type);
        Link(Dict, Object, Type);

        Link(Void, Object, Type);

        // 元类型 —— 元类是它自己
        Link(Type, Function, Type);

        Link(Ravel, Object, Type);
        Link(ScopeType, Object, Type);
        Link(Property, Object, Type);
        Link(Exception, Object, Type);
        // 底类型/顶类型：parent 自引用（链到自己就停）
        Link(Every, Every, Type);
        Link(Any, Any, Type);

        // ---- 注册内置运算符（作为方法） ----
        RegisterOperators();
        // ---- 注册内置方法 ----
        RegisterMethods();
        // ---- 注册类型构造器 ----
        RegisterInitializers();

        // ---- 收集所有内置类（供 Subtypes 反射） ----
        foreach (var t in new[]
                 {
                     Object, ValueType, Int, Float, Bool, String, BigInt,
                     Fraction, BigFraction, Function, Block,
                     List, Set, Dict, Void, Type,
                     Ravel, Any, Every, Exception, ScopeType, Property
                 })
            AllTypes.Add(t);

        _builtinCount = AllTypes.Count;
    }

    private static ObjectVal New(string name)
    {
        var t = new ObjectVal(null, new Scope());   // null → ClassType 先自指
        t.Scope.Define(ObjectVal.NameMember, null!, new StringVal(name));
        return t;
    }

    /// <summary>第二趟：挂 parent、回填元类。约束这时才给得上（String/Object 已经存在）。</summary>
    private static void Link(ObjectVal t, ObjectVal parent, ObjectVal meta)
    {
        t.ClassType = meta;
        t.Scope.DefineOrReplace(ObjectVal.ParentMember, Object, parent);
        t.Scope.DefineOrReplace(ObjectVal.CallMember, Function, Call);
        t.Scope.LookupField(ObjectVal.NameMember)!.TypeConstraint = String;
    }

    // ============================================================
    //  用户自定义类
    // ============================================================

    /// <summary>建一个用户类对象（`class Parent { ... }` 执行时调用）。
    /// 登记进 AllTypes，否则 `Subtypes ()` 反射看不到用户类（只列内置类）。
    ///
    /// `C := class {...}` 建的类**没有名字**（只有 `::=` 会命名）——由 `ObjectVal.Name` 的空串表示。</summary>
    internal static ObjectVal CreateClass(ObjectVal parent, BlockVal block)
    {
        var t = new ObjectVal(Type, new Scope());
        t.Scope.Define(ObjectVal.ParentMember, Object, parent);
        t.Scope.Define(ObjectVal.BlockMember, Block, block);
        t.Scope.Define(ObjectVal.NameMember, String, new StringVal(""));
        t.Scope.Define(ObjectVal.CallMember, Function, Call);   // "可调用"的凭据
        AllTypes.Add(t);

        // 类体里用符号定义的运算符(`+ := f` 定义、`+ = f` 覆盖)注册到类的方法表
        foreach (var stmt in block.Block.Statements)
        {
            var op = stmt switch
            {
                VarDefinition v when v.IsOperator => v.Name,
                Assignment a when OperatorSymbols.IsSymbol(a.Name) => a.Name,
                _ => null,
            };
            if (op != null) t.DefineClassOperator(op);
        }

        return t;
    }

    /// <summary>建一个模块的类对象（`ravel "M"` / System 模块用）。模块也是类型，
    /// 但它的成员住在 <see cref="ModuleVal.ModuleScope"/> 里而不是这类对象自己的 Scope ——
    /// 所以不登记进 AllTypes（每个 Interpreter 都重建一份，登记只会累积）。</summary>
    internal static ObjectVal NewModuleClass(string name, ObjectVal parent)
    {
        var t = new ObjectVal(Type, new Scope());
        t.Scope.Define(ObjectVal.ParentMember, Object, parent);
        t.Scope.Define(ObjectVal.NameMember, String, new StringVal(name));
        return t;
    }

    /// <summary>清掉已登记的用户类（内置的留着，见 `_builtinCount`）。
    /// `Subtypes` 依赖这张表，而一个进程里可能跑好几个 Interpreter（测试每个文件一个、
    /// REPL 反复 new），不清的话上一个建过的类会出现在下一个的 `Subtypes ()` 里。</summary>
    internal static void ResetUserTypes()
        => AllTypes.RemoveRange(_builtinCount, AllTypes.Count - _builtinCount);

    /// <summary>沿 parent 链收集各层类体，返回「顶祖先 → 自身」。没有类体的层（内建）不入列且到此为止。
    /// 类体创建后不可变，所以这是纯函数，可随帧推进反复调用。</summary>
    internal static List<BlockVal> CollectBodies(ObjectVal type)
    {
        var layers = new List<BlockVal>();
        for (var t = type; ; t = t.Parent)
        {
            if (t == null) break;
            var body = t.Body;
            if (body == null) break;
            layers.Add(body);
            if (t.Parent == t) break;
        }

        layers.Reverse();
        return layers;
    }
}
