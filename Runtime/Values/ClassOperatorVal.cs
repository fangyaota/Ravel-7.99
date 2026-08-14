namespace Ravel.Runtime;

/// <summary>类运算符方法值(_methods 标记):调用时推 ClassOp 控制帧,动态找实例 operatorX 字段并调</summary>
public sealed record ClassOperatorVal(string OpName) : FunctionVal(null!, (_, _) => new Done(VoidVal.Instance));

/// <summary>绑定的类运算符(a.operator+ 的值):调用时推 ClassOp 帧</summary>
public sealed record BoundClassOp(ObjectVal Self, string OpName) : FunctionVal(null!, (_, _) => new Done(VoidVal.Instance));
