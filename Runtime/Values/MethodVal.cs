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

/// <summary>绑定的**槽运算符**(`by + := property g s`):调用时推 TraitOp 帧。
///
/// 和 <see cref="BoundClassOp"/> 的区别在于那一格的**值是 property**:用它的运算符要两级 ——
/// 先读槽(推 getter)拿到运算符函数,再拿那个函数收右操作数。所以它单独占一个帧,
/// 别去挤 ClassOp 那条(那边一级就够,但两条路的阶段数不一样,合在一起就得靠旗子分辨)。
///
/// 槽在哪儿由 TraitOp 帧自己重新找(自己的那层 → 生效中的接口实现),和 ClassOp 一个路子。</summary>
public sealed record BoundTraitOp(ObjectVal Self, string OpName) : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("BoundTraitOp"))
{
    public override string ToString() => "<function " + OpName + ">";
}

/// <summary>类型查询的两个方向(名字对应 `Type` 上那两个方法):
/// <see cref="Implements"/> = "这个类型现在实现了哪些接口",
/// <see cref="Implementors"/> = "哪些类型现在实现了这个接口"。</summary>
public enum TraitQueryKind { Implements, Implementors }

/// <summary>`T.GetImplements ()` / `I.GetImplementors ()` 挂在 `Type` 上的那个成员值。
/// 和 <see cref="ClassOperatorFactory"/> 一个路子:读出来先绑接收者(得到
/// <see cref="BoundTraitQuery"/>),调用时由 `CallInto` 认出它当场算。
///
/// 为什么不能像别的方法那样写个纯 C# 闭包:**这两个问题都要当前作用域** ——
/// 实现是登记在作用域里的(见 BuiltinClasses.Interfaces.cs),而内置方法的体拿不到解释器。</summary>
public sealed record TraitQuery(TraitQueryKind Kind)
    : FunctionVal(null!, (_, self) => new BoundTraitQuery((ObjectVal)self, Kind)), ISelfBinding
{
    /// <summary>成员名(和别的成员一样挂在 `Name` 上,报错和显示要用)</summary>
    public TraitQuery(string name, TraitQueryKind kind) : this(kind) => Name = name;
}

/// <summary>绑好的那个查询(调用时的值):`CallInto` 见着它就按方向去列。</summary>
public sealed record BoundTraitQuery(ObjectVal Self, TraitQueryKind Kind)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("BoundTraitQuery"))
{
    public override string ToString()
        => Kind == TraitQueryKind.Implements ? "<function GetImplements>" : "<function GetImplementors>";
}
