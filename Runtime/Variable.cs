namespace Ravel.Runtime;

public class Variable
{
    public string Name { get; }
    public RuntimeType TypeConstraint { get; private set; }
    public RuntimeValue Value { get; private set; }
    readonly HashSet<string> _attrs = new();

    public Variable(string name, RuntimeType typeConstraint, RuntimeValue initialValue)
    { Name = name; TypeConstraint = typeConstraint; Value = initialValue; }

    public bool HasAttr(string a) => _attrs.Contains(a);
    public void SetAttr(string a) => _attrs.Add(a);
    public IEnumerable<string> Attrs => _attrs;

    // 便捷属性
    public bool IsReadonly => HasAttr("readonly");
    public bool IsInit => HasAttr("init");
    public bool IsCore => HasAttr("core");
    public bool IsOperator => _attrs.Any(a => a.StartsWith("operator"));
    public string? OperatorName => _attrs.FirstOrDefault(a => a.StartsWith("operator"));
    public bool IsOverride => HasAttr("override");
    public bool IsNew => HasAttr("new");

    public void Assign(RuntimeValue newValue)
    {
        if(IsReadonly) throw new RuntimeException("Cannot assign to readonly variable '" + Name + "'");
        if(TypeConstraint != RuntimeType.Any && !newValue.Type.IsAssignableTo(TypeConstraint))
            throw new RuntimeException("Type error: cannot assign " + newValue.Type + " to '" + Name + "' (declared " + TypeConstraint + ")");
        Value = newValue;
    }

    public void Update(RuntimeValue newValue, RuntimeType? newType) { Value = newValue; if(newType!=null) TypeConstraint = newType; }
    public override string ToString() => Name + ": " + TypeConstraint + " = " + Value;
}
