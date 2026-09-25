namespace Ravel.Runtime;

/// <summary>callcc 续延:捕获帧链引用,调用时还原并塞结果(持久帧→多发射天然支持)。调用走 CallInto 的 ContinuationVal 分支</summary>
public sealed record ContinuationVal(Frame Captured) : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    /// <summary>盖掉 record 的自动 dump(否则会把整条帧链打出来)</summary>
    public override string ToString() => "<continuation>";
}
