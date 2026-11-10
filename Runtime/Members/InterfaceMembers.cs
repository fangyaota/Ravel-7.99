namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**接口给它的实例(实现)准备什么。**
///
/// 目前只有一条:`Dispose` —— 提前取消一个实现(`myImplement.Dispose ()`,幂等:
/// 实现身上那个代号 +1,登记过的统统作废,见 `BuiltinClasses.DisposeMember` /
/// `BuiltinClasses.DisposeImplementation`)。
///
/// 从前它是在**造接口的那一刻**一条条挂上去的,而且是两处同一份 lambda:
///   * `BuildInterface` 里 `trait.DefineMethod("Dispose", …)` —— 每个接口挂一遍;
///   * `InstallInterfaceInit` 里 `IValue.DefineMethod("Dispose", …)` —— `IValue` 是内置接口、
///     不走 `BuildInterface`,得单独再补一次。
/// 现在挂给 **`BaseInterface`**(所有接口的父):接口是 `Install(scope, BaseInterface, blk)`
/// 造的,`Members` 按引用从父类接下(见 `BuiltinClasses.Install`),于是**每个接口**
/// 的实例都读得到这一条 —— 一处定义就够。
/// (`IValue.Members` 得单独指过来:`Link` 不接 `Members`。)
///
/// 为什么挂在"接口"上而不是"实现对象"上:实现对象的 `Type` 就是**那个接口**(或 `IValue`),
/// 成员是按它的类去找的;而接口是用户随时能新建的东西(`interface Foo { … }`),
/// 没法给每个实现对象单独挂表。
///
/// `Dispose` 是 `ISelfBinding` 的内置方法:读成员时才绑接收者,于是 `self` 就是
/// **拿到的那个实现** —— `Copy ()` / `with` 出来的副本绑的是它自己,不会误伤原件。</summary>
internal static class InterfaceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(InterfaceMembers), ObjectMembers.Set);

    /// <summary>`myImplement.Dispose ()` —— 提前取消这个实现。
    /// 幂等:每调一次它就老一岁,记录里"登记时它几岁"的那些统统作废;在哪个作用域调都一样,
    /// 之后再 `use` 就重新记一条新的。
    /// (名字取自 `BuiltinClasses.DisposeMember`,那儿的常量还留着 —— 别的代码按名字找它。)</summary>
    [ClassMethod(BuiltinClasses.DisposeMember)]
    private static RuntimeValue Dispose(RuntimeValue self)
        => BuiltinClasses.DisposeImplementation((ObjectVal)self);
}
