namespace Ravel.Runtime;

/// <summary>绑定了 self 的 `call` 成员:调用它 = 造 self 的实例（self 是类对象时）。
/// 和 <see cref="BoundClassOp"/> 一个路子——靠 `CallInto` 分派，自身不驱动求值。</summary>
public sealed record BoundCall(ObjectVal Self) : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    /// <summary>盖掉 record 的自动 dump（否则会把 Self 整个对象打出来）</summary>
    public override string ToString() => "<function call>";
}
