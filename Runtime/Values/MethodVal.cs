namespace Ravel.Runtime;

/// <summary>内置同步方法/运算符值:self → (arg → impl(self, arg))。分派走快速同步路径(非此即走 CallInto)</summary>
public sealed record BuiltinMethodVal(Func<RuntimeValue, RuntimeValue, RuntimeValue> Impl)
    : FunctionVal(null!, (_, self) => FunctionVal.From(arg => Impl(self, arg))), ISelfBinding;

/// <summary>`call` 成员的值 —— **新建实例的责任**。自绑定:读出来要绑上接收者,
/// 绑完是 <see cref="BoundCall"/>,`CallInto` 见到它就走实例化那条路。
/// 所有类对象装的都是它,只是绑的 self 不同(`C ()` 拿 C;`type { body }` 拿 type)。</summary>
public sealed record ClassCallFactory()
    : FunctionVal(null!, (_, self) => new BoundCall((ObjectVal)self)), ISelfBinding;

/// <summary>类运算符工厂:存的是"self → BoundClassOp",读成员时要绑接收者。
/// 和 BuiltinMethodVal 一样是自绑定成员,区别是它不直接算而是推 ClassOp 帧到实例里找实现。</summary>
public sealed record ClassOperatorFactory(string OpName)
    : FunctionVal(null!, (_, self) => new BoundClassOp((ObjectVal)self, OpName)), ISelfBinding;

/// <summary>绑定的类运算符(a.+ 的值):调用时推 ClassOp 帧,到实例里按符号名找实现</summary>
public sealed record BoundClassOp(ObjectVal Self, string OpName) : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    /// <summary>盖掉 record 的自动 dump(否则会把 Self 整个对象打出来)</summary>
    public override string ToString() => "<function " + OpName + ">";
}
