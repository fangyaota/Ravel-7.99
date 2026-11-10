namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`Type`(也就是一个类对象)给它的实例准备什么。**
///
/// **父是 `FunctionMembers.Set`,不是 `Object`** —— 类树里写的是 `Type : Function`
/// (`Link(Type, Function, Type)`),所以类对象本来就该有 `Name` / `Scope` / `Body` /
/// `SetScope` / `Prepend` / `Append`。父指错了,`C.name`、`C.body` 这一批就静默消失
/// (旧注册表还兜着的时候看不出来,迁完之后立刻显形)—— 这里按类树来。
///
/// 这一层**没有**(而且是故意删掉的)几条,原因记在这儿,免得有人又想加回来:
/// - `name` **方法**:类名现在是类对象 Scope 里的 `name` 成员(见 `ObjectVal.Name`),
///   和它同住一个作用域的方法会把它覆盖掉。而且那个方法本来就是死代码 ——
///   `GetMember` 的 `name` 伪成员排在方法查找之前。读走伪成员、写走成员赋值,行为不变。
/// - `Parent ()` **方法**:与 `parent` 成员是同一格数据(`C.parent` 和 `C.Parent ()`
///   都返回 `Object`),留两个名字只会让人以为一个给类、一个给实例。要读父类就是
///   `C.parent`(机制成员,类自己那层;实例上读不到 —— 它不沿链继承)。
/// - `Is` **方法**:它问类型、`is` 问值,两个长得像的东西各管一头,最常踩的是拿实例去调
///   (`C.Is (C ())` 从前静默给 false,读起来还像"这个实例是不是 C")。要问类型之间的关系
///   就用运算符(`int <: object` / `object :> int`);要问值就用 `x is T`。
///
/// `GetImplements` / `GetImplementors` 挂的是 `TraitQuery`(`SeqMethod` 那个家族)——
/// 它们不是普通的 `BuiltinMethodVal`,真正算它们的是求值器(实现登记在作用域里),
/// `[ClassMethod]` 只能绑 `MethodInfo`,表达不了那种值 —— 所以走 `[ClassValue]`
/// (字段里那个现成的值直接当成员交出去)。挂的位置没变:还是 `Type` 给实例的一格,
/// `Option.GetImplements ()` 里的 `Option`(是 `type` 的实例)沿 `parent` 往上兜到这一张。</summary>
internal static class TypeMembers
{
    // **声明必须排在 `Set` 之前** —— `Set` 是 `MemberSet.Scan` 靠反射读这两格建的,
    // 字段还没初始化就读到 null 了。
    [ClassValue("GetImplements")]
    private static readonly RuntimeValue GetImplements = new TraitQuery("GetImplements", TraitQueryKind.Implements);

    [ClassValue("GetImplementors")]
    private static readonly RuntimeValue GetImplementors = new TraitQuery("GetImplementors", TraitQueryKind.Implementors);

    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(TypeMembers), FunctionMembers.Set);

    /// <summary>`int default` 之类:这个类型的"没给值时的那个值",由 `ConvertDirect` 说了算
    /// (它同时管 `function default` 给空函数、`bool default` 给 false 那几条)。</summary>
    [ClassMethod("Default")]
    private static RuntimeValue Default(RuntimeValue self) => BuiltinClasses.ConvertDirect((ObjectVal)self, DefaultVal.Instance);

    /// <summary>这个类型的**全部直接与间接子类**(不含自己)。跑一遍内建类的总表
    /// (`AllTypes`)用 `IsAssignableTo` 问 —— 子类关系就是类树本身,没有别处可查。</summary>
    [ClassMethod("Subtypes")]
    private static RuntimeValue Subtypes(RuntimeValue self)
    {
        var t = (ObjectVal)self;
        var subs = new List<RuntimeValue>();
        foreach (var sub in BuiltinClasses.AllTypes)
        {
            if (sub != t && sub.IsAssignableTo(t))
                subs.Add(sub);
        }

        return new ListVal(subs);
    }
}
