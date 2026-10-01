namespace Ravel.Runtime;

/// <summary>函数值 —— **是对象的一种**(`FunctionVal : ObjectVal`)。所以它身上有两个
/// 各归其位的"作用域",分家之后谁也不冒充谁:
///
/// - <see cref="CaptureScope"/>:**捕获作用域**(lambda 定义处的词法作用域),闭包靠它。
///   类对象没有这一个 —— 它不是闭包。`Function.scope ()` 取的是这个。
/// - 继承来的 `ObjectVal.Scope`:**成员表**(这个值身上挂着哪些成员),与对象同源。
///
/// 从前这两个概念共用一个名叫 `Scope` 的成员:函数说那是捕获作用域、对象说那是成员表,
/// 一个名字两个意思 —— 于是 `Function.scope ()` 会把对象的成员表当"作用域"交出去。
/// 名字也不重复:类名/函数名统一落在成员表的 `name` 成员上(见 <see cref="ObjectVal.Name"/>),
/// 不再有"函数名住自动属性、类名住成员表"这种看静态类型才知道往哪写的分裂。
///
/// `Body` 一律是同步的「参数 → 结果」:求值器用帧栈表达延迟,不需要 Step 包装。</summary>
public record FunctionVal : ObjectVal
{
    public Func<RuntimeValue, RuntimeValue> Body { get; set; }

    /// <summary>捕获作用域 —— 函数是它定义处的那个词法作用域。
    ///
    /// `null` 是给那些**不靠捕获作用域**的后代留的:内置方法、`Bool`/`Block`/控制内建
    /// 要么由求值器按类型先分派、要么只靠参数自足。它们读了就是 null,
    /// 所以取用点要自带兜底(见 `Function.scope` 的 `?? new Scope()`)。</summary>
    public Scope CaptureScope { get; set; }

    /// <summary>函数的元类恒为 `Function`。
    ///
    /// 允许 `ClassType` 为 null 并**懒回填**:构造函数是在基类 ctor 之后才填自己的,
    /// 而基类 ctor 里那句 `ClassType = BuiltinClasses.Function` 有可能撞上静态初始化
    /// —— 那时 `Function` 还是 null,读它得到 null 而不是异常,所以这里必须能兜住。
    /// **别在 ctor 里读 `BuiltinClasses.X` 并假定它已就绪。**</summary>
    public override ObjectVal Type => ClassType ??= BuiltinClasses.Function;

    public override string ToString() => Name != null ? "<function " + Name + ">" : "<function>";

    /// <summary>**半成品**:实参还没收满,再喂它才接着收(柯里化)。
    ///
    /// 打标的地方就那么几处,因为"还等着实参"这件事只有**分派那一步**知道:
    /// 柯里化的 lambda 交回内层时(`CallInto` 的 LambdaVal 那一格 + `BlockExecFrame.Curried`)、
    /// 内置的柯里化函数交回内层时(<see cref="From(Func{RuntimeValue, RuntimeValue, RuntimeValue})"/>)、
    /// 以及本身就是半成品的 <see cref="PartialCtor"/> / <see cref="PartialBool"/>。
    ///
    /// **别拿 `LambdaVal.Applied ()` 当这个用**:那一条走的是"闭包捕获了定义处的实参",
    /// 在一个被调用过的函数里定义的 lambda 一律非空(`Try` 里的 `mine` 就是),分不出半成品。
    ///
    /// 只有一处用处:`WarnForgotCall` 判"这一句丢掉的函数是不是**忘了给实参**"
    /// (见 `Interpreter.WarnIfForgotCall`)。</summary>
    public bool IsPartial { get; set; }

    /// <summary>需要捕获作用域时用(captureScope 参与签名,Body 里可读 CaptureScope)。
    /// `members` 只有 <see cref="ClassVal"/> 用得上 —— 类对象的成员表由调用方备好(实例作用域)。</summary>
    public FunctionVal(Scope captureScope, Func<Scope, RuntimeValue, RuntimeValue> rawBody, Scope? members = null)
        : base(null!, members ?? new Scope())
    {
        // 基类 ctor 的 `?? this` 是给根元类 `type` 留的自指,函数得改回 Function。
        // (ClassVal 的 ctor 随后会把它改成元类。)
        ClassType = BuiltinClasses.Function;
        CaptureScope = captureScope;
        Body = a => rawBody(CaptureScope, a);
    }

