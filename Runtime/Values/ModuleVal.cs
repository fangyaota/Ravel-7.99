namespace Ravel.Runtime;

/// <summary>Ravel 模块实例。
///
/// **成员表和模块作用域是同一个 <see cref="ObjectVal.Scope"/>** —— 模块的成员本来就住在
/// 那个作用域里(`Fields ()` 和成员读取一直都是这么认的),所以直接把它交给基类当成员表,
/// 不另开一张空表、也不再另起一个 `ModuleScope` 的名字:一个对象两个名字,读到哪一处
/// 都得想一想"这是不是同一个东西"。
///
/// **模块不是类,也不是"某种类"**:它的类对象就是 <see cref="BuiltinClasses.Ravel"/> 本身,
/// 每个模块**不再各建一个 `Ravel` 的子类**。从前每建一个模块就 `new ClassVal` 一个
/// —— 那份类对象只为两件事活着:让 `print` 打出 `<module Math>`、让 `typeof` 打出 `Math`。
/// 而它**不登记进 `AllTypes`**(每个 Interpreter 都要重建一份,登记只会累积),
/// 于是类型树底下挂着一堆看不见的子类,树和现实对不上。现在一个类、`Ravel` 一个名字,
/// 那两件事改由 <see cref="Label"/> 担 —— 类型树上就干干净净一个 `Ravel` 了。
///
/// `typeof (Math)` 因此是 `Ravel`(所有模块都一样),`Math <: Ravel` 照旧成立。
/// 注意"模块仍然是**对象**不是类":`IsClass` 走的是**元类**链
/// (`Ravel → Object`,够不着 `type`),所以模块里就算有个叫 `parent` 的变量,
/// 也不会把模块变成类。</summary>
public record ModuleVal : ObjectVal
{
    /// <param name="label">模块名(`ravel "M"` 那个 M)。只用来**显示**(`print (Math)`
    /// 打 `<module Math>`)—— 它是值自己的一个字段,不是类那边的 `name` 成员,
    /// 所以 `Math.name` 还是"没有这个方法"(模块的成员表里本来就没人叫 name)。</param>
    /// <param name="moduleScope">模块作用域,直接当成员表。</param>
    public ModuleVal(string label, Scope moduleScope)
        : base(BuiltinClasses.Ravel, moduleScope) => Label = label;

    /// <summary>模块名</summary>
    public string Label { get; init; }

    /// <summary>报错里要说**用户写的那个名字**(`System.Class` 打错字该报「类型 'System'」,
    /// 不是「类型 'Ravel'」)—— 类对象都指向同一个 `Ravel`,照基类那条走就串了。</summary>
    internal override string DisplayName => Label;

    public override string ToString() => $"<module {Label}>";
}
