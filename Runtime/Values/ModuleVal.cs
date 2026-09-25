namespace Ravel.Runtime;

/// <summary>Ravel 模块实例。
///
/// 非原子值 —— 成员表就是**模块作用域**(模块的成员本来就住在那里,`Fields ()` 和
/// 成员读取一直都是这么认的),所以直接把它交给基类当成员表,不另开一张空表。
///
/// 注意"模块不是类":`IsClass` 走的是**元类**链(模块类的 parent 链是
/// `ModType → Ravel → Object`,够不着 `type`),所以模块里就算有个叫 `parent` 的变量,
/// 也不会把模块变成类。</summary>
public record ModuleVal : ObjectVal
{
    /// <summary>模块的类对象。</summary>
    public ClassVal ModType { get; init; }

    /// <summary>模块作用域(也是它的成员表)。</summary>
    public Scope ModuleScope { get; init; }

    public ModuleVal(ClassVal modType, Scope moduleScope)
        : base(modType, moduleScope)
    {
        ModType = modType;
        ModuleScope = moduleScope;
    }

    public override string ToString() => $"<module {ModType.Name}>";
}
