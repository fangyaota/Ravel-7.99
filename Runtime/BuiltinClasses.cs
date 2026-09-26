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
    public static readonly ClassVal Object;

    // 值类型分支（不可变）
    public static readonly ClassVal ValueType;
    public static readonly ClassVal Int;
    public static readonly ClassVal Float;
    public static readonly ClassVal Bool;
    public static readonly ClassVal String;
    public static readonly ClassVal BigInt;
    public static readonly ClassVal Fraction;
    public static readonly ClassVal BigFraction;

    // 引用类型分支（可变/有行为）
    public static readonly ClassVal Function;
    public static readonly ClassVal Block;
    public static readonly ClassVal List;
    public static readonly ClassVal Set;
    public static readonly ClassVal Dict;

    // void — 唯一值 ()
    public static readonly ClassVal Void;

    /// <summary>元类型 —— 所有类型的类型，也是"建类"这个动作本身（`class` 是它的别名）。
    /// 它的元类是**它自己**（自指，元类链的起点）。用户类直接挂在它下面。</summary>
    public static readonly ClassVal Type;

    // 底类型 — 为所有类的子类，default 是其唯一实例；不在继承树里，IsAssignableTo 全局特判
    public static readonly ClassVal Every;

    // 顶类型 — 任何类型都可赋值给它
    public static readonly ClassVal Any;

    public static readonly ClassVal Exception;
    public static readonly ClassVal Ravel;
    public static readonly ClassVal ScopeType;
    public static readonly ClassVal Property;

    /// <summary>所有已注册的类对象（内置 + 用户定义），供 Subtypes 反射</summary>
    internal static readonly List<ClassVal> AllTypes = [];

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
        // ---- type 的 init:默认的建类逻辑(必须在 Type/Object/Function 都挂好之后) ----
        InstallTypeInit();

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

    // ============================================================
    //  预设类体 —— 内置类的"类体"
    // ============================================================

    /// <summary>造一段**预设类体**:内置类的类体。里面只定义它的成员。
    ///
    /// 内置类的成员是 C# 造好的值(转换器、默认建类函数),写不出 Ravel 源码,
    /// 所以用 <see cref="LiteralExpr"/> 直接塞进去。这样内置类和用户类走**完全相同**的
    /// 实例化路径:跑各层类体 → 在实例作用域里找 `init` → 调它、交出它的返回值。
    /// S1 那条 `Initializer` vs `Body` 的分叉到这里就没了。
    ///
    /// 捕获作用域给一个空 scope:体里只有 `init := <字面量值>`,不需要解析任何名字。</summary>
    private static BlockVal PresetBody(params (string Name, RuntimeValue Value)[] members)
    {
        var stmts = new List<Statement>();
        foreach (var (name, value) in members)
            stmts.Add(new VarDefinition(name, null, new LiteralExpr(value)) { Line = 1, Column = 1 });
        return new BlockVal(new BlockExpr(stmts) { Line = 1, Column = 1, Source = "<preset>" }, new Scope());
    }

    /// <summary>「没有体」时交出去的那个**空块** —— 类型恒定,别拿 `()` 顶替。
    /// 见 `Function.body`。</summary>
    internal static readonly BlockVal EmptyBody =
        new(new BlockExpr([]) { Line = 1, Column = 1 }, new Scope());

    /// <summary>把两个候选做成 `|` 交替(和 `Function.|` 同一个机制,只是从 C# 侧构造)。
    /// 分流靠 TypeMismatchException —— 第一支的参数类型对不上就试第二支,见 StepAlternate。</summary>
    internal static ControlFunction Alternate(RuntimeValue a, RuntimeValue b)
        => new(ControlKind.Alternate, 1, RList<RuntimeValue>.Empty.Add(a).Add(b));

    /// <summary>`type` 的 init —— **新建实例/新建类这件事的默认逻辑**,也是元类要委托的那一层。
    ///
    /// 两分支(和用户在 Ravel 里写 `(parent: Type body) => ... | (body) => ...` 完全一样):
    /// - 第一支收 `(parent: Type, body)`。参数标成 `Type`,于是 `class { body }` 传块进来时
    ///   类型不匹配、自动落到第二支 —— **交替机制就是靠参数类型分流的**。
    /// - 第二支只收 `body`,parent 默认成 `object`。
    ///
    /// 做的事只有一件:把 (parent, block) 装到**正在构造的那个对象**上,它于是成为一个类。
    /// `this` 从实例作用域拿 —— 这个函数是在 ClassInit 里以实例作用域为调用点被调的。</summary>
    private static void InstallTypeInit()
    {
        var twoArg = new NativeClosure("parent", Type, (scope, parent) =>
            FunctionVal.From(body => Install(scope, (ObjectVal)parent, body)));
        var oneArg = new NativeClosure("body", Function, (scope, body) => Install(scope, Object, body));
        Type.ClassBody = PresetBody(("init", Alternate(twoArg, oneArg)));
    }

    /// <summary>把 (parent, body) 装到 self 上,self 于是是一个类。返回 self ——
    /// 构造交出 init 的返回值,所以这就是"建出来的那个类"。
    ///
    /// 装完顺手登记进 AllTypes(`Subtypes ()` 反射要用)并扫类体里用符号定义的运算符。</summary>
    private static ClassVal Install(Scope scope, ObjectVal parent, RuntimeValue body)
    {
        if (body is not BlockVal blk) throw new RuntimeException("class 需要代码块参数");
        // 正在被装成的这个对象**必然是 ClassVal**:这段只在实例化 `type` 或它的子类时跑,
        // 而 StepClassInit 正是按"被实例化的类 <: type"来决定造 ClassVal 的。
        var self = (ClassVal)scope.Lookup("this").Value;
        self.Scope.DefineOrReplace(ObjectVal.ParentMember, Object, parent);
        self.Scope.DefineOrReplace(ObjectVal.BlockMember, Block, blk);
        self.Scope.Define(ObjectVal.NameMember, String, new StringVal(""));
        AllTypes.Add(self);

        foreach (var stmt in blk.Block.Statements)
        {
            var op = stmt switch
            {
                VarDefinition v when v.IsOperator => v.Name,
                Assignment a when OperatorSymbols.IsSymbol(a.Name) => a.Name,
                _ => null,
            };
            if (op != null) self.DefineClassOperator(op);
        }

        return self;
    }

    private static ClassVal New(string name)
    {
        var t = new ClassVal(null, new Scope());   // null → ClassType 先自指
        t.Scope.Define(ObjectVal.NameMember, null!, new StringVal(name));
        return t;
    }

    /// <summary>第二趟：挂 parent、回填元类。约束这时才给得上（String/Object 已经存在）。</summary>
    private static void Link(ObjectVal t, ObjectVal parent, ClassVal meta)
    {
        t.ClassType = meta;
        t.Scope.DefineOrReplace(ObjectVal.ParentMember, Object, parent);
        t.Scope.LookupField(ObjectVal.NameMember)!.TypeConstraint = String;
    }

    // ============================================================
    //  用户自定义类
    // ============================================================

    /// <summary>建一个模块的类对象（`ravel "M"` / System 模块用）。模块也是类型，
    /// 但它的成员住在 `ModuleVal.ModuleScope` 里而不是这类对象自己的 Scope ——
    /// 所以不登记进 AllTypes（每个 Interpreter 都重建一份，登记只会累积）。</summary>
    internal static ClassVal NewModuleClass(string name, ObjectVal parent)
    {
        var t = new ClassVal(Type, new Scope());
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
            var body = t.ClassBody;
            if (body == null) break;
            layers.Add(body);
            if (t.Parent == t) break;
        }

        layers.Reverse();
        return layers;
    }
}
