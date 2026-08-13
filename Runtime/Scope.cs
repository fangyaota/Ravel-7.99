namespace Ravel.Runtime;

public class Scope(Scope? parent = null)
{
    private readonly Dictionary<string, Variable> _vars = [];
    public Scope? Parent { get; } = parent;


    public Variable Define(string name, RuntimeType typeConstraint, RuntimeValue initialValue)
    {
        if (_vars.ContainsKey(name)) throw new RuntimeException($"变量 '{name}' 已定义");
        var v = new Variable(name, typeConstraint, initialValue);
        _vars[name] = v;
        return v;
    }

    public Variable Lookup(string name)
    {
        if (_vars.TryGetValue(name, out var v)) return v;
        if (Parent != null) return Parent.Lookup(name);
        throw new RuntimeException($"未定义的变量 '{name}'");
    }

    /// <summary>查找变量，找不到返回 null（不抛异常）</summary>
    public Variable? TryLookup(string name)
    {
        if (_vars.TryGetValue(name, out var v)) return v;
        if (Parent != null) return Parent.TryLookup(name);
        return null;
    }

    public Variable DefineOrReplace(string name, RuntimeType typeConstraint, RuntimeValue initialValue)
    {
        var v = new Variable(name, typeConstraint, initialValue);
        _vars[name] = v;
        return v;
    }

    public void Assign(string name, RuntimeValue value)
    {
        if (_vars.TryGetValue(name, out var v))
        {
            v.Assign(value);
            return;
        }

        if (Parent != null)
        {
            Parent.Assign(name, value);
            return;
        }

        throw new RuntimeException($"无法给未定义变量 '{name}' 赋值");
    }

    public Scope Push() => new(this);
    public bool Contains(string name) => _vars.ContainsKey(name);
    public IEnumerable<KeyValuePair<string, Variable>> Variables => _vars;

    /// <summary>沿 parent 变量链查找字段（继承）：当前层 → parent 字段的父类实例 Scope → 祖父…</summary>
    public Variable? LookupField(string name)
    {
        var current = this;
        while (true)
        {
            if (current._vars.TryGetValue(name, out var v))
                return v;
            if (!current._vars.TryGetValue("parent", out var p) || p.Value is not ObjectVal obj)
                return null;
            current = obj.Scope;
        }
    }

    /// <summary>查找变量/字段：先词法链，再沿词法链找到的 this 对象的 parent 字段链（继承）</summary>
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
