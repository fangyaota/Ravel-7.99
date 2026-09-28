namespace Ravel.Runtime;

/// <summary>callcc 续延:捕获帧链引用,调用时还原并塞结果(持久帧→多发射天然支持)。调用走 CallInto 的 ContinuationVal 分支。
///
/// **控制状态(handler 栈、模块加载栈)不在这里** —— "续延被调时该还原什么"是**策略**,
/// 由 predefined.rav 里 `callcc` 那层包装定;引擎只提供 `System.ControlState` /
/// `System.RestoreControl` 两个原语。</summary>
public sealed record ContinuationVal(Frame Captured)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("ContinuationVal"))
{
    /// <summary>盖掉 record 的自动 dump(否则会把整条帧链打出来)</summary>
    public override string ToString() => "<continuation>";
}
