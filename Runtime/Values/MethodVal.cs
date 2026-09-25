namespace Ravel.Runtime;

/// <summary>内置同步方法/运算符值:self → (arg → impl(self, arg))。分派走快速同步路径(非此即走 CallInto)</summary>
public sealed record BuiltinMethodVal(Func<RuntimeValue, RuntimeValue, RuntimeValue> Impl)
    : FunctionVal(null!, (_, self) => FunctionVal.From(arg => Impl(self, arg)));

/// <summary>绑定的类运算符(a.+ 的值):调用时推 ClassOp 帧,到实例里按符号名找实现</summary>
public sealed record BoundClassOp(ObjectVal Self, string OpName) : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    /// <summary>盖掉 record 的自动 dump(否则会把 Self 整个对象打出来)</summary>
    public override string ToString() => "<function " + OpName + ">";
}
