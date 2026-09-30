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
/// **类那一侧**（`classSide`,建视图的是个类对象）多一条:自己那层里带 `private` 的成员
/// 读不到 —— 「private = 类上读不到」（内置方法都带它,`list.Add` 从类上读到的那份没绑 self,
/// 调用就是 `((ListVal)ClassVal)` 的 InvalidCastException）。**只拦自己那层**:
/// 沿链继承来的(各家 `Object` / `Type` 那层的 `Fields ()` / `Copy ()` / `GetImplements ()`)
/// 照旧读得到 —— `C.Fields ()` 天天在类对象上读,拦了等于把反射全关掉。
///
/// 只读:定义/赋值一律拒绝。往这儿写等于把字段挂到整个类型上。
///
/// 它不是 `Scope` 的词法链:视图的 `Parent` 是 null,`Variables` 是空的 ——
/// 它只覆写 `LookupField` 这一个动作。</summary>
internal sealed class MemberView(Scope? own, ObjectVal type, bool classSide = false) : Scope
{
    public override Variable? LookupField(string name)
        => ReadOwn(name) ?? LookupInClassChain(name);

    /// <summary>自己那层(扁平,不走链)。类那一侧要把带 `private` 的那些挡掉。</summary>
    private Variable? ReadOwn(string name)
    {
        var ownVar = own?.LookupField(name);
        if (ownVar == null) return null;
        return classSide && ownVar.HasAttr(Attr.Private) ? null : ownVar;
    }

    /// <summary>这个值**有哪些成员** —— 就是"查找能解析出什么",两段和 <see cref="LookupField"/>
    /// **共用同一份判据**:
    /// 类链那半只收带 `forInstance` 的(见 <see cref="ObjectVal.MethodNames"/>),
    /// 自己那层在**类那一侧**也把 `private` 挡掉(`ReadOwn` 挡的那批就是这同一批)。
    ///
    /// 于是 `list.Fields ()` / `Json.Fields ()` 里没有 `Add` / `Kind` 那批 ——
    /// 它们**从这一侧读不到**,列出来等于骗人(那是"给实例的成员",在实例那侧列)。
    /// 类那一侧本来就要用的(`Json.FromString`、运算符)不在 private 那一批里,照旧列。
    ///
    /// 不收自己那层的话,模块里一个叫 `name` 的变量就没了 —— 它不是机制成员,是用户的变量。
    /// 唯一另外排掉的是 `this`:它不是"这个值的成员",是**这个值自己**的别名,
    /// 引擎为了让类体写得出 `this` 才注入的。</summary>
    public override IEnumerable<string> MemberNames => Names();

    private IEnumerable<string> Names()
    {
        var seen = new HashSet<string>();
        if (own != null)
            foreach (var kv in own.Variables)
            {
                if (kv.Key == ObjectVal.ThisMember) continue;
                if (classSide && kv.Value.HasAttr(Attr.Private)) continue;
                if (seen.Add(kv.Key)) yield return kv.Key;
            }

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
