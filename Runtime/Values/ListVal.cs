namespace Ravel.Runtime;

/// <summary>非原子值 —— 有**自己的成员表**(见 <see cref="ObjectVal"/>),所以继承 `ObjectVal`。
/// 每个实例一张**自己的**空表:既能挂成员(`l.tag := 1`),又不会像"共用类型那张表"那样
/// 一改就改掉整个类型。相等性随 `ObjectVal` 走身份比较(容器本来就是按身份算的)。
///
/// 不是位置记录:`members` 是**喂给基类的成员表**,不是这个值的数据字段
/// (数据字段只有 `Elements`),所以不让它变成公开的记录属性。</summary>
public record ListVal : ObjectVal
{
    public List<RuntimeValue> Elements { get; init; }

    /// <param name="members">成员表。`with`/`Copy` 要给副本一张**拷好的**表,
    /// 否则块里 `tag = …` 那种赋值找不到成员(成员表是空的)。</param>
    public ListVal(List<RuntimeValue> elements, Scope? members = null)
        : base(BuiltinClasses.List, members)
        => Elements = elements;

    public override string ToString() => ShowDepth.Guard(() => "[" + string.Join(" ", Elements) + "]");
}
