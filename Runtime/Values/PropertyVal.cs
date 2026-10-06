namespace Ravel.Runtime;

/// <summary>Property = getter + setter 对。
///
/// 非原子值 —— 有**自己的成员表**,见 <see cref="ObjectVal"/>。
///
/// `Get` / `Set` 两个函数**不在成员表里**:它们是 `BoxedValue.GetMember` 对这两个名字的
/// 特判(见 <see cref="ObjectVal.GetterMember"/>),所以 `p.Get ()` 拿得到、写不出。</summary>
public record PropertyVal : ObjectVal
{
    public FunctionVal Getter { get; init; }
    public FunctionVal Setter { get; init; }

    /// <summary>这个属性值是从哪个变量上取的(`scope.Lookup` / `Variables` 交出来的那些)。
    ///
    /// **attrs 只存一份,在 <see cref="Variable"/> 上。** 从前这里另存一个 `List<string>`
    /// 副本,于是 `Attrs ()` 说的和门禁执行的可能不是一回事(副本是取值那一刻的快照)。
    /// 裸的 `property g s`(还没挂到任何变量上)这里是 null,`Attrs ()` 回空表 ——
    /// 和从前那个"没传 attrs 就回空表"的行为一致。</summary>
    public Variable? Var { get; init; }

    public PropertyVal(FunctionVal getter, FunctionVal setter, Variable? var = null, Scope? members = null)
        : base(BuiltinClasses.Property, members)
    {
        Getter = getter;
        Setter = setter;
        Var = var;
    }

    public override string ToString() => "<property>";
}
