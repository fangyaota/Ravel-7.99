namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static partial class BuiltinClasses
{
    // 顶类型
    public static readonly ClassVal Object;

    // 值类型分支（不可变）
    public static readonly ClassVal Int;
    public static readonly ClassVal Real;
    public static readonly ClassVal Bool;
    public static readonly ClassVal String;
    public static readonly ClassVal Char;
    public static readonly ClassVal BigInt;
    public static readonly ClassVal Fraction;
    public static readonly ClassVal BigFraction;
    public static readonly ClassVal Range;

    // 引用类型分支（可变/有行为）
    public static readonly ClassVal Function;
    public static readonly ClassVal Block;
    public static readonly ClassVal Continuation;
    public static readonly ClassVal List;
    public static readonly ClassVal Set;
    public static readonly ClassVal Dict;

    // `Pair` —— 值类型的一对(键 / 值)。不进"引用类型"那一支:它不可变、能当键。
    public static readonly ClassVal Pair;

    public static readonly ClassVal Waitable;

    // void — 唯一值 ()
    public static readonly ClassVal Void;

    public static readonly ClassVal Type;

    // 底类型 — 为所有类的子类，default 是其唯一实例；不在继承树里，IsAssignableTo 全局特判

    // 顶类型 — 任何类型都可赋值给它

    public static readonly ClassVal Exception;

    public static readonly ClassVal TypeError;
    public static readonly ClassVal NameError;
    public static readonly ClassVal AttributeError;
    public static readonly ClassVal IndexError;
    public static readonly ClassVal KeyError;
    public static readonly ClassVal ZeroDivisionError;
    public static readonly ClassVal AssertionError;
    public static readonly ClassVal AccessError;
    public static readonly ClassVal ArgumentError;
    public static readonly ClassVal ValueError;
    public static readonly ClassVal IoError;
    public static readonly ClassVal RegexError;

    public static readonly ClassVal Json;

    public static readonly ClassVal DotNetObject;

    public static readonly ClassVal DotNetType;
    public static readonly ClassVal Ravel;
    public static readonly ClassVal ScopeType;
    public static readonly ClassVal Property;
    // 接口 — `interface`(见 BuiltinClasses.Interfaces.cs)。parent 是 `type`。
    public static readonly ClassVal Interface;
    public static readonly ClassVal BaseInterface;
    public static readonly ClassVal IValue;

    internal static readonly List<ClassVal> AllTypes = [];

    // 类对象**还没建完**的时候(`New` 里就在往类的作用域 `Define` 名字),成员链是悬空的
    // (`ObjectMembers.Any` 要读 `Object.Members`,而 `Object` 这会儿还是 null)——
    // `Scope.Define` 里那条"钉住的成员不许遮"的检查先跳过,建完再开(见静态构造末尾)。
    internal static bool Booting = true;

    private static int _builtinCount;

    static BuiltinClasses()
    {
        // ---- 第一趟：建出所有类对象，ClassType 先自指 ----
        Object = New("Object");
        Function = New("Function");
        Int = New("Integer");           // 名字是 Integer，字段名沿用旧名 Int
        Real = New("Real");
        Bool = New("Bool");
        String = New("String");
        Char = New("Char");
        BigInt = New("BigInt");
        Fraction = New("Fraction");
        BigFraction = New("BigFraction");
        Range = New("Range");
        Block = New("Block");
        Continuation = New("Continuation");
        List = New("List");
        Set = New("Set");
        Dict = New("Dict");
        Pair = New("Pair");
        Waitable = New("Waitable");
        Void = New("Void");
        Type = New("Type");
        Ravel = New("Ravel");
        ScopeType = New("Scope");
        Property = New("Property");
        Interface = New("Interface");
        BaseInterface = New("BaseInterface");
        IValue = New("IValue");
        Exception = New("Exception");
        TypeError = New("TypeError");
        NameError = New("NameError");
        AttributeError = New("AttributeError");
        IndexError = New("IndexError");
        KeyError = New("KeyError");
        ZeroDivisionError = New("ZeroDivisionError");
        AssertionError = New("AssertionError");
        AccessError = New("AccessError");
        ArgumentError = New("ArgumentError");
        ValueError = New("ValueError");
        IoError = New("IoError");
        RegexError = New("RegexError");
        Json = New("Json");
        DotNetObject = New("DotNetObject");
        DotNetType = New("DotNetType");

        // ---- 第二趟：挂 parent 链 + 回填元类 ----
        // Function 必须早于 Bool / Block（它们的父类）
        Link(Object, Object, Type);         // parent 自引用(链到头);元类是 type
        Link(Function, Object, Type);

        // 值类型直挂 `Object`(**不在** `ValueType` 那一支了 —— 那一支改成了接口 `IValue`,
        // 名单和封口都在 `SealBuiltins`)。`bool` 是函数:true/false 可调用,收两个块返回选中那个的结果(lisp 式)
        Link(Int, Object, Type);
        Link(Real, Object, Type);
        Link(Bool, Function, Type);
        Link(String, Object, Type);
        Link(Char, Object, Type);
        Link(BigInt, Object, Type);
        Link(Fraction, Object, Type);
        Link(BigFraction, Object, Type);
        Link(Range, Object, Type);

        // 引用类型
        Link(Block, Function, Type);
        Link(Continuation, Function, Type);
        Link(List, Object, Type);
        Link(Set, Object, Type);
        Link(Dict, Object, Type);
        Link(Pair, Object, Type);
        Link(Waitable, Object, Type);

        Link(Void, Object, Type);

        // 元类型 —— 元类是它自己
        Link(Type, Function, Type);

        Link(Ravel, Object, Type);
        Link(ScopeType, Object, Type);
        Link(Property, Object, Type);
        Link(Exception, Object, Type);
        // 错误那一族 —— 都挂在 Exception 下面(`try (e: Exception)` 照旧全接)
        Link(TypeError, Exception, Type);
        Link(NameError, Exception, Type);
        Link(AttributeError, Exception, Type);
        Link(IndexError, Exception, Type);
        Link(KeyError, Exception, Type);
        Link(ZeroDivisionError, Exception, Type);
        Link(AssertionError, Exception, Type);
        Link(AccessError, Exception, Type);
        Link(ArgumentError, Exception, Type);
        Link(ValueError, Exception, Type);
        Link(IoError, Exception, Type);
        Link(RegexError, Exception, Type);
        Link(Json, Object, Type);
        // 反射那两个值 —— 直挂 `Object`（它们不是"值类型"：按身份认，不做 IValue）
        Link(DotNetObject, Object, Type);
        Link(DotNetType, Object, Type);
        // 接口继承 `type`:于是 `interface is type`,而 `interface { … }` 造出来的是**类对象**
        Link(BaseInterface, Object, Interface);   // 它自己就是个接口(所有接口的根)
        // `IValue` 是**内置的接口**:和平常的接口一样 —— 元类是 `Interface`、
        // parent 是 `BaseInterface`(`BuildInterface` 给无父接口挂的也是它)。
        Link(IValue, BaseInterface, Interface);
        Link(Interface, Type, Type);
        // 底类型/顶类型：parent 自引用（链到自己就停）

        // ---- 每个内置类自己那份成员(要在注册之前:注册里有些地方要读成员) ----
        AttachMembers();
        // ---- 注册内置运算符（作为方法） ----
        RegisterOperators();
        // ---- 注册内置方法 ----
        RegisterMethods();
        // ---- 注册类型构造器 ----
        RegisterInitializers();
        // ---- type 的 init:默认的建类逻辑(必须在 Type/Object/Function 都挂好之后) ----
        InstallTypeInit();
        // ---- interface 的 init:借着上面那套(它的类体也要建类那趟) ----
        InstallInterfaceInit();

        // ---- 收集所有内置类（供 Subtypes 反射） ----
        foreach (var t in new[]
                 {
                     Object, Int, Real, Bool, String, Char, BigInt,
                     Fraction, BigFraction, Range, Function, Block, Continuation,
                     List, Set, Dict, Pair, Waitable, Void, Type, Interface, BaseInterface, IValue,
                     Ravel, Exception, Json, DotNetObject, DotNetType, ScopeType, Property,
                     TypeError, NameError, AttributeError, IndexError, KeyError,
                     ZeroDivisionError, AssertionError, AccessError, ArgumentError, ValueError,
                     IoError, RegexError
                 })
            AllTypes.Add(t);

        // 数据类型和插件`using "x.dll"`带来的类都由 `BuiltinClasses.AddType` 自己往前推这个数
        _builtinCount = AllTypes.Count;

        // ---- 收尾:内置那几支**封口**(见 SealBuiltins) ----
        SealBuiltins();

        // 类对象齐了、成员链也能走了 —— 从这一刻起 `Scope.Define` 才拦"遮钉住的成员"。
        Booting = false;
    }

    private static void AttachMembers()
    {
        // 每个类自己那份成员工厂。**父子关系写在 `MemberSet.Scan` 的第二个参数上**
        // (`StringMembers` 的父是 `ObjectMembers` …),这里只把类接上去。
        //
        // `Object` 接的是**运算符那份** —— 它的父就是 `ObjectMembers`,所以一条
        // 通用成员只写一遍,`:` / `==` / `<:` 也一并跟着来。
        Object.Members = ObjectOperatorMembers.Set;
        Void.Members = VoidOperatorMembers.Set;
        Ravel.Members = ModuleMembers.Set;
        Int.Members = IntegerMembers.Set;

        Real.Members = RealMembers.Set;
        BigInt.Members = BigIntMembers.Set;
        Fraction.Members = FractionMembers.Set;
        BigFraction.Members = BigFractionMembers.Set;

        // `Function` 那套(`Name` / `Scope` / `Body` / `SetScope` / `Prepend` / `Append`)
        // 是**函数这一类**的公共尾巴:类对象、布尔、代码块、续体都是它的实例。
        // `Function` 自己也有运算符(`|` 组合、`>>` 喂一个实参),所以它那份是两半合起来的。
        Function.Members = MemberSet.Combine(FunctionOperatorMembers.Set, FunctionMembers.Set);
        var function = Function.Members;
        Type.Members = MemberSet.Combine(TypeMembers.Set, function);
        Bool.Members = MemberSet.Combine(BoolMembers.Set, function);
        Block.Members = function;
        Continuation.Members = function;

        String.Members = MemberSet.Combine(StringOperatorMembers.Set, StringMembers.Set);
        Char.Members = MemberSet.Combine(CharOperatorMembers.Set, CharMembers.Set);
        Range.Members = RangeMembers.Set;
        List.Members = ListMembers.Set;
        Set.Members = SetMembers.Set;
        Dict.Members = DictMembers.Set;
        Pair.Members = PairMembers.Set;
        ScopeType.Members = ScopeMembers.Set;
        Property.Members = PropertyMembers.Set;

        Json.Members = JsonMembers.Set;
        DotNetObject.Members = DotNetObjectMembers.Set;
        DotNetType.Members = DotNetTypeMembers.Set;
        // `Interface` 自己的 parent 是 `Type`(`Link(Interface, Type, Type)`),所以
        // `IValue.GetImplementors ()` 这类读的就是 `Type` 那份 —— 用户建的接口也一样。
        Interface.Members = Type.Members;
        // 接口:`Link` 不接 `Members`,而且接口的实例就是**实现对象自己**,
        // 所以这儿的工厂得把 `Dispose` 递到每一个接口实现上去。
        BaseInterface.Members = InterfaceMembers.Set;
        IValue.Members = InterfaceMembers.Set;
    }

    private static void SealBuiltins()
    {
        // **谁按值比**(`IValue` 的实现者)—— 不可变、`==` 比内容、能当字典的键。
        // 这份名单从前是 `lib/values.rav` 里那 10 条 `impl`,搬到这儿是因为 IValue 封了、
        // 而 `impl` 那条口子也一并归引擎。
        ClassVal[] values =
        [
            Int, Real, BigInt, Fraction, BigFraction, String, Char, Bool, Void, Range, Pair,
        ];
        foreach (var v in values)
        {
            v.IsValueLike = true;
            v.IsSealed = true;
        }

        // 集合类型 —— `list` / `set` / `dict` 自己那一套(预设类体、内部表示、序列方法)
        foreach (var c in new[] { List, Set, Dict, Pair })
            c.IsSealed = true;

        // `IValue` 本身:用户不能再 `impl` 它了(那正是"值类型这份名单引擎说了算"那句)
        IValue.IsSealed = true;
    }

    // ============================================================
    //  预设类体 —— 内置类的"类体"
    // ============================================================

    private static BlockVal PresetCtor(RuntimeValue caster)
        => new(
            new BlockExpr([new VarDefinition(ObjectVal.InitMember, null, new LiteralExpr(caster))
                { Line = 1, Column = 1, Preset = true, Attrs = [Attr.Protected] }])
                { Line = 1, Column = 1, Source = "<preset>" },
            new Scope());

    internal static void SetCtor(ClassVal cls, System.Reflection.MethodInfo factory)
        => BuiltinCtor(cls, FunctionVal.From(arg => ClassRegistry.Call(factory, [arg])));

    internal static void BuiltinCtor(ObjectVal type, RuntimeValue caster)
        => type.ClassBody = PresetCtor(caster);

    internal static RuntimeValue BindProperty(RuntimeValue prop, RuntimeValue self)
        => prop is PropertyVal pv && (pv.Getter is ISelfBinding || pv.Setter is ISelfBinding)
            ? pv with { Getter = BoundFn(pv.Getter, self), Setter = BoundFn(pv.Setter, self) }
            : prop;

    private static FunctionVal BoundFn(FunctionVal f, RuntimeValue self)
        => f is ISelfBinding ? ObjectVal.BindMethod(f, self) : f;

    internal static Variable? ClassBySlot(ObjectVal obj, string name)
        => obj.MemberScope.LookupField(name) is { } v && v.HasAttr(Attr.By) ? v : null;

    internal static void ClassSideMember(ObjectVal type, string name, BuiltinMethodVal impl)
    {
        impl.Name = name;
        type.Scope.DefineOrReplace(name, impl).SetAttr(Attr.Readonly);
    }

    internal static readonly BlockVal EmptyBody =
        new(new BlockExpr([]) { Line = 1, Column = 1 }, new Scope());

    private static void InstallTypeInit()
    {
        // 两种写法:`某个类 (父类) { 体 }` / `某个类 { 体 }`。
        // 从前靠 `Alternate` 逐支试,现在看一眼参数直接选 —— 类是 `Function` 的子类,先认类。
        BuiltinCtor(Type, new NativeClosure("_", Object, (scope, param) => param switch
        {
            ObjectVal { IsClass: true } parent => FunctionVal.From(body => Install(scope, parent, body)),
            FunctionVal => Install(scope, Object, param),
            _ => throw new RuntimeException(
                "`某个类` 后面要跟一个父类（`某个类 (父类) { … }`）或一个代码块（`某个类 { … }`）"
                + $"，得到 {param.Type}", ErrorKind.Argument),
        }));
    }

    internal static bool SealedUp(ObjectVal t)
    {
        for (var cur = t; cur != null; cur = cur.Parent)
        {
            if (cur.IsSealed) return true;
            if (ReferenceEquals(cur.Parent, cur)) break;
        }

        return false;
    }

    private static ObjectVal? SealedAncestor(ObjectVal t)
    {
        for (var cur = t; cur != null; cur = cur.Parent)
        {
            if (cur.IsSealed) return cur;
            if (ReferenceEquals(cur.Parent, cur)) break;
        }

        return null;
    }

    private static ClassVal Install(Scope scope, ObjectVal parent, RuntimeValue body)
    {
        // **密封的不能再被继承** —— `class 某个密封的类 { … }`(接口同理:`interface 某个密封的接口`),
        // 都在这一句上挡住。放在这儿是因为**只此一个建类入口**。
        if (SealedAncestor(parent) is { } seal)
            throw new RuntimeException(
                $"`{seal.DisplayName}` 被 seal 了 —— 不能再被继承"
                + (parent == seal ? "" : $"（`{parent.DisplayName}` 就在它的链上）"), ErrorKind.Access);

        var pieces = BodyPieces(body);
        var blk = pieces[0];

        // **同一个块挂在祖先链的两层上** —— 建类时当场说清楚。
        //
        // 不查的话它会跑到实例化才发作,而症状是静默的:那一块被跑两遍,里面的定义第二次
        // 覆盖掉第一次(值被重置回默认)。`Scope.Define` 那条"同一条定义语句重跑"的豁免
        // (为 `while` 的续延重入开的)正好把 `Site` 相同的情形放过去,一个字都不说。
        //
        // **预设体不在其列**:引擎自己装的类体是 C# 造的(`PresetCtor` 标了 `Source = "<preset>"`),
        // 而错误那一族**故意**共用同一份 —— 那不叫重复(见 `RegisterInitializers`)。
        for (var t = parent; t != null; t = t.Parent)
        {
            if (t.ClassBody is { } cb && IsShared(pieces, cb))
                throw new RuntimeException(
                    "同一个代码块被挂在类的**两层**上了（比如 `M := { … }` 之后 `class M` 又 `class 父 M`）"
                    + " —— 实例化时它会跑两遍,里面的定义第二次会静默盖掉第一次", ErrorKind.Value);
            foreach (var extra in t.BodyExtras)
                if (IsShared(pieces, extra))
                    throw new RuntimeException(
                        "同一个代码块被挂在类的**两层**上了（`body.Append` 拼上去的那一块也是这个规矩）"
                        + " —— 实例化时它会跑两遍,里面的定义第二次会静默盖掉第一次", ErrorKind.Value);
            if (ReferenceEquals(t.Parent, t)) break;
        }
        // 正在被装成的这个对象**一般是 ClassVal**:这段在实例化 `type` 或它的子类时跑,
        // 而 StepClassInit 正是按"被实例化的类 <: type"来决定造 ClassVal 的。
        // 但**接口**是个例外:接口对象的 parent 是 `BaseInterface`(普通对象),
        // 所以"给接口再套一个代码块"这种写法会走到这儿而 `this` 只是个普通对象 ——
        // 硬转就是 C# 的 InvalidCastException(不是 RuntimeException,`try` 接不住,一路打穿到顶层),
        // 所以改成说人话的 Ravel 错误。
        if (scope.Lookup(ObjectVal.ThisMember).Value is not ClassVal self)
            throw new RuntimeException("这个类型不能再套一个代码块来建类（它是个接口：接口是用 `interface { … }` 造的）", ErrorKind.Type);
        // `parent` / `block` **只读**:它们是"这个类是什么"的定义,改它等于把类换一个
        // (`C.parent = int` 之后 `C ()` 就去跑 Integer 的构造器了)。装类是**一次**的事
        // —— 想换个父类就再造一个类,别改这一个。
        self.Scope.DefineOrReplace(ObjectVal.ParentMember, parent).SetAttr(Attr.Readonly);
        self.Scope.DefineOrReplace(ObjectVal.BlockMember, blk).SetAttr(Attr.Readonly);
        self.Scope.Define(ObjectVal.NameMember, new StringVal(""));
        self.BodyExtras = pieces.Count > 1 ? pieces[1..] : [];
        AllTypes.Add(self);
        // 父类给实例准备的那份**成员工厂**按引用接下(不是抄):`class (P) { … }` 白得 P 的
        // 所有成员,而且 P 换了自己的那份(内置类在 `AttachMembers`、插件类在装载时)子类跟着变。
        // 成员的来源只有这一个 —— 外置注册表已经删了(见 `Runtime/Members/`)。
        if (parent is ClassVal parentType) self.Members = parentType.Members;

        return self;
    }

    private static bool IsShared(List<BlockVal> pieces, BlockVal block)
        => pieces.Any(p => ReferenceEquals(p, block));

    private static List<BlockVal> BodyPieces(RuntimeValue body)
    {
        var pieces = new List<BlockVal>();
        Walk(body);
        return pieces;

        void Walk(RuntimeValue v)
        {
            switch (v)
            {
                case BlockVal b:
                    pieces.Add(b);
                    break;
                case ComposeVal { IsPrepend: true, Block: BlockVal pb } c:
                    pieces.Add(pb);
                    Walk(c.Original);
                    break;
                case ComposeVal { Block: BlockVal sb } c:
                    Walk(c.Original);
                    pieces.Add(sb);
                    break;
                default:
                    throw new RuntimeException("class 需要代码块参数", ErrorKind.Argument);
            }
        }
    }

    internal static ClassVal New(string name)
    {
        var t = new ClassVal(null, null);   // null → ClassType 先自指;表由 ObjectVal.Scope 懒建
        t.Scope.Define(ObjectVal.NameMember, new StringVal(name));
        ByName[name] = t;
        return t;
    }

    private static readonly Dictionary<string, ClassVal> ByName = [];

    internal static void AddType(ClassVal cls)
    {
        if (!AllTypes.Contains(cls)) AllTypes.Add(cls);
        if (AllTypes.Count > _builtinCount) _builtinCount = AllTypes.Count;
    }

    internal static ClassVal ClassOf(string name, string what = "类型")
        => ByName.TryGetValue(name, out var t)
            ? t
            : throw new RuntimeException($"{what}: 没有 '{name}' 这个内置类", ErrorKind.Name);

    internal static ClassVal? ClassOrNull(string name)
        => ByName.TryGetValue(name, out var t) ? t : null;

    internal static void Link(ObjectVal t, ObjectVal parent, ClassVal meta)
    {
        t.ClassType = meta;
        t.Scope.DefineOrReplace(ObjectVal.ParentMember, parent).SetAttr(Attr.Readonly);
    }

    // ============================================================
    //  用户自定义类
    // ============================================================

    internal static void ResetUserTypes()
        => AllTypes.RemoveRange(_builtinCount, AllTypes.Count - _builtinCount);

    internal static List<BodyStep> CollectBodies(ObjectVal type)
    {
        var layers = new List<List<BodyStep>>();
        for (var t = type; ; t = t.Parent)
        {
            if (t == null) break;
            var body = t.ClassBody;
            if (body == null) break;
            var steps = new List<BodyStep> { new(body, body.CaptureScope) };
            foreach (var extra in t.BodyExtras) steps.Add(new(extra, body.CaptureScope));
            layers.Add(steps);
            if (t.Parent == t) break;
        }

        layers.Reverse();
        return [.. layers.SelectMany(s => s)];
    }
}

internal readonly record struct BodyStep(BlockVal Block, Scope Lexical);
