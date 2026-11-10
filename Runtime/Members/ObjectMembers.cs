namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

/// <summary>**`Object` 这个类给它的实例准备什么。**
///
/// 每个类型自己管自己那份:要加一条成员,就在**这个类**里加一个静态方法、挂上特性
/// (`[ClassMethod]` / `[ClassOp]` / `[ClassProperty]` / `[ClassCtor]`)—— 和插件
/// (`PluginApi`)是同一套写法、同一套绑定,没有第二种。
///
/// 名字和造法由 <see cref="MemberSet.Scan"/> 反射一次收齐;读到一个成员时
/// `VirtualScope` 问的是**类型自己**(`ClassVal.Members`),不经过任何外置注册表。
///
/// 子类那份(`StringMembers` / `ListMembers` …)把 `Set` 的父指到这儿,于是一条成员
/// 只写一遍:`String` 的实例照样有 `ToString` / `Copy` / `Fields` / `Key`。</summary>
internal static class ObjectMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ObjectMembers));

    /// <summary>`print` 出来的那个样子。子类在自己类体里写一条 `ToString` 就把它盖掉。</summary>
    [ClassMethod("ToString")]
    private static RuntimeValue ToString(RuntimeValue self) => new StringVal(self.ToString()!);

    /// <summary>浅拷:容器拷一份、对象拷一张表(`BuiltinClasses.CopyValue`)。</summary>
    [ClassMethod("Copy")]
    private static RuntimeValue Copy(RuntimeValue self) => BuiltinClasses.CopyValue(self);

    /// <summary>这个值**有哪些成员名**(已造出来的 + 类型本来有的),按顺序。</summary>
    [ClassMethod("Fields")]
    private static RuntimeValue Fields(RuntimeValue self)
        => new ListVal([.. self.MemberScope.MemberNames.Select(n => (RuntimeValue)new StringVal(n))]);

    /// <summary>**成员表本身**,交回一个 `Scope` 值 —— 有它才能按**动态名字**读写成员:
    ///     (o.MemberScope ()).Lookup n     读
    ///     (o.MemberScope ()).Define n t   挂
    /// 给的是**那个值自己的那张表**(`ObjectVal.Scope`);原子值自己没有表,退回成员表。
    /// 和 `Scope ()` 不是一回事:那个是捕获作用域(函数独有,类对象给空 Scope)。</summary>
    [ClassMethod("MemberScope")]
    private static RuntimeValue MemberScope(RuntimeValue self)
        => new ScopeVal(self is ObjectVal o ? o.Scope : self.MemberScope);

    /// <summary>谁大谁小 —— 库里的 `IComparable`(`lib/sorting.rav`)架在这条上。</summary>
    [ClassMethod("CompareTo")]
    private static RuntimeValue CompareTo(RuntimeValue self, RuntimeValue arg)
        => IntVal.Of(BuiltinClasses.Less(self, arg) ? -1 : BuiltinClasses.Less(arg, self) ? 1 : 0);

    /// <summary>`Key` —— "我当键时等于什么"。默认只对值类型有话说:交回**它自己**;
    /// 别的类型当场报错(对象没有天然的"值形式",要当键就自己写一条 `Key`)。</summary>
    [ClassMethod("Key")]
    private static RuntimeValue Key(RuntimeValue self)
        => BuiltinClasses.CanBeKey(self.Type)
            ? self
            : throw new RuntimeException(
                $"Key: {self.Type} 没有默认的键（只有按值比的那些有：数 / 串 / 字符 / 布尔 / `()`）。"
                + "要拿它当键，就在类里写一条 Key，交回一个值类型的键", ErrorKind.Type);
}

/// <summary>**`Ravel` 这个类给它的实例(所有模块)准备什么。**
///
/// 模块是 `Ravel` 的实例,所以这一条每个模块都有:交回**这个模块自己写着的那些 `using`**,
/// 而且是模块本身、不是那串路径(见 `ModuleVal.ReferencePaths`)。</summary>
internal static class ModuleMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ModuleMembers), ObjectMembers.Set);

    [ClassMethod("References")]
    private static RuntimeValue References(RuntimeValue self)
        => new ListVal(self is ModuleVal { Owner: { } engine } m
            ? [.. engine.ResolveReferences(m)]
            : []);
}
