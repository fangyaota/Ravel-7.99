namespace Ravel.Runtime;

public class Scope(Scope? parent = null)
{
    private readonly Dictionary<string, Variable> _vars = [];
    public Scope? Parent { get; } = parent;

    /// <summary>沿词法链找变量(当前层 → 父层 → …),找不到返回 null。
    /// 其余查找/赋值都走这里,链式遍历只有这一份实现。</summary>
    private Variable? Find(string name)
    {
        for (var current = this; current != null; current = current.Parent)
            if (current._vars.TryGetValue(name, out var v))
                return v;

        return null;
    }

    public virtual Variable Define(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
    {
        if (_vars.ContainsKey(name)) throw new RuntimeException($"变量 '{name}' 已定义");
        var v = new Variable(name, typeConstraint, initialValue);
        _vars[name] = v;
        return v;
    }

    public virtual Variable DefineOrReplace(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
    {
        var v = new Variable(name, typeConstraint, initialValue);
        _vars[name] = v;
        return v;
    }

    public Variable Lookup(string name)
        => Find(name) ?? throw new RuntimeException($"未定义的变量 '{name}'");

    /// <summary>查找变量，找不到返回 null（不抛异常）</summary>
    public Variable? TryLookup(string name) => Find(name);

    public virtual void Assign(string name, RuntimeValue value)
    {
        var v = Find(name) ?? throw new RuntimeException($"无法给未定义变量 '{name}' 赋值");
        v.Assign(value);
    }

    public Scope Push() => new(this);
    public bool Contains(string name) => _vars.ContainsKey(name);
    public IEnumerable<KeyValuePair<string, Variable>> Variables => _vars;

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
        var thisVar = TryLookup("this");
        if (thisVar?.Value is ObjectVal obj)
            return obj.Scope.LookupField(name);
        return null;
    }
}
