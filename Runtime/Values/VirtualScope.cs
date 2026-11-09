namespace Ravel.Runtime;

/// <summary>一个**值**自己的成员表 —— 一个值一张；表里有哪些名字是**静态清单**，
/// 但每一格的**值**第一次被读到时才造。</summary>
public abstract class VirtualScope(RuntimeValue owner) : Scope
{
    protected RuntimeValue Owner { get; } = owner;

    protected abstract Variable? Create(string name);

    /// <summary>这一张表的全部名字(静态清单)。懒的是"值什么时候造",不是"名字有没有"。</summary>
    protected abstract IEnumerable<string> AllNames { get; }

    /// <summary>清单在前(值自己的那些),类那层的机制名在后 —— 与 `LookupField` 的兜底顺序一致。</summary>
    private IEnumerable<string> NamesInOrder()
    {
        foreach (var name in AllNames) yield return name;

        var seen = new HashSet<string>(AllNames);
        for (var t = Owner.Type; t is not null; t = t.Parent)
        {
            if (t is ClassVal tc)
                foreach (var name in tc.InstanceTable.MemberNames)
                    if (seen.Add(name)) yield return name;
            if (t.Parent is null || ReferenceEquals(t.Parent, t)) break;
        }
    }

    public override IEnumerable<string> MemberNames => NamesInOrder();

    /// <summary>列举 = 把每一格都造出来(顺序照静态清单)。`Fields ()` 走这条。</summary>
    public override IEnumerable<KeyValuePair<string, Variable>> Variables
    {
        get
        {
            foreach (var name in NamesInOrder())
                if (LookupField(name) is { } v)
                    yield return new KeyValuePair<string, Variable>(name, v);
        }
    }

    protected Variable Operator(string name, Func<RuntimeValue, RuntimeValue, RuntimeValue> impl)
        => Field(name, new BuiltinMethodVal(impl) { Name = name });

    protected Variable Method(string name, Func<RuntimeValue, RuntimeValue> impl)
        => Field(name, new BuiltinMethodVal((self, _) => impl(self)) { Name = name });

    protected Variable Field(string name, RuntimeValue value)
    {
        var v = new Variable(name, BuiltinClasses.Function, value);
        v.SetAttr(Attr.Readonly);
        return v;
    }

    public override Variable? LookupField(string name)
    {
        if (base.LookupField(name) is { } hit) return hit;

        if (Create(name) is { } made)
            // 走基类那条**不抛**的存档路:本类的 DefineOrReplace 是给外面写口用的(拒写)
            return base.DefineOrReplace(name, made.TypeConstraint, made.Value);

        // 机制成员(`parent` / `Fields` 这类)住在类那层表里;值类型只有一两层,走完拉倒。
        for (var t = Owner.Type; t is not null; t = t.Parent)
        {
            if (t is ClassVal tc)
            {
                if (tc.InstanceTable.LookupField(name) is { } mv) return mv;
            }
            if (t.Parent is null || ReferenceEquals(t.Parent, t)) break;
        }

        return null;
    }

    public override Variable Define(string name, ObjectVal typeConstraint, RuntimeValue initialValue,
                                    Statement? site = null)
        => throw ReadOnly(name);

    public override Variable DefineOrReplace(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
        => throw ReadOnly(name);

    public override void Assign(string name, RuntimeValue value, Func<ObjectVal, bool>? alsoAccepts = null)
        => throw ReadOnly(name);

    private static RuntimeException ReadOnly(string name)
        => new($"值类型的成员表是只读的：不能定义/改写 '{name}'", ErrorKind.Access);
}
