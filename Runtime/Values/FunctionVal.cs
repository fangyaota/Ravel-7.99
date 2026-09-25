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
    /// 这里允许 `ClassType` 为 null 并**懒回填**:静态初始化期间 `BuiltinClasses.Function`
    /// 还没造出来(`BuiltinClasses.Call` 是字段初始化器,排在静态 ctor 体之前,
    /// 它就是个 `ClassCallFactory : FunctionVal`),ctor 里只能先记 null。
    /// **别在 ctor 里读 `BuiltinClasses.X` 并假定它已就绪。**</summary>
    public override ObjectVal Type => ClassType ??= BuiltinClasses.Function;

    public override string ToString() => Name != null ? "<function " + Name + ">" : "<function>";

    /// <summary>需要捕获作用域时用(captureScope 参与签名,Body 里可读 CaptureScope)</summary>
    public FunctionVal(Scope captureScope, Func<Scope, RuntimeValue, RuntimeValue> rawBody)
        : base(null!, new Scope())
    {
        // 基类 ctor 的 `?? this` 是给根元类 `type` 留的自指,函数得改回 Function。
        ClassType = BuiltinClasses.Function;
        CaptureScope = captureScope;
        Body = a => rawBody(CaptureScope, a);
    }

    /// <summary>单参内置函数(最常见)</summary>
    public static FunctionVal From(Func<RuntimeValue, RuntimeValue> f)
        => new(null!, (_, a) => f(a));

    /// <summary>二元内置函数,柯里化:f a b ≡ (f a) b</summary>
    public static FunctionVal From(Func<RuntimeValue, RuntimeValue, RuntimeValue> f)
        => From(a1 => From(a2 => f(a1, a2)));

    /// <summary>三元版（if 用：if {c} {t} {e} ≡ ((if c) t) e）</summary>
    public static FunctionVal From(Func<RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue> f)
        => From(a1 => From(a2 => From(a3 => f(a1, a2, a3))));

    /// <summary>前置执行一个块，再执行本函数（参数原样转发）→ Compose 控制帧</summary>
    public FunctionVal Prepend(BlockVal prefix) => new ComposeVal(this, prefix, true);

    /// <summary>执行本函数后，追加执行一个块 → Compose 控制帧</summary>
    public FunctionVal Append(BlockVal suffix) => new ComposeVal(this, suffix, false);
}
