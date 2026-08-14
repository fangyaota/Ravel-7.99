namespace Ravel.Runtime;

/// <summary>用户 lambda:显式栈求值器调用时推 body 帧(不再用闭包 rawBody)。Scope=捕获的闭包作用域</summary>
public sealed record LambdaVal(string ParamName, RuntimeType ParamType, BlockExpr Body)
    : FunctionVal(null!, (_, _) => new Done(VoidVal.Instance));
