namespace Ravel.Runtime;

public class Variable
{
    public string Name { get; }
    public RuntimeType TypeConstraint { get; private set; }
    public RuntimeValue Value { get; private set; }
    readonly HashSet<string> _attrs = [];

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
        if (IsReadonly) throw new RuntimeException("无法给只读变量 '" + Name + "' 赋值");
        if (TypeConstraint != RuntimeType.Any && !newValue.Type.IsAssignableTo(TypeConstraint))
            throw new RuntimeException("类型错误: 无法将 " + newValue.Type + " 赋值给 '" + Name + "' (声明为 " + TypeConstraint + ")");
        Value = newValue;
    }

    public void Update(RuntimeValue newValue, RuntimeType? newType) { Value = newValue; if (newType != null) TypeConstraint = newType; }
    public override string ToString() => Name + ": " + TypeConstraint + " = " + Value;
}
