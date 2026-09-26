namespace Ravel.Runtime;

/// <summary>内置同步方法/运算符值:self → (arg → impl(self, arg))。分派走快速同步路径(非此即走 CallInto)</summary>
public sealed record BuiltinMethodVal(Func<RuntimeValue, RuntimeValue, RuntimeValue> Impl)
    : FunctionVal(null!, (_, self) => FunctionVal.From(arg => Impl(self, arg))), ISelfBinding;

/// <summary>类运算符工厂:存的是"self → BoundClassOp",读成员时要绑接收者。
/// 和 BuiltinMethodVal 一样是自绑定成员,区别是它不直接算而是推 ClassOp 帧到实例里找实现 ——
/// 所以它**不吃**同步快路径,两个标记因此不能合并(见 <see cref="ISelfBinding"/>)。</summary>
public sealed record ClassOperatorFactory(string OpName)
    : FunctionVal(null!, (_, self) => new BoundClassOp((ObjectVal)self, OpName)), ISelfBinding;

/// <summary>绑定的类运算符(a.+ 的值):调用时推 ClassOp 帧,到实例里按符号名找实现</summary>
public sealed record BoundClassOp(ObjectVal Self, string OpName) : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("BoundClassOp"))
{
    /// <summary>盖掉 record 的自动 dump(否则会把 Self 整个对象打出来)</summary>
    public override string ToString() => "<function " + OpName + ">";
}