    /// <summary>占位体 —— 给那些**由求值器按类型先分派**的值用(LambdaVal/BlockVal/BoolVal/
    /// 控制内建/续延/…,见 `CallInto` 的分派表)。它们的 `Body` 永远不该被调用。
    ///
    /// 真被调到了,说明**漏了一个 case**(或谁把它们传给了 `BindMethod`)—— 报错,
    /// 不要交出 `()` 也不要让硬转抛 C# 异常:后者漏到顶层会把程序打掉,前者是静默的错答案。
    /// 这两个坑都真出过。</summary>
    public static RuntimeValue PlaceholderBody(string what)
        => throw new RuntimeException($"{what} 的 Body 不该被调用 —— 它由求值器按类型分派(见 CallInto)，这里漏了一个 case");

    /// <summary>单参内置函数(最常见)</summary>
    public static FunctionVal From(Func<RuntimeValue, RuntimeValue> f)
        => new(null!, (_, a) => f(a));

    /// <summary>和 <see cref="From(Func{RuntimeValue, RuntimeValue})"/> 一样,只是**不多包那一层** ——
    /// `Body` 直接就是 `f`(基类 ctor 那句 `a => rawBody(CaptureScope, a)` 被盖掉;对这批内置来说
    /// 捕获作用域本来就用不上)。
    ///
    /// 给 `[Sys]` 那套用:`CreateDelegate` 出来的委托**没法像编译器生成的闭包那样被 JIT 去虚化**,
    /// 再叠一层 lambda 就是白白多一跳。实测(40 万次 `PathClean`)每次调用差 80ns 左右 ——
    /// 换掉这一层就又平了。</summary>
    public static FunctionVal Direct(Func<RuntimeValue, RuntimeValue> f)
        => new(null!, (_, a) => f(a)) { Body = f };

    /// <summary>二元版(柯里化、半成品标记同 <see cref="From(Func{RuntimeValue, RuntimeValue, RuntimeValue})"/>)</summary>
    public static FunctionVal Direct(Func<RuntimeValue, RuntimeValue, RuntimeValue> f)
        => Direct(a1 => Partial(a2 => f(a1, a2)));

    /// <summary>三元版</summary>
    public static FunctionVal Direct(Func<RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue> f)
        => Direct(a1 => Partial(a2 => Partial(a3 => f(a1, a2, a3))));

    /// <summary>二元内置函数,柯里化:f a b ≡ (f a) b。**只给了第一个实参时交回的那个标成半成品**
    /// (它确实还等着第二个);给全了的那个不标 —— 它是完整的。</summary>
    public static FunctionVal From(Func<RuntimeValue, RuntimeValue, RuntimeValue> f)
        => From(a1 => Partial(a2 => f(a1, a2)));

    /// <summary>三元版（if 用：if {c} {t} {e} ≡ ((if c) t) e）——
    /// 前两次交回的都还等着实参,照标。</summary>
    public static FunctionVal From(Func<RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue> f)
        => From(a1 => Partial(a2 => Partial(a3 => f(a1, a2, a3))));

    /// <summary>一个**半成品**内置函数(见 <see cref="IsPartial"/>)</summary>
    private static FunctionVal Partial(Func<RuntimeValue, RuntimeValue> f)
        => new(null!, (_, a) => f(a)) { IsPartial = true };

    /// <summary>前置执行一个块，再执行本函数（参数原样转发）→ Compose 控制帧</summary>
    public FunctionVal Prepend(BlockVal prefix) => new ComposeVal(this, prefix, true);

    /// <summary>执行本函数后，追加执行一个块 → Compose 控制帧</summary>
    public FunctionVal Append(BlockVal suffix) => new ComposeVal(this, suffix, false);
}
