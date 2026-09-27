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

/// <summary>`T.GetImplements ()` 挂在 `Type` 上的那个成员值。和 <see cref="ClassOperatorFactory"/>
/// 一个路子:读出来先绑接收者,拿到 <see cref="BoundImplementsQuery"/>,调用时由 `CallInto` 认出它
/// 当场算。
///
/// 为什么不能像别的方法那样写个纯 C# 闭包:**"这个类型现在实现了哪些接口"要当前作用域**
/// ——实现是登记在作用域里的(见 BuiltinClasses.Interfaces.cs),而内置方法的体拿不到解释器。</summary>
public sealed record ImplementsQuery() : FunctionVal(null!, (_, self) => new BoundImplementsQuery((ObjectVal)self)), ISelfBinding
{
    /// <summary>成员名(和别的成员一样挂在 `Name` 上,报错和显示要用)</summary>
    public ImplementsQuery(string name) : this() => Name = name;
}

/// <summary>绑好的 `T.GetImplements`(调用时的值):`CallInto` 见着它就把 `Self` 实现了的接口列出来。</summary>
public sealed record BoundImplementsQuery(ObjectVal Self) : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("BoundImplementsQuery"))
{
    public override string ToString() => "<function GetImplements>";
}
