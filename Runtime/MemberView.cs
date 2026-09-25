namespace Ravel.Runtime;

/// <summary>成员表视图 —— 「**这个值**能读到哪些成员」的唯一入口。
///
/// 两段,顺序固定:
/// 1. <c>own</c>:值**自己**那层(对象是它的实例作用域)。对象是扁平的,只查这一层。
///    原子值(IntVal/StringVal/…)没有这一层,传 null —— 这正是"伪 Scope":
///    它不落地成字段,所以 `IntVal(1) == IntVal(1)` 依旧成立。
/// 2. 沿**类对象的 parent 链**兜底:方法住在类那层,`1.Fields ()` 的 `Fields`
///    就在 `Integer → ValueType → Object` 链上的 `Object` 那层。
///
/// 第 2 段**只认方法名**(<see cref="ObjectVal.IsMethodName"/>)且只收 `FunctionVal`:
/// 类对象的表里还躺着 `parent`/`block`/`name`/`this`/`init` —— 那些是**类自己的数据**,
/// 不是"这个值的成员"。不挡的话 `(5).parent` 会从报错变成返回 `ValueType`。
///
/// 只读:定义/赋值一律拒绝。往这儿写等于把字段挂到整个类型上。
///
/// 它不是 `Scope` 的词法链:视图的 `Parent` 是 null,`Variables` 是空的 ——
/// 它只覆写 `LookupField` 这一个动作。</summary>
internal sealed class MemberView(Scope? own, ObjectVal type) : Scope
{
    public override Variable? LookupField(string name)
        => own?.LookupField(name) ?? LookupInClassChain(name);

    /// <summary>沿类对象的 parent 链找方法。自引用(`object`/`Every`/`Any` 的 parent 是自己)就地停。</summary>
    private Variable? LookupInClassChain(string name)
    {
        if (!ObjectVal.IsMethodName(name)) return null;
        for (var t = type; t != null; t = t.Parent)
        {
            var vr = t.Scope.LookupField(name);
            if (vr?.Value is FunctionVal) return vr;
            if (t.Parent == t) break;      // 链到头
        }

        return null;
    }

    private static RuntimeException ReadOnly()
        => new("值类型的成员只读（它们的成员表是借类那层的，写进去等于改掉整个类型）");

    public override Variable Define(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
        => throw ReadOnly();

    public override Variable DefineOrReplace(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
        => throw ReadOnly();

    public override void Assign(string name, RuntimeValue value) => throw ReadOnly();
}
