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
    public static readonly ClassVal Char;
    public static readonly ClassVal BigInt;
    public static readonly ClassVal Fraction;
    public static readonly ClassVal BigFraction;
    /// <summary>区间(`[1..3]` / `(3..5)` …)—— `RangeVal`。
    /// 它是**值类型**(不可变、按值比),所以挂在 `ValueType` 那一支下。</summary>
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
        Void = New("Void");
        Type = New("Type");
        Ravel = New("Ravel");
        ScopeType = New("Scope");
        Property = New("Property");
        Interface = New("Interface");
        BaseInterface = New("BaseInterface");
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
        Link(Char, ValueType, Type);
        Link(BigInt, ValueType, Type);
        Link(Fraction, ValueType, Type);
        Link(BigFraction, ValueType, Type);
        Link(Range, ValueType, Type);

        // 引用类型
        Link(Block, Function, Type);
        Link(Continuation, Function, Type);
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
        // 接口继承 `type`:于是 `interface is type`,而 `interface { … }` 造出来的是**类对象**
        Link(BaseInterface, Object, Interface);   // 它自己就是个接口(所有接口的根)
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
                     Object, ValueType, Int, Float, Bool, String, Char, BigInt,
                     Fraction, BigFraction, Range, Function, Block, Continuation,
                     List, Set, Dict, Void, Type, Interface, BaseInterface,
                     Ravel, Any, Every, Exception, Json, ScopeType, Property,
                     TypeError, NameError, AttributeError, IndexError, KeyError,
                     ZeroDivisionError, AssertionError, AccessError, ArgumentError, ValueError,
                     IoError, RegexError
                 })
            AllTypes.Add(t);

        // 数据类型和插件`using "x.dll"`带来的类都由 `BuiltinClasses.AddType` 自己往前推这个数
        _builtinCount = AllTypes.Count;
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
    internal static Variable EngineMember(ObjectVal type, string name, RuntimeValue impl,
                                          bool readOnly = true)
    {
        // 只有类对象有实例表。挂错了是 C# 侧的程序错误 —— 当场炸比静默挂到别处好
        var v = ((ClassVal)type).InstanceTable.DefineOrReplace(name, Function, impl);
        if (readOnly) v.SetAttr(Attr.Readonly);
        return v;
    }

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

    /// <summary>把 (parent, body) 装到 self 上,self 于是是一个类。返回 self ——
    /// 构造交出 init 的返回值,所以这就是"建出来的那个类"。
    ///
    /// 装完顺手登记进 AllTypes(`Subtypes ()` 反射要用)并扫类体里用符号定义的运算符。
    ///
    /// `body` 可以是**块拼出来的**(`body.Append { … }`,见 <see cref="BodyPieces"/>):
    /// 第一块是主类体,其余记进 <see cref="ObjectVal.BodyExtras"/>,实例化时接着主块跑。</summary>
    private static ClassVal Install(Scope scope, ObjectVal parent, RuntimeValue body)
    {
        var pieces = BodyPieces(body);
        var blk = pieces[0];
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

    /// <summary>建一个模块的类对象（`ravel "M"` / System 模块用）。模块也是类型，
    /// 但它的成员住在**模块作用域**(`ModuleVal.Scope`,同时也是它的成员表)里 ——
    /// 所以不登记进 AllTypes（每个 Interpreter 都重建一份，登记只会累积）。</summary>
    internal static ClassVal NewModuleClass(string name, ObjectVal parent)
    {
        var t = new ClassVal(Type, new Scope());
        t.Scope.Define(ObjectVal.ParentMember, Object, parent).SetAttr(Attr.Readonly);
        t.Scope.Define(ObjectVal.NameMember, String, new StringVal(name));
        return t;
    }

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
