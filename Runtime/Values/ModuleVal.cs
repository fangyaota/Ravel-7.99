namespace Ravel.Runtime;

/// <summary>Ravel 模块实例。
///
/// **成员表和模块作用域是同一个 <see cref="ObjectVal.Scope"/>** —— 模块的成员本来就住在
/// 那个作用域里(`Fields ()` 和成员读取一直都是这么认的),所以直接把它交给基类当成员表,
/// 不另开一张空表、也不再另起一个 `ModuleScope` 的名字:一个对象两个名字,读到哪一处
/// 都得想一想"这是不是同一个东西"。
///
/// 注意"模块不是类":`IsClass` 走的是**元类**链(模块类的 parent 链是
/// `ModType → Ravel → Object`,够不着 `type`),所以模块里就算有个叫 `parent` 的变量,
/// 也不会把模块变成类。</summary>
public record ModuleVal : ObjectVal
{
    /// <param name="modType">模块的类对象(`ClassType`)。</param>
    /// <param name="moduleScope">模块作用域,直接当成员表。</param>
    public ModuleVal(ClassVal modType, Scope moduleScope)
        : base(modType, moduleScope) { }

    public override string ToString() => $"<module {ClassType.DisplayName}>";
}
