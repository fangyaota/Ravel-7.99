namespace Ravel.Runtime;

/// <summary>callcc 续延:捕获帧链引用,调用时还原并塞结果(持久帧→多发射天然支持)。调用走 CallInto 的 ContinuationVal 分支。
///
/// **控制状态分两半,归属也分两半**:
/// * **模块加载栈**是**引擎的家事** —— `System.CallCC` 捕获时拍一份
///   (<see cref="Loading"/>),续延被调时引擎自己盖回去,库不必管;
/// * **handler 栈**是**库的状态**(就在 Ravel 里那个 list 上)—— 由 predefined.rav 里
///   `callcc` 那层包装自己拍自己还原,引擎不掺和。
///
/// 它是**自己的类型**(`Continuation &lt;: Function`)。续延本来就"是个函数" —— 能调、
/// 能存进字段、能当参数传,所以父类是 `Function`(`is function` 照旧成立)。但 `typeof`
/// 该说出它到底是什么,而不是含糊地报 `Function`:于是 `Continuation` 是这个语言里
/// 继 `Block` 之后第二个"函数的子类型"。**要和普通函数区分开的地方一律用 `is`** ——
/// `typeof k == function` 在续延上不再成立(它现在报 `Continuation`)。
///
/// 两枚字段对应两种来源:
/// <list type="bullet">
/// <item><see cref="Captured"/> —— `System.CallCC` 直接交出来的那一枚:调它 = 丢弃当前帧链、
/// 跳回捕获点。引擎自己只造这一种。</item>
/// <item><see cref="Jump"/> —— 用 `Continuation f` **包**出来的那一枚:调它 = 调那枚函数
/// (走普通调用那条路)。库里的 `callcc` 靠它把"先还原控制状态、再跳回去"那层也做成
/// 续延 —— 于是用户手里那枚**类型上**就是 `Continuation`,而不是一个说不清的 lambda。</item>
/// </list></summary>
public sealed record ContinuationVal(Frame? Captured, FunctionVal? Jump = null)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("ContinuationVal"))
{
    /// <summary>"还没到手的那一枚" —— `default` / `Continuation.Default ()` 给的就是它。
    ///
    /// 它**一调就报错**(见 CallInto 那一臂):续延调用是"跳到别处去",而这一枚没有可以
    /// 去的地方。做成"空操作"是不行的 —— 那样控制权静静留在原地、调用方还以为跳过了,
    /// 而这正是它当哨兵用的场合(生成器里"还没起跑")最不该静的地方。</summary>
    public static readonly ContinuationVal Default = new((Frame?)null);

    /// <summary>捕获那一刻**引擎自己那份控制状态**(模块加载栈)的快照。
    ///
    /// **由引擎拍、也由引擎还原** —— 续延被调时,`CallInto` 先把它盖回去再跳。
    /// 从前这是库里 `callcc` 的活(每次调用都喊一遍 `System.LoadingState` / `RestoreLoading`),
    /// 而那两次喊是**每次调用续延**都跑的:循环里就是每轮,一次内置调用 ≈ 0.65 µs,
    /// 占一轮 `while` 的 11%。搬进引擎之后那次判断是 C# 里一句比较,不要钱。
    ///
    /// **handler 栈不在这儿** —— 那是**库**的状态(就在 Ravel 里那个 list 上),
    /// 库自己拍、自己还原,引擎照样不掺和(见 `lib/predefined.rav` 的 `callcc`)。
    ///
    /// `null` = 这一枚不是 `System.CallCC` 交出来的(`Continuation f` 包出来的那种,
    /// 它只转发给里面那枚;还有 `Default`),那就没什么可还原的。</summary>
    public RuntimeValue? Loading { get; init; }

    /// <summary>是"还没到手"的那一枚吗</summary>
    public bool IsDefault => Captured is null && Jump is null;

    /// <summary>盖掉 record 的自动 dump(否则会把整条帧链打出来)</summary>
    public override string ToString() => IsDefault ? "<续延 default>" : "<continuation>";

    /// <summary>元类是 `Continuation`。
    ///
    /// 直接交回、不靠 `ClassType ??=` 懒回填:基类 ctor 已经把 `ClassType` 填成了 `Function`,
    /// 那个 `??=` 在这里根本不会生效(`bool` 那边踩的是同一个坑)。</summary>
    public override ObjectVal Type => BuiltinClasses.Continuation;
}
