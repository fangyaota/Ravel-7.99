namespace Ravel.Runtime;

/// <summary>prepend/append 合成的函数:先跑块再调原函数(或反过来)。调用时推 Compose 控制帧</summary>
public sealed record ComposeVal(FunctionVal Original, BlockVal Block, bool IsPrepend)
    : FunctionVal(null!, (_, _) => VoidVal.Instance);
