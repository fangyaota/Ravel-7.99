namespace Ravel.Runtime;

/// <summary>callcc 续延:捕获帧链引用,调用时还原并塞结果(持久帧→多发射天然支持)。调用走 CallInto 的 ContinuationVal 分支。
///
/// **控制状态(handler 栈、模块加载栈)不在这里** —— "续延被调时该还原什么"是**策略**,
/// 由 predefined.rav 里 `callcc` 那层包装定;引擎只提供 `System.ControlState` /
/// `System.RestoreControl` 两个原语。
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
