namespace Ravel.Runtime;

public class Scope(Scope? parent = null)
{
    private Dictionary<string, Variable>? _vars;
    public Scope? Parent { get; private set; } = parent;

    public void Reparent(Scope parent) => Parent = parent;

    private Variable? Find(string name)
    {
        for (var current = this; current != null; current = current.Parent)
            if (current.LookupHere(name) is { } v)
                return v;

        return null;
    }

    protected virtual Variable? LookupHere(string name)
        => _vars is { } d && d.TryGetValue(name, out var v) ? v : null;

    internal ObjectVal? TableOf { get; set; }

    internal virtual Scope? InstanceScope => null;

    public virtual Variable Define(string name, RuntimeValue initialValue, Statement? site = null)
    {
        // **`:=` 是定义,不是覆盖**:同一个作用域里同名再定义就是错。
        //
        // 只有一种情形放行:**同一个定义语句重跑** —— 续延(续延重入会把捕获点之后的尾巴再走
        // 一遍,`while` 就是拿 callcc 写的)与"同一个块被反复执行"。那两条路落下来的
        // `Site` 是同一个节点,于是认出"这是重跑"而不是"又来一条定义"。
        // `site` 为 null 的是引擎内部直接调的那些(装类、绑 `this`…),按老规矩覆盖。
        if (_vars is { } had && had.TryGetValue(name, out var existing)
            && !(site != null && ReferenceEquals(existing.Site, site)))
            throw new RuntimeException(
                $"'{name}' 在这个作用域里已经定义过 —— `:=` 是定义不是覆盖（要改值/覆盖继承来的成员，用 `=`）");

        // **这一层是某个值的成员表**(模块):**类型链上**已有的名字也归这条规矩 ——
        // 那不是"新开一个变量",是把继承来的成员蒙掉:
        //     ravel "M"
        //     References := () => { … }      # ✗ 蒙掉 `Ravel.References`
        //     References = () => { … }       # ✓ 覆盖走 `=`
        // 类体那边同一条规矩由"各层类体**平铺进同一个实例作用域**"实现(见 `StepClassInit`):
        // 继承来的 `init` 就摆在本层,再 `:=` 自然撞上。模块的成员是**动态往下查**的
        // (`MemberView`),平铺不了,所以在这儿补一道。
        // (`_vars?.ContainsKey` 那半是给**上面那条**让路的:同一个节点重跑时本层已经有它了,
        //  那是放行的情形,不该再拿去撞类链 —— 撞的是同一个自己。)
        if (TableOf is { } owner && _vars?.ContainsKey(name) != true
            && owner.MemberScope.LookupField(name) is not null)
            throw new RuntimeException(
                $"'{name}' 是 {owner.Type} 的成员 —— 顶层作用域就是模块的成员表，`:=` 蒙不掉它（要覆盖用 `=`）");
        var v = new Variable(name, initialValue) { Site = site };
        (_vars ??= [])[name] = v;
        return v;
    }

    public virtual Variable DefineOrReplace(string name, RuntimeValue initialValue)
    {
        // **只读变量不许重新定义** —— `:=` 换掉的是整个 Variable,attrs(readonly/core/private…)
        // 跟着老的那个一起没,于是"只读"就成了一道能随手绕过的门:
        //     true = 1        → 无法给只读变量 'true' 赋值      ✓ 拦住了
        //     true := 1       → 从前静默成功,而且 readonly 就此消失
        // 只查**本层**:在外层作用域里 `x := 1` 是新开一个局部变量(遮蔽),不是重新定义。
        if (_vars is { } had && had.TryGetValue(name, out var old) && old.HasAttr(Attr.Readonly))
            throw new RuntimeException($"无法重新定义只读变量 '{name}'", ErrorKind.Access);
        var v = new Variable(name, initialValue);
        (_vars ??= [])[name] = v;
        return v;
    }

    public void RemoveHere(string name) => _vars?.Remove(name);

    public Variable Lookup(string name)
        => Find(name) ?? throw new RuntimeException($"未定义的变量 '{name}'", ErrorKind.Name);

    public Variable? TryLookup(string name) => Find(name);

    public virtual void Assign(string name, RuntimeValue value)
    {
        if (Find(name) is { } v)
        {
            v.Assign(value);
            return;
        }

        // **这一层是某个值的成员表**(模块)时,`=` 落到**类型链上**的成员要落到**本层** ——
        // 在成员表里开一格盖住它(和类体里 `init = …` 一个意思)。
        // 不能去改类对象上那一格:那是**所有模块共用的同一份**,`M.References = f`
        // 会顺手把别的模块一起改掉。
        //
        // **这里不查只读**:这一句不是"给找到的那个变量赋值",是**新开一个遮蔽的** ——
        // 内层作用域里 `true := 1` 遮蔽外层那个只读的就是这么过去的(见 `Define` 只查本层)。
        // 类对象上那一格原样不动,`readonly` 保护的是它。
        if (TableOf is { } owner && owner.MemberScope.LookupField(name) is { } inherited)
        {
            (_vars ??= [])[name] = new Variable(name, value);
            return;
        }

        throw new RuntimeException($"无法给未定义变量 '{name}' 赋值", ErrorKind.Name);
    }

    public Scope Push() => new(this);
    public bool Contains(string name) => _vars?.ContainsKey(name) ?? false;

    protected IEnumerable<KeyValuePair<string, Variable>> StoredHere
        => _vars ?? Enumerable.Empty<KeyValuePair<string, Variable>>();

    protected void PutHere(string name, Variable v) => (_vars ??= [])[name] = v;

    public virtual IEnumerable<KeyValuePair<string, Variable>> Variables
        => _vars ?? Enumerable.Empty<KeyValuePair<string, Variable>>();

    public virtual IEnumerable<string> MemberNames
    {
        get
        {
            if (_vars is { } d)
                foreach (var name in d.Keys) yield return name;
            if (MemberOf is not { } owner) yield break;

            var seen = _vars is { } stored ? new HashSet<string>(stored.Keys) : new HashSet<string>();
            if (owner.Type is ClassVal type)
            {
                if (type.Members is { } members)
                    foreach (var name in members.Names)
                        if (seen.Add(name)) yield return name;
                foreach (var name in ObjectMembers.Any.Names)
                    if (seen.Add(name)) yield return name;
            }
        }
    }

    internal RuntimeValue? MemberOf { get; set; }

    internal Variable? OwnField(string name) => Stored(name);

    protected Variable? Stored(string name)
        => _vars is { } d && d.TryGetValue(name, out var v) ? v : null;

    public virtual Variable? LookupField(string name)
        => Stored(name) ?? (MemberOf is { } owner ? OwnerMember(owner, name) : null);

    private static Variable? OwnerMember(RuntimeValue owner, string name)
        => owner.Type is ClassVal type
            ? type.Members?.Create(type, name) ?? ObjectMembers.Any.Create(type, name)
            : null;

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
