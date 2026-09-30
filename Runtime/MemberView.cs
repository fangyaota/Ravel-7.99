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
/// 第 2 段**只认带 `forInstance` 标记的成员**(<see cref="Attr.ForInstance"/>),外加一条
/// 机制名过滤(<see cref="ObjectVal.IsMethodName"/>):类对象的表里还躺着
/// `parent`/`block`/`name`/`this`/`init` —— 那些是**类自己的数据**,不是"这个值的成员",
/// 不挡的话 `(5).parent` 会从报错变成返回 `ValueType`。
///
/// 从前那半的判据是"值是不是 `FunctionVal`"这么一个猜法,于是**用户在类上挂的任何函数
/// 都会漏给所有实例**(`C.func := …` → `c.func` 也读得到);挂数据则读不到 —— 同一件事
/// 两个答案,全看那个值碰巧长什么样。现在凭据只有一个:带标记才给。
///
/// 只读:定义/赋值一律拒绝。往这儿写等于把字段挂到整个类型上。
///
/// 它不是 `Scope` 的词法链:视图的 `Parent` 是 null,`Variables` 是空的 ——
/// 它只覆写 `LookupField` 这一个动作。</summary>
internal sealed class MemberView(Scope? own, ObjectVal type) : Scope
{
    public override Variable? LookupField(string name)
        => own?.LookupField(name) ?? LookupInClassChain(name);

    /// <summary>这个值**有哪些成员** —— 就是"查找能解析出什么",所以两个动作共用同一份判据。
    ///
    /// 两段的规则和 <see cref="LookupField"/> 完全一致:自己那层照单全收(不收的话,
    /// 模块里一个叫 `name` 的变量就没了 —— 它不是机制成员,是用户的变量),
    /// 类链那半只认方法名。
    ///
    /// 唯一排掉的是 `this`:它不是"这个值的成员",是**这个值自己**的别名,引擎为了让
    /// 类体写得出 `this` 才注入的。</summary>
    public override IEnumerable<string> MemberNames => Names();

    private IEnumerable<string> Names()
    {
        var seen = new HashSet<string>();
        if (own != null)
            foreach (var kv in own.Variables)
                if (kv.Key != ObjectVal.ThisMember && seen.Add(kv.Key))
                    yield return kv.Key;

        foreach (var t in ClassChain())
            foreach (var n in t.MethodNames)
                if (seen.Add(n))
                    yield return n;
    }

    /// <summary>沿类对象的 parent 链找**实例可见**的成员(`forInstance` 标记)。
    /// 自引用(`object`/`Every`/`Any` 的 parent 是自己)就地停。
    ///
    /// 判据和 <see cref="ObjectVal.MethodNames"/>(`Fields ()` 那半)必须一致 ——
    /// "查得到"和"列得出"是同一个问题的两个问法。</summary>
    internal Variable? LookupInClassChain(string name)
    {
        if (!ObjectVal.IsMethodName(name)) return null;
        foreach (var t in ClassChain())
        {
            var vr = t.Scope.LookupField(name);
            if (vr != null && vr.HasAttr(Attr.ForInstance)) return vr;
        }

        return null;
    }

    /// <summary>这个值所属的类,以及它沿 parent 的原型链上游(`object`/`Every`/`Any` 自引用时到头)。</summary>
    private IEnumerable<ObjectVal> ClassChain()
    {
        for (var t = type; t != null; t = t.Parent)
        {
            yield return t;
            if (t.Parent == t) yield break;     // 链到头
        }
    }

    private static RuntimeException ReadOnly()
        => new("值类型的成员只读（它们的成员表是借类那层的，写进去等于改掉整个类型）");

    public override Variable Define(string name, ObjectVal typeConstraint, RuntimeValue initialValue,
                                     Statement? site = null)
        => throw ReadOnly();

    public override Variable DefineOrReplace(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
        => throw ReadOnly();

    public override void Assign(string name, RuntimeValue value, Func<ObjectVal, bool>? alsoAccepts = null)
        => throw ReadOnly();
}
