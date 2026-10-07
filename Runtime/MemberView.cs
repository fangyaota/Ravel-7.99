namespace Ravel.Runtime;

/// <summary>成员表视图 —— 「**这个值**能读到哪些成员」的唯一入口。
///
/// 两段,顺序固定:
/// 1. <c>own</c>:值**自己**那层(对象是它的实例作用域;类对象是它自己那张成员表)。
///    对象是扁平的,只查这一层。原子值(IntVal/StringVal/…)没有这一层,传 null ——
///    这正是"伪 Scope":它不落地成字段,所以 `IntVal(1) == IntVal(1)` 依旧成立。
/// 2. 沿**类对象的 parent 链**兜底,读每一层的 <see cref="ClassVal.InstanceTable"/>:
///    给实例的成员住在那一张("以我为类型的那些值读得到的"),`1.Fields ()` 的 `Fields`
///    就在 `Integer → IValue → Object` 链上 `Object` 的实例表里。
///
/// 第 2 段读的是**实例表**而不是类自己那张,所以"类上挂的东西实例看不到"是**结构**决定的
/// (`C.func := …` 落在 `C.Scope` 里,链上没人读它);反过来 `list.Add` 从类那一侧也读不到
/// —— `Add` 在 `List.InstanceTable` 里,而类对象自己的查找(第 1 段用它的 `Scope`、
/// 第 2 段沿**元类**链)两条都不经过它。不必再靠标记或过滤去分这两侧。
///
/// 只读:定义/赋值一律拒绝。往这儿写等于把字段挂到整个类型上。
///
/// 它不是 `Scope` 的词法链:视图的 `Parent` 是 null,`Variables` 是空的 ——
/// 它只覆写 `LookupField` 这一个动作。</summary>
internal sealed class MemberView(Scope? own, ObjectVal type) : Scope
{
    public override Variable? LookupField(string name)
        => own?.LookupField(name) ?? LookupInClassChain(name);

    /// <summary>这个值**有哪些成员** —— 就是"查找能解析出什么",两段和 <see cref="LookupField"/>
    /// **共用同一份判据**(类链那半见 <see cref="ClassVal.MethodNames"/>)。
    ///
    /// 自己那层照单全收:它就是"这个 Scope 里有哪些名字"(`init` / `parent` 也列,
    /// 读不到是另一回事)。不收的话,模块里一个叫 `name` 的变量就没了 ——
    /// 它不是机制成员,是用户的变量。
    ///
    /// 唯一另外排掉的是 `this`:它不是"这个值的成员",是**这个值自己**的别名,
    /// 引擎为了让类体写得出 `this` 才注入的。</summary>
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

    /// <summary>「**往上查**」那一半:这个值所属的类,再沿它的 `parent` 一层层往上。
    ///
    /// 就一条规则 —— 值读成员是"自己那层 + 它的类那层 + 类往上那些层";**类对象也是值**
    /// (它的类是它的元类),所以从类对象上读成员时起点落在元类,一路还是"往上",不是另一条链。
    ///
    /// 自引用(`parent` 是自己)是"链到头"的标记。**唯一的例外**是 `object` 以外的两个自指:
    /// `Every` / `Any` 借自指表示"不在继承链上"(底类型/顶类型按特判挂靠),而"任何值都有"
    /// 的那批(`ToString` / `Copy` / `Fields` / `is` / `==`…)住在 `object` 上 ——
    /// 所以它们**接上 `object` 再走完**。`object` 自己是根,到它就是到头。
    ///
    /// **只取类对象**:链上理论上都是类(`parent` 是建类时装进去的),万一有个普通对象
    /// 被当父类挂着,它没有实例表可言、也就贡献不了成员。
    ///
    /// **算一次就存下来**:取成员是解释器最热的一条路,而链建好之后不会再变(各方都只在
    /// 建类那一刻写 `parent`,之后只读) —— 从前这是个迭代器,每次取成员都要新建一堆
    /// 状态机对象。值类型那批(`IntVal` / `StringVal`…)的 `MemberScope` 直接借
    /// `Type.InstanceMembers`,每个类一份,于是这份缓存**所有同类值共享**。</summary>
    private ClassVal[]? _chain;

    private ClassVal[] ClassChain() => _chain ??= BuildChain();

    private ClassVal[] BuildChain()
    {
        var chain = new List<ClassVal>(4);
        var t = type as ClassVal;
        while (t != null)
        {
            chain.Add(t);
            if (t.Parent != t) { t = t.Parent as ClassVal; continue; }   // 往上一层
            if (t == BuiltinClasses.Object) break;                       // 根
            t = BuiltinClasses.Object;                                   // Every / Any:接到 object
        }

        return [.. chain];
    }

    /// <summary>沿类对象的 parent 链读**实例表** —— "给它那些实例用的成员"住在那一张。
    /// 自引用(`object`/`Every`/`Any` 的 parent 是自己)就地停。
    ///
    /// 判据和 <see cref="ClassVal.MethodNames"/>(`Fields ()` 那半)必须一致 ——
    /// "查得到"和"列得出"是同一个问题的两个问法。</summary>
    internal Variable? LookupInClassChain(string name)
    {
        if (!ObjectVal.IsMethodName(name)) return null;
        var chain = ClassChain();
        for (int i = 0; i < chain.Length; i++)
            if (chain[i].InstanceTable.LookupField(name) is { } vr && Bindable(vr))
                return vr;

        return null;
    }

    /// <summary>这一格能不能沿类链交出去。**函数**,以及 `by` 槽 —— 后者是引擎挂的属性
    /// (`[ClassProperty]`:值是一对 getter/setter 的 `PropertyVal`),它和函数一样
    /// "读的那一刻才认接收者"(见 `BuiltinClasses.BindProperty`)。
    ///
    /// 判据必须和 <see cref="ClassVal.MethodNames"/> 一字不差:"查得到"和"列得出"
    /// 是同一个问题的两个问法。别的数据成员照旧不出去 —— 它们没法绑 `self`。</summary>
    private static bool Bindable(Variable v) => v.Value is FunctionVal || v.HasAttr(Attr.By);

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
