namespace Ravel.Runtime;

/// <summary>用户 lambda:调用时推 body 帧(不再用闭包 rawBody)。Scope=捕获的闭包作用域;Block=lambda 体</summary>
public sealed record LambdaVal(string ParamName, RuntimeType ParamType, BlockExpr Block)
    : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    // record 自动生成的 ToString 会把 Body/Scope/Block 这些实现细节全 dump 出来,
    // 盖掉基类的 <function>。显式盖回去。
    public override string ToString() => base.ToString();
}
