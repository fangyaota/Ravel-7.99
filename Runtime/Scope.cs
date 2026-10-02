namespace Ravel.Runtime;

public class Scope(Scope? parent = null)
{
    private readonly Dictionary<string, Variable> _vars = [];
    public Scope? Parent { get; private set; } = parent;

    /// <summary>换掉词法父 —— **只给"造实现"那一处用**(见 `StepImplMake`)。
    ///
    /// 那个 scope 是"实例化接口"那趟(`StepClassInit`)建出来的(词法父 = **接口定义**处,
    /// 接口体在那儿写的),而它同时要当**实现对象**的成员表 —— 实现块的自由名字得在
    /// "实现写在哪"解析(实现体和接口体常常不在一处:`lib/keys.rav` 里 `IDict` 的实现块
    /// 引用的 `Of` 就只在那儿可见)。所以那一处把它接到实现块的捕获作用域上。
    ///
    /// 别处不要再调:作用域是棵树,父能改的话读的人就没法靠它推理了。</summary>
    public void Reparent(Scope parent) => Parent = parent;

    /// <summary>沿词法链找变量(当前层 → 父层 → …),找不到返回 null。
    /// 其余查找/赋值都走这里,链式遍历只有这一份实现。
    ///
    /// 每一层问的是 <see cref="LookupHere"/>(虚的),不是直接翻 `_vars` ——
    /// 于是某一层可以**代别人回答**(<see cref="BodyScope"/> 就是这么把自己的实例表
    /// 插进链里的:类体跑在它上面,而"这一段代码属于哪个对象"由它说了算)。</summary>
    private Variable? Find(string name)
    {
        for (var current = this; current != null; current = current.Parent)
            if (current.LookupHere(name) is { } v)
                return v;

        return null;
    }

    /// <summary>这一层能不能给出这个名字 —— 默认就是翻本层自己的 `_vars`。
    ///
    /// 覆写它的只有 <see cref="BodyScope"/>:那一层自己**不装东西**(定义都转发给实例表了),
    /// 它的回答是"先看看实例表本层有没有"。**只问那一层**(`LookupField`,不走它的词法父):
    /// 实例表的词法父是"最具体那个类的写法处",那正是 <see cref="BodyScope"/> 要绕开的东西。</summary>
    protected virtual Variable? LookupHere(string name)
        => _vars.TryGetValue(name, out var v) ? v : null;

    /// <summary>这一层**替哪个对象服务**(类体作用域会说"我服务那个实例")。默认没有。
    ///
    /// 给 `Interpreter.CheckFieldAccess` 那种"当前代码在不在这个对象的类里"的判断用:
    /// 类体跑在 <see cref="BodyScope"/> 上之后,实例表不在词法链上,只比对 `Parent` 会漏掉它。</summary>
    internal virtual Scope? InstanceScope => null;

    public virtual Variable Define(string name, ObjectVal typeConstraint, RuntimeValue initialValue,
                                    Statement? site = null)
    {
        // **`:=` 是定义,不是覆盖**:同一个作用域里同名再定义就是错。
        //
        // 只有一种情形放行:**同一个定义语句重跑** —— 续延(续延重入会把捕获点之后的尾巴再走
        // 一遍,`while` 就是拿 callcc 写的)与"同一个块被反复执行"。那两条路落下来的
        // `Site` 是同一个节点,于是认出"这是重跑"而不是"又来一条定义"。
        // `site` 为 null 的是引擎内部直接调的那些(装类、绑 `this`…),按老规矩覆盖。
        if (_vars.TryGetValue(name, out var existing)
            && !(site != null && ReferenceEquals(existing.Site, site)))
            throw new RuntimeException(
                $"'{name}' 在这个作用域里已经定义过 —— `:=` 是定义不是覆盖（要改值/覆盖继承来的成员，用 `=`）");
        var v = new Variable(name, typeConstraint, initialValue) { Site = site };
        _vars[name] = v;
        return v;
    }

    public virtual Variable DefineOrReplace(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
    {
        // **只读变量不许重新定义** —— `:=` 换掉的是整个 Variable,attrs(readonly/core/private…)
        // 跟着老的那个一起没,于是"只读"就成了一道能随手绕过的门:
        //     true = 1        → 无法给只读变量 'true' 赋值      ✓ 拦住了
        //     true := 1       → 从前静默成功,而且 readonly 就此消失
        // 只查**本层**:在外层作用域里 `x := 1` 是新开一个局部变量(遮蔽),不是重新定义。
        if (_vars.TryGetValue(name, out var old) && old.HasAttr(Attr.Readonly))
            throw new RuntimeException($"无法重新定义只读变量 '{name}'", ErrorKind.Access);
        var v = new Variable(name, typeConstraint, initialValue);
        _vars[name] = v;
        return v;
    }

    /// <summary>**删掉本层的一个名字**,找不到就什么都不做。给 `IDict` 的 `Remove` 槽用 ——
    /// 作用域从前只有增改,没有删(`lib/keys.rav` 里那条 `impl (IDict System.Scope …)`)。
    ///
    /// 只删**本层**:外层同名的那个不受影响(它本来就是另一格);和 `Define` 一样,
    /// 作用域是棵树,别处不该被牵连。</summary>
    public void RemoveHere(string name) => _vars.Remove(name);

    public Variable Lookup(string name)
        => Find(name) ?? throw new RuntimeException($"未定义的变量 '{name}'", ErrorKind.Name);

    /// <summary>查找变量，找不到返回 null（不抛异常）</summary>
    public Variable? TryLookup(string name) => Find(name);

    public virtual void Assign(string name, RuntimeValue value, Func<ObjectVal, bool>? alsoAccepts = null)
    {
        var v = Find(name) ?? throw new RuntimeException($"无法给未定义变量 '{name}' 赋值", ErrorKind.Name);
        v.Assign(value, alsoAccepts);
    }

    public Scope Push() => new(this);
    public bool Contains(string name) => _vars.ContainsKey(name);
    public IEnumerable<KeyValuePair<string, Variable>> Variables => _vars;

    /// <summary>这一层里**有哪些成员名**。取值的成员清单要走 <see cref="MemberView"/>
    /// (它在本层之外还并上类链的方法);这里只是"本层登记了哪些名字"。</summary>
    public virtual IEnumerable<string> MemberNames => _vars.Keys;

    /// <summary>查找字段：只看本 scope 自己的变量，不走词法链、不走链式继承。
    /// 继承来的字段在实例化时就已平铺进同一个实例 scope，所以一层就够；
    /// 不能走词法链是因为实例 scope 的 Parent 是「类定义处的作用域」，那会泄漏外部局部变量。
    ///
    /// **取值的成员要走 <see cref="MemberView"/>**(它在本层之外还沿类链兜底);
    /// 这个名字保留"只查一层"的语义,给那些**必须**看本层的调用点用:
    /// 找构造器(`init`)、找类运算符、取 `parent`/`block`/`name` 这些类自己的数据。</summary>
    public virtual Variable? LookupField(string name)
        => _vars.TryGetValue(name, out var v) ? v : null;

    /// <summary>查找变量/字段：先词法链（方法体内能直接读写实例字段），再兜底查 this 对象的实例 scope</summary>
    public Variable? LookupVar(string name)
    {
        var v = TryLookup(name);
        if (v != null) return v;
        var thisVar = TryLookup(ObjectVal.ThisMember);
        if (thisVar?.Value is ObjectVal obj)
            return obj.Scope.LookupField(name);
        return null;
    }
}
