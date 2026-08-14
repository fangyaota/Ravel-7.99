namespace Ravel.Runtime;

/// <summary>callcc 续延:捕获帧链引用,调用时还原并塞结果(持久帧→多发射天然支持)</summary>
public sealed record ContinuationVal(Frame Captured) : FunctionVal(null!, (_, _) => new Done(VoidVal.Instance));
