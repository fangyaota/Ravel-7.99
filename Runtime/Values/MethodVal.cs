namespace Ravel.Runtime;

/// <summary>内置同步方法/运算符值:self → (arg → Done(impl(self, arg)))。分派走快速同步路径(非此即走 CallInto)</summary>
public sealed record BuiltinMethodVal(Func<RuntimeValue, RuntimeValue, RuntimeValue> Impl)
    : FunctionVal(null!, (_, self) => Interpreter.ToDone(
        FunctionVal.FromTrampolined(arg => new Done(Impl(self, arg)))));

/// <summary>绑定的类运算符(a.operator+ 的值):调用时推 ClassOp 帧,动态找实例 operatorX 字段</summary>
public sealed record BoundClassOp(ObjectVal Self, string OpName) : FunctionVal(null!, (_, _) => new Done(VoidVal.Instance));
