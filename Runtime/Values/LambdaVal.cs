namespace Ravel.Runtime;

/// <summary>用户 lambda:调用时推 body 帧(不再用闭包 rawBody)。Scope=捕获的闭包作用域;Block=lambda 体</summary>
public sealed record LambdaVal(string ParamName, RuntimeType ParamType, BlockExpr Block)
    : FunctionVal(null!, (_, _) => VoidVal.Instance);
