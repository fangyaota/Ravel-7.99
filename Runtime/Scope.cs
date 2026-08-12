namespace Ravel.Runtime;

public class Scope
{
    private readonly Dictionary<string, Variable> _vars = new();
    public Scope? Parent { get; }

    public Scope(Scope? parent = null) { Parent = parent; }

    public Variable Define(string name, RuntimeType typeConstraint, RuntimeValue initialValue)
    {
        if (_vars.ContainsKey(name)) throw new RuntimeException($"Variable '{name}' already defined");
        var v = new Variable(name, typeConstraint, initialValue);
        _vars[name] = v;
        return v;
    }

    public Variable Lookup(string name)
    {
        if (_vars.TryGetValue(name, out var v)) return v;
        if (Parent != null) return Parent.Lookup(name);
        throw new RuntimeException($"Undefined variable '{name}'");
    }

    public Variable DefineOrReplace(string name, RuntimeType typeConstraint, RuntimeValue initialValue)
    {
        var v = new Variable(name, typeConstraint, initialValue);
        _vars[name] = v;
        return v;
    }

    public void Assign(string name, RuntimeValue value)
    {
        if (_vars.TryGetValue(name, out var v)) { v.Assign(value); return; }
        if (Parent != null) { Parent.Assign(name, value); return; }
        throw new RuntimeException($"Cannot assign to undefined variable '{name}'");
    }

    public Scope Push() => new(this);
    public bool Contains(string name) => _vars.ContainsKey(name);
    public IEnumerable<KeyValuePair<string, Variable>> Variables => _vars;
}
