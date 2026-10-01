namespace Ravel.Runtime;

/// <summary>运行时值的抽象基类</summary>
public abstract record RuntimeValue
{
    /// <summary>这个值的类对象(创建它的那个类;类对象的"类"就是它的元类)。`typeof X` 取的就是它。</summary>
    public abstract ObjectVal Type { get; }

    /// <summary>这是个真的闭包(方法 / lambda / 内置函数 / 块),而不是**恰好**
    /// 落在 Function 类型下的数据值吗?
    ///
    /// **判据不能用 `is FunctionVal`**,有两个反例:
    /// - 类型表里 `Bool &lt;: Function`——true/false 可调用,`true {a} {b}` 选一个块跑——
    ///   所以 BoolVal 也是 FunctionVal。它那个 Body 只是占位、从不被调用
    ///   (求值器在 CallInto 里按类型先分派掉了)。
    /// - 类对象也是 FunctionVal(见 <see cref="ClassVal"/>)。它是**数据**:`C := class {…}`
    ///   建出来的那个类必须原样交出去,不能被当成"还差参数的构造器"再包一层。
    ///
    /// 凡是需要区分「方法 vs 数据字段」「还差参数的构造器 vs 返回值」的地方都得走这个属性,
    /// 否则 bool 会被当成方法(字段从 `Fields ()`/`print obj` 里消失),
    /// `init := () => { true; }` 的对象会被当成半成品构造器交出去,
    /// 而 `class {…}` 拿到的会是一个 PartialCtor 而不是类。</summary>
    public bool IsClosure => this is FunctionVal and not (BoolVal or ClassVal);

    /// <summary>取成员的唯一入口 —— 「我作为一个对象」的那半边:我身上挂着哪些成员。
    /// 一次 <see cref="Scope.LookupField"/> 走完"自己那层 → 沿类对象的 parent 链兜底",
    /// 细节在 <see cref="MemberView"/>。
    ///
    /// **不是 `FunctionVal.CaptureScope`(捕获作用域)**,两个概念不同、字段也分开:
    /// 捕获作用域是 lambda 定义处的词法作用域,闭包靠它(调用时 Push、`Copy` 时重绑);
    /// 成员作用域是"这个值能读到哪些成员"。混成一个字段的话,给函数挂个成员就会写进
    /// 它捕获的那个作用域,泄漏给共用该作用域的其它闭包、甚至漏进外围局部变量。
    ///
    /// 默认实现是**伪 Scope** —— 原子值(`IntVal`/`DefaultVal`/…)自己没有成员表,
    /// 借的是 `Type` 那条链,而那是算出来的视图、不落地。它只读:
    /// `HasOwnMembers` 为假时写路径会把写入挡回去,免得"挂到借用来的表上"等于改掉整个类型。</summary>
    public virtual Scope MemberScope => Type.InstanceMembers;

    /// <summary>这个值有**自己**的成员表吗?有才能往它身上写成员
    /// (对象有;原子值和借类成员表的那些没有)。</summary>
    public virtual bool HasOwnMembers => false;
}

/// <summary>引擎报的错**属于哪一族** —— 决定它交给 Ravel 那边时是**哪一类异常**
/// (见 <c>BuiltinClasses.ErrorClass</c> 与 `Runtime/BuiltinClasses.Errors.cs`)。
///
/// 那 11 个类是**内置类**(和 `Exception` 一样在 C# 侧建):报错的是引擎,分类自然也该由
/// 引擎说了算 —— 库里再定义一遍就成了两处各管一半。用户在 Ravel 里照样能继承它们、
/// `throw (TypeError "自己造的")`、按类型接:
///
///     try { 1 + "a"; } (e: TypeError) => { … }
///
/// 默认 `Error` = 还没归类的一律落基类 `Exception`(分类可以慢慢补,不必一次到位)。</summary>
public enum ErrorKind
{
    Error,
    Type,
    Name,
    Attribute,
    Index,
    Key,
    ZeroDivision,
    Assert,
    Argument,
    Value,
    Io,
    Regex,
}

/// <summary>Ravel 运行时错误。抛出点只管给消息(和**哪一族**)——位置和调用栈由求值器在
/// 异常冒泡到 `StepOnce` 时补上(见 <c>Interpreter.Locate</c>),所以 180 多处 throw
/// 不用各自操心这些。</summary>
public class RuntimeException(string message, ErrorKind kind = ErrorKind.Error) : Exception(message)
{
    /// <summary>哪一族 —— Ravel 那边接住它的是哪一类异常(见 <see cref="ErrorKind"/>)。</summary>
    public ErrorKind Kind { get; } = kind;

    /// <summary>出错位置所在的源文件(沿帧链找到最近一个有 Source 的块)</summary>
    public string? File { get; internal set; }
    public int Line { get; internal set; }
    public int Column { get; internal set; }

    /// <summary>Ravel 层的调用栈,最近的在最前,形如 "file:line 在 <块 12:4> 里"</summary>
    public IReadOnlyList<string> Trace { get; internal set; } = [];

    /// <summary>已经补过位置了——内层 StepOnce 补完,外层就不会覆盖成更外侧的位置</summary>
    internal bool Located => Line != 0;
}

/// <summary>参数类型不匹配(lambda 参数检查失败,供 | 交替 / 多 init 捕捉)。
/// 消息由抛出点给全(哪个参数、要什么、得到什么)——从前它固定是「类型不匹配」,
/// 于是 `M { ... }` 这类调用失败时只看到四个字,完全不知道错在哪。
///
/// 归**类型错误**那一族:它冒到用户眼前时说的确实是"类型不对"
/// (交给用户看的只有没被任何分支/`try` 接住的那些)。</summary>
public sealed class TypeMismatchException(string message) : RuntimeException(message, ErrorKind.Type);

/// <summary>exit 专用异常——不被 EvalCall 捕获，直接向上抛出</summary>
public class ExitException(string message) : Exception(message);

/// <summary>错误在源码里的位置。运行时错误和语法错误都要报位置、都要画那行源码和插入符,
/// 所以位置单独成一个值,渲染只有一份实现(见 <see cref="ErrorReport"/>)。</summary>
/// <param name="File">源文件名;null / 空 表示 eval、REPL 这类没有真实文件的片段</param>
public sealed record SourceSpot(string? File, int Line, int Column);

/// <summary>语法/词法错误:源码本身写错了,不是程序跑出来的。
/// 位置来自 token、也没有 Ravel 调用栈可讲——所以它和 <see cref="RuntimeException"/> 分开:
/// 以前它只是个裸的 System.Exception,于是调用方分不清「用户代码写错了」和「解释器有 bug」,
/// 测试运行器更是把两者都当普通的 Error 放行。</summary>
public sealed class SyntaxException(string message, SourceSpot spot) : Exception(message)
{
    public SourceSpot Spot { get; } = spot;
}
