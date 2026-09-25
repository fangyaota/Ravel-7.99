namespace Ravel.Runtime;

public class Variable(string name, RuntimeType typeConstraint, RuntimeValue initialValue)
{
    public string Name { get; } = name;
    public RuntimeType TypeConstraint { get; } = typeConstraint;
    public RuntimeValue Value { get; private set; } = initialValue;
    private readonly HashSet<string> _attrs = [];

    public bool HasAttr(string a) => _attrs.Contains(a);
    public void SetAttr(string a) => _attrs.Add(a);
    public IEnumerable<string> Attrs => _attrs;

    public void Assign(RuntimeValue newValue)
    {
        if (HasAttr("readonly")) throw new RuntimeException("无法给只读变量 '" + Name + "' 赋值");
        if (TypeConstraint != RuntimeType.Any && !newValue.Type.IsAssignableTo(TypeConstraint))
            throw new RuntimeException(
                "类型错误: 无法将 " + newValue.Type + " 赋值给 '" + Name + "' (声明为 " + TypeConstraint + ")");
        Value = newValue;
    }

    public override string ToString() => Name + ": " + TypeConstraint + " = " + Value;
}
