namespace Ravel.Runtime;

/// <summary>内置类对象。
///
/// **每个类型都是一个 <see cref="ObjectVal"/>** —— 和用户写的类用的是同一种值。
/// 类性靠 Scope 里的成员表达：`parent`（父类对象）+ `block`（类体）。
/// 内置类没有用户写的类体，所以 S1 阶段它们的成员靠 <see cref="ObjectVal.DefineMethod"/>
/// 直接注册进 Scope（S2 会改成"预设类体里定义"，那时"内置类也有类体"就名副其实了）。
///
/// 静态字段名沿用旧 `RuntimeType` 的名字（`Int`/`Real`/…），三个注册文件因此可以原样搬运。
///
/// 建树分两趟：第一趟把所有类对象建出来（`ClassType` 先自指），第二趟回填元类并挂
/// `parent`/`name` —— 因为建 `Object` 的时候 `Type` 还不存在，而 `Object` 的元类是 `Type`。</summary>
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
    /// <summary>区间(`[1..3]` / `(3..5)` …)—— `RangeVal`。
    /// 它是**值类型**(不可变、按值比)—— 名单在 `SealBuiltins` 里,直挂 `Object` 下。</summary>
    public static readonly ClassVal Range;

    // 引用类型分支（可变/有行为）
    public static readonly ClassVal Function;
    public static readonly ClassVal Block;
    /// <summary>`callcc` 交出来的那枚续延(`ContinuationVal`)。父类是 `Function` ——
    /// 续延本来就能调、能存进字段、能当参数传;它只是**自己的类型**,好让 `typeof` /
    /// 注解 / `is Continuation` 说得清"这是续延,不是普通函数"。
    /// `default` 给的是"还没到手的那一枚"(一调就报错,见 ContinuationVal.Default)。</summary>
    public static readonly ClassVal Continuation;
    public static readonly ClassVal List;
    public static readonly ClassVal Set;
    public static readonly ClassVal Dict;

    /// <summary>**等待句柄**(`WaitableVal`)—— "那边做完了会给一个值",`System.WaitAny` /
    /// `HandleValue` 两条原语围着它转,调度器在 `lib/tasks.rav`。
    /// 它是**引用类型**(句柄没有"相等"这回事,比的是最后拿到的值)。</summary>
    public static readonly ClassVal Waitable;

    // void — 唯一值 ()
    public static readonly ClassVal Void;

    /// <summary>元类型 —— 所有类型的类型，也是"建类"这个动作本身（`class` 是它的别名）。
    /// 它的元类是**它自己**（自指）。用户类直接挂在它下面。</summary>
    public static readonly ClassVal Type;

    // 底类型 — 为所有类的子类，default 是其唯一实例；不在继承树里，IsAssignableTo 全局特判
    public static readonly ClassVal Every;

    // 顶类型 — 任何类型都可赋值给它
    public static readonly ClassVal Any;

    public static readonly ClassVal Exception;

    /// <summary>**引擎报错的那一族** —— 一个 <see cref="ErrorKind"/> 一个类,全是 `Exception`
    /// 的子类,所以 `try { … } (e: Exception) => …` 照旧接得住全部,而
    /// `(e: TypeError) => …` 只接那一族(挑哪一类见 `BuiltinClasses.Errors.cs`)。
    ///
    /// 它们在**引擎**里建、不在 `lib/exceptions.rav` 里建:报错的是引擎,分类也该由引擎说了算
    /// (库里再定义一遍就成两处各管一半)。用户在 Ravel 里照样能继承、能
    /// `throw (TypeError "自己造的")` —— 它们和别的内置类没两样。</summary>
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

    /// <summary>JSON 值:里面包着一棵 Newtonsoft 的 `JToken` 树,`Extract ()` 才转成
    /// dict / list / 原生值。`predefined.rav` 给全局别名 `Json`。</summary>
    public static readonly ClassVal Json;

    /// <summary>一个**任意 .NET 对象**（`DotNetVal` 的类）。方法见 `BuiltinClasses.Net.cs`。
    /// 从前是 `Ravel.Reflect` 插件带来的 `[RavelClass]`,2026-10-07 搬进引擎 ——
    /// `DotNetVal` 的基类 ctor 要按它挂类,不在引擎里的话没 `using` 那个插件就造不出来。</summary>
    public static readonly ClassVal DotNetObject;

    /// <summary>一个 **`System.Type`**（`DotNetTypeVal` 的类）。同一个来路。</summary>
    public static readonly ClassVal DotNetType;
    public static readonly ClassVal Ravel;
    public static readonly ClassVal ScopeType;
    public static readonly ClassVal Property;
    // 接口 — `interface`(见 BuiltinClasses.Interfaces.cs)。parent 是 `type`。
    public static readonly ClassVal Interface;
    /// <summary>**所有接口的基类**。`interface { … }` 造出来的接口,parent 挂在它下面
    /// (不是 `object`),于是那些接口**继承它类体里的 `init`** —— 造实现(`某接口 某个类 { … }`)
    /// 就是实例化那个接口对象,得有 `init` 才跑得起来。
    ///
    /// 从前没这一层:接口的 parent 直接是 `object`,继承不到 `init`,于是每个接口的类体里都被
    /// **烤进一份** `init`。多一层基类之后那份烤就不用了 —— 一处默认,
    /// 所有接口共用(而且实现 scope 里也不再莫名多出一个没人看的 `init`)。</summary>
    public static readonly ClassVal BaseInterface;
    /// <summary>`IValue` —— "这个类型是个**值**"那个接口。**内置**,而且**封了**
    /// (见 `SealBuiltins`):实现者那份名单由引擎说了算 —— 内置那 10 个值类型在
    /// `SealBuiltins` 里直接标,插件走 `[RavelClass(Implements = "IValue")]`,
    /// 用户代码 `impl` 不进来。
    ///
    /// **内置**而不是让 Ravel 侧建:引擎要拿它做**引用比较** —— 判据要跑在
    /// `dict.SysGet` 那些**同步 C#** 里,按名字比(`DisplayName == "IValue"`)
    /// 能跑但脆(名字被别人用了就误判),有个句柄就干净了。</summary>
    public static readonly ClassVal IValue;

    /// <summary>所有已注册的类对象（内置 + 用户定义），供 Subtypes 反射</summary>
    internal static readonly List<ClassVal> AllTypes = [];

    /// <summary>AllTypes 里内置类占多少——静态构造器填完后定下来，Reset 时按它切</summary>
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
        Every = New("Every");
        Any = New("Any");

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
        // ---- interface 的 init:借着上面那套(它的类体也要 Alternate/Install) ----
        InstallInterfaceInit();

        // ---- 收集所有内置类（供 Subtypes 反射） ----
        foreach (var t in new[]
                 {
                     Object, Int, Real, Bool, String, Char, BigInt,
                     Fraction, BigFraction, Range, Function, Block, Continuation,
                     List, Set, Dict, Waitable, Void, Type, Interface, BaseInterface, IValue,
                     Ravel, Any, Every, Exception, Json, DotNetObject, DotNetType, ScopeType, Property,
                     TypeError, NameError, AttributeError, IndexError, KeyError,
                     ZeroDivisionError, AssertionError, AccessError, ArgumentError, ValueError,
                     IoError, RegexError
                 })
            AllTypes.Add(t);

        // 数据类型和插件`using "x.dll"`带来的类都由 `BuiltinClasses.AddType` 自己往前推这个数
        _builtinCount = AllTypes.Count;

        // ---- 收尾:内置那几支**封口**(见 SealBuiltins) ----
        SealBuiltins();
    }

    /// <summary>**内置的值类型、集合类型,和 `IValue` —— 到此为止,不能再往下长。**
    ///
    /// 理由:这几支的形状是**引擎定死的**那一份 —— `int` 的构造器认字符串、`List` 的预设类体、
    /// "哪些类型按值比"那张表,全是 C# 侧装上去的。放进来继承只会长出一个半生不熟的子类型:
    /// 引擎那条判据(`IsValueLike`、按类型找的运算符表、`ConvertDirect`)一个都不认它。
    ///
    /// 挡的是两件事(两处都问 <see cref="SealedUp"/>,判据同一份):
    /// <list type="bullet">
    /// <item>`class int { … }` / `interface 某接口 IValue { … }` —— 建类那一步(<see cref="Install"/>);</item>
    /// <item>`impl (IValue 我的类 { … })` —— 造实现那一步(`Interpreter.StepImplMake`)。</item>
    /// </list>
    ///
    /// **`IValue` 封了,那 10 个内置实现就不走 `impl` 了** —— 直接落
    /// <see cref="ClassVal.IsValueLike"/> 那格**索引**(从前那 10 条 `impl` 在 `lib/values.rav`)。
    /// 索引本来就是这格的唯一真相(见 `HasTrait` 那条短路):O(1)、不吃解释器 ——
    /// 于是 `dict.SysGet` 那些**同步 C#** 里也问得了同一个答案。顺带省掉了
    /// "每个解释器都重跑一遍那 10 条 `impl`"(实现登记在各解释器各自的作用域里,而这是进程级的一份)。
    ///
    /// **插件不受影响**:`[RavelClass(Implements = "IValue")]` 本来就是直接写那格索引,
    /// 不走 Ravel 侧的 `impl`(见 `PluginApi`)。所以"新的值类型"仍由**引擎/插件**说了算 ——
    /// 封的只是**用户代码**这条口子。
    ///
    /// **`class int { … }` 那条也因此没了**:从前靠继承内置类型白拿它的构造器
    /// (`MyInt ::= class int { … }` 之后 `MyInt "42"` 转出个 42),现在被这条挡下。</summary>
    private static void SealBuiltins()
    {
        // **谁按值比**(`IValue` 的实现者)—— 不可变、`==` 比内容、能当字典的键。
        // 这份名单从前是 `lib/values.rav` 里那 10 条 `impl`,搬到这儿是因为 IValue 封了、
        // 而 `impl` 那条口子也一并归引擎。
        ClassVal[] values =
        [
            Int, Real, BigInt, Fraction, BigFraction, String, Char, Bool, Void, Range,
        ];
        foreach (var v in values)
        {
            v.IsValueLike = true;
            v.IsSealed = true;
        }

        // 集合类型 —— `list` / `set` / `dict` 自己那一套(预设类体、内部表示、序列方法)
        foreach (var c in new[] { List, Set, Dict })
            c.IsSealed = true;

        // `IValue` 本身:用户不能再 `impl` 它了(那正是"值类型这份名单引擎说了算"那句)
        IValue.IsSealed = true;
    }

    // ============================================================
    //  预设类体 —— 内置类的"类体"
    // ============================================================

    /// <summary>造一段**预设类体**:内置类的类体,里面只定义它的 `init`(构造器)。
    ///
    /// 内置类的成员是 C# 造好的值(转换器、默认建类函数),写不出 Ravel 源码,
    /// 所以用 <see cref="LiteralExpr"/> 直接塞进去。这样内置类和用户类走**完全相同**的
    /// 实例化路径:跑各层类体 → 在实例作用域里找 `init` → 调它、交出它的返回值。
    ///
    /// 捕获作用域给一个空 scope:体里只有 `init := <字面量值>`,不需要解析任何名字。</summary>
    /// <summary>预设类体:一条 `init := <caster>`,而且**标成 protected**。
    ///
    /// protected 就是构造器该有的可见性:类体系里照旧(子类那句 `init = …` 是普通赋值,
    /// 走的是 `Scope.Assign`,不受成员门禁拦),外面 `obj.init` / `obj.init = …` 读不到也写不了
    /// —— 构造器不是给人从外面拨的开关。</summary>
    private static BlockVal PresetCtor(RuntimeValue caster)
        => new(
            new BlockExpr([new VarDefinition(ObjectVal.InitMember, null, new LiteralExpr(caster))
                { Line = 1, Column = 1, Preset = true, Attrs = [Attr.Protected] }])
                { Line = 1, Column = 1, Source = "<preset>" },
            new Scope());

    /// <summary>给一个内置类装**构造器**(`[ClassCtor]` 那个方法):一条 `init := <工厂>`,
    /// 和 `List` / `Integer` 那些预设类体一个做法(见 `PresetCtor`)。</summary>
    internal static void SetCtor(ClassVal cls, System.Reflection.MethodInfo factory)
        => cls.ClassBody = PresetCtor(FunctionVal.From(arg => ClassRegistry.Call(factory, [arg])));

    /// <summary>往一个**类对象**的**实例表**里挂引擎成员 —— `ClassVal.InstanceTable` 那张
    /// "给实例的成员表"。内置方法、类运算符、序列方法(`SeqMethod`)、`GetImplements ()`
    /// 这类查询(`TraitQuery`)实现各不相同,身份是同一种:**引擎给这一类挂的、实例能用的成员**。
    ///
    /// 落点就是声明 —— 不必再打 `forInstance` 之类的标记:写进哪张表就说明给谁用。
    /// 唯一还挂的属性是 `readonly`(谁都换不掉;`readOnly: false` 只给**用户类体里那句
    /// `+ := f`**,见 <see cref="ObjectVal.DefineClassOperator"/>)。
    ///
    /// 类侧本来就要用的成员(读它的就是类对象自己,比如 `Json.FromString`)别走这条 ——
    /// 用 <see cref="ClassSideMember"/>。</summary>
    /// <param name="declared">那一格的**类型约束**(默认 `Function` —— 方法就是函数值)。
    /// **`by` 属性必须给 `Any`**:那格存的是 property,而写进来的值是**属性值**
    /// (`sb.Length = 0` 写的是 `IntVal`,不是那个 property)—— 按 `Function` 约束的话
    /// 写一次就报「无法将 IntVal 赋值给 'Length'」(见 `Variable.CheckAssignable`)。
    /// 接口那条槽也是这么办的(`impl` 的 `instance` 落 `Any`)。</param>
    internal static Variable EngineMember(ObjectVal type, string name, RuntimeValue impl,
                                          bool readOnly = true, ObjectVal? declared = null)
    {
        // 只有类对象有实例表。挂错了是 C# 侧的程序错误 —— 当场炸比静默挂到别处好
        var v = ((ClassVal)type).InstanceTable.DefineOrReplace(name, declared ?? Function, impl);
        if (readOnly) v.SetAttr(Attr.Readonly);
        return v;
    }

    /// <summary>把一份 property 绑到**这一次的接收者**上再交出去。
    ///
    /// **只有引擎挂在类上的那种 `by` 槽**(`[ClassProperty]`)需要这一步:它那两个函数是
    /// `BuiltinMethodVal`(`ISelfBinding`),和普通类方法一个待遇 —— 读成员的那一刻才认"谁在调"。
    /// 用户自己写的 `property g s` 是普通 lambda(接收者靠捕获的作用域),原样交回;
    /// 接口那条槽的 getter/setter 是 `NativeClosure`(要连捕获作用域一起换,见
    /// <see cref="Activate"/>),也不是 `ISelfBinding`,这儿认不出、也就不插手。
    ///
    /// **读和写都得过这里** —— 一处漏了就是"读得到、写不回"那种半瘫,而两处在两个文件里
    /// (读:`BoxedValue.TryGetByGetter`;写:`Interpreter.PropOf`)。</summary>
    internal static RuntimeValue BindProperty(RuntimeValue prop, RuntimeValue self)
        => prop is PropertyVal pv && (pv.Getter is ISelfBinding || pv.Setter is ISelfBinding)
            ? pv with { Getter = BoundFn(pv.Getter, self), Setter = BoundFn(pv.Setter, self) }
            : prop;

    private static FunctionVal BoundFn(FunctionVal f, RuntimeValue self)
        => f is ISelfBinding ? ObjectVal.BindMethod(f, self) : f;

    /// <summary>**类那层挂的 `by` 槽** —— 插件的 `[ClassProperty]` 就落在那里(类的实例表,
    /// 和 `Append` 那些方法一处)。调用方是"实例自己那层没找到"之后的第二步;
    /// **只认 `by` 槽**:类那层别的东西(方法)不归读写这条路管
    /// (读那条路 `BoxedValue.GetMember` 会去 `MemberScope` 找,而且顺带绑接收者)。
    ///
    /// 少了这一步,引擎挂的属性就是"看得到、摸不着":`sb.Length` 报「没有方法 'Length'」、
    /// `sb.Length = 2` 报「对象没有字段」(实测,四条路各报各的)。</summary>
    internal static Variable? ClassBySlot(ObjectVal obj, string name)
        => obj.MemberScope.LookupField(name) is { } v && v.HasAttr(Attr.By) ? v : null;

    /// <summary>挂一个**类侧**的引擎成员:读它的就是**类对象自己**(`Json.FromString s`)
    /// —— 落类那张表(`Scope`),不进给实例的表。全库只有 `Json.FromString` 一处。</summary>
    internal static void ClassSideMember(ObjectVal type, string name, BuiltinMethodVal impl)
    {
        impl.Name = name;
        type.Scope.DefineOrReplace(name, Function, impl).SetAttr(Attr.Readonly);
    }

    /// <summary>「没有体」时交出去的那个**空块** —— 类型恒定,别拿 `()` 顶替。
    /// 见 `Function.body`。</summary>
    internal static readonly BlockVal EmptyBody =
        new(new BlockExpr([]) { Line = 1, Column = 1 }, new Scope());

    /// <summary>把两个候选做成 `|` 交替(和 `Function.|` 同一个机制,只是从 C# 侧构造)。
    /// 分流靠 TypeMismatchException —— 第一支的参数类型对不上就试第二支,见 StepAlternate。</summary>
    internal static ControlFunction Alternate(RuntimeValue a, RuntimeValue b)
        => new(ControlKind.Alternate, 1, RList<RuntimeValue>.Empty.Add(a).Add(b));

    /// <summary>N 个候选的交替(接口那个 init 要三支:接口表 / 一个类型 / 代码块)。
    /// `Arity` 恒为 1:这一段是**一个**参数上的分流,分支个数由参数表长度带,
    /// 和 `Function.|` 摊平出来的那种是同一个东西(它也是这么堆的)。</summary>
    internal static ControlFunction Alternate(params RuntimeValue[] branches)
    {
        var args = RList<RuntimeValue>.Empty;
        foreach (var b in branches) args = args.Add(b);
        return new ControlFunction(ControlKind.Alternate, 1, args);
    }

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
        Type.ClassBody = PresetCtor(Alternate(twoArg, oneArg));
    }

    /// <summary>这个类型自己**或它的任一祖先**被 `seal` 了吗。
    ///
    /// 两个地方问它:**建类/建接口**(<see cref="Install"/>)和**造实现**(`StepImplMake`)。
    /// 沿 `parent` 链走而不是只看自己 —— 密封说的是"这条继承链到此为止":已经存在的那层
    /// 子类下面也不能再长,不然密封就成了一句只挡直接子类的话。</summary>
    internal static bool SealedUp(ObjectVal t)
    {
        for (var cur = t; cur != null; cur = cur.Parent)
        {
            if (cur.IsSealed) return true;
            if (ReferenceEquals(cur.Parent, cur)) break;
        }

        return false;
    }

    /// <summary>被密封的那个祖先(报错要用它的名字),没有就 null。
    /// 和 <see cref="SealedUp"/> 同一趟走法 —— 一个问有没有,一个问是哪一个。</summary>
    private static ObjectVal? SealedAncestor(ObjectVal t)
    {
        for (var cur = t; cur != null; cur = cur.Parent)
        {
            if (cur.IsSealed) return cur;
            if (ReferenceEquals(cur.Parent, cur)) break;
        }

        return null;
    }

    /// <summary>把 (parent, body) 装到 self 上,self 于是是一个类。返回 self ——
    /// 构造交出 init 的返回值,所以这就是"建出来的那个类"。
    ///
    /// 装完顺手登记进 AllTypes(`Subtypes ()` 反射要用)并扫类体里用符号定义的运算符。
    ///
    /// `body` 可以是**块拼出来的**(`body.Append { … }`,见 <see cref="BodyPieces"/>):
    /// 第一块是主类体,其余记进 <see cref="ObjectVal.BodyExtras"/>,实例化时接着主块跑。</summary>
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
        self.Scope.DefineOrReplace(ObjectVal.ParentMember, Object, parent).SetAttr(Attr.Readonly);
        self.Scope.DefineOrReplace(ObjectVal.BlockMember, Block, blk).SetAttr(Attr.Readonly);
        self.Scope.Define(ObjectVal.NameMember, String, new StringVal(""));
        self.BodyExtras = pieces.Count > 1 ? pieces[1..] : [];
        AllTypes.Add(self);

        // 运算符是**扫体里的语句**装进实例表的(见 `DefineClassOperator`),所以拼上来的块
        // 也要扫 —— 它们和主块同属一层,`body.Append { == := … }` 那个 `==` 得算数。
        foreach (var piece in pieces)
        foreach (var stmt in piece.Block.Statements)
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

    /// <summary>这一串块里有没有**就是** `block` 这一个(按身份比 —— `BlockVal` 是 record,
    /// 默认的相等是按内容,而这儿要的是"同一个块对象")。</summary>
    private static bool IsShared(List<BlockVal> pieces, BlockVal block)
        => pieces.Any(p => ReferenceEquals(p, block));

    /// <summary>把"体"摊平成**一串块**(执行顺序)。`Append` / `Prepend` 交出来的是
    /// <see cref="ComposeVal"/>(原值 + 那一块),这里把它拆开:prepend 的块排在原值前面、
    /// append 的排在后面 —— 和调用时那套顺序一致(见 `Interpreter.StepCompose`),
    /// 只是类体要的是"一串块",不是"一个能调的函数"。
    ///
    /// 别的形状照旧报「class 需要代码块参数」:类体造不出来,只能写出来或拼出来。</summary>
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
                case ComposeVal c when c.IsPrepend:
                    pieces.Add(c.Block);
                    Walk(c.Original);
                    break;
                case ComposeVal c:
                    Walk(c.Original);
                    pieces.Add(c.Block);
                    break;
                default:
                    throw new RuntimeException("class 需要代码块参数", ErrorKind.Argument);
            }
        }
    }

    /// <summary>建一个类对象(第一趟:只有名字,元类先自指)。**顺手登记进名字表** ——
    /// `ClassOf` 就是查它,结构那一族(`[BuiltinClass]`)也靠它找父类。</summary>
    internal static ClassVal New(string name)
    {
        var t = new ClassVal(null, new Scope());   // null → ClassType 先自指
        t.Scope.Define(ObjectVal.NameMember, null!, new StringVal(name));
        ByName[name] = t;
        return t;
    }

    /// <summary>名字 → 类对象(建树那趟顺手填)。给"按名字拿类"的地方用:
    /// 内置值类型的 `Type`(如 `StackVal`)、`[BuiltinClass]` 找父类。
    ///
    /// 从前每个结构要自己留一个 `internal static ClassVal` 字段 —— 那是"加一个结构就得
    /// 记得改几处"的老毛病,一张表就够了。</summary>
    private static readonly Dictionary<string, ClassVal> ByName = [];

    /// <summary>把一个**外来**的类登记进 `AllTypes`(`using "x.dll"` 插件带来的那些)。
    /// 顺手把 `_builtinCount` 往前推 —— 那个数切的是"哪些是**装进来的**(Reset 时留着)"、
    /// 哪些是用户 `class { … }` 现写的(Reset 时清掉);插件带来的属于前者。</summary>
    internal static void AddType(ClassVal cls)
    {
        if (!AllTypes.Contains(cls)) AllTypes.Add(cls);
        if (AllTypes.Count > _builtinCount) _builtinCount = AllTypes.Count;
    }

    internal static ClassVal ClassOf(string name, string what = "类型")
        => ByName.TryGetValue(name, out var t)
            ? t
            : throw new RuntimeException($"{what}: 没有 '{name}' 这个内置类", ErrorKind.Name);

    /// <summary>有就交回、没有交回 `null` —— **不抛**。`ClassOf` 是"必须有"那条，这条是
    /// "**有就用**"：生成出来的适配层拿它决定"能不能把手里这个 .NET 值包成某个 Ravel 类"
    /// （见 `PluginKit.Wrap` —— 调用方为那个类型生成过适配器就包成它，没有就 `DotNetObject`，
    /// 两种都是能用的值）。</summary>
    internal static ClassVal? ClassOrNull(string name)
        => ByName.TryGetValue(name, out var t) ? t : null;

    /// <summary>第二趟：挂 parent、回填元类。约束这时才给得上（String/Object 已经存在）。</summary>
    internal static void Link(ObjectVal t, ObjectVal parent, ClassVal meta)
    {
        t.ClassType = meta;
        t.Scope.DefineOrReplace(ObjectVal.ParentMember, Object, parent).SetAttr(Attr.Readonly);
        t.Scope.LookupField(ObjectVal.NameMember)!.TypeConstraint = String;
    }

    // ============================================================
    //  用户自定义类
    // ============================================================

    /// <summary>清掉已登记的用户类（内置的留着，见 `_builtinCount`）。
    /// `Subtypes` 依赖这张表，而一个进程里可能跑好几个 Interpreter（测试每个文件一个、
    /// REPL 反复 new），不清的话上一个建过的类会出现在下一个的 `Subtypes ()` 里。</summary>
    internal static void ResetUserTypes()
        => AllTypes.RemoveRange(_builtinCount, AllTypes.Count - _builtinCount);

    /// <summary>沿 parent 链收集各层类体，返回「顶祖先 → 自身」。没有类体的层（内建）不入列且到此为止。
    /// 类体创建后不可变，所以这是纯函数，可随帧推进反复调用。
    ///
    /// **一层可能不止一块**：`body.Append { … }` 拼上来的块跟在主块后面，同属这一层 ——
    /// 于是 `Lexical` 给的是**主块**的写法处（拼上来的块自由名字也照这一处解析，
    /// 和"每层各按各的写法处"那条是一个道理，见 <see cref="BodyScope"/>）。</summary>
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

/// <summary>实例化要跑的一步：跑哪一块、那一层的自由名字按哪儿解析（见 <see cref="BodyScope"/>）。
/// 一层通常只有一步；`body.Append { … }` 拼上来的块各算一步，`Lexical` 都指着主块那一处。</summary>
internal readonly record struct BodyStep(BlockVal Block, Scope Lexical);
