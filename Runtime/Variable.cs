namespace Ravel.Runtime;

public class Variable(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
{
    public string Name { get; } = name;
    /// <summary>类型约束。可写:BuiltinClasses 建树分两趟,String/Object 这些类对象
    /// 在第二趟才存在,那时才能给 parent/name 这些机制成员挂上约束。</summary>
    public ObjectVal TypeConstraint { get; internal set; } = typeConstraint;
    public RuntimeValue Value { get; private set; } = initialValue;
    private readonly HashSet<string> _attrs = [];

    public bool HasAttr(string a) => _attrs.Contains(a);
    public void SetAttr(string a) => _attrs.Add(a);
    public IEnumerable<string> Attrs => _attrs;

    public void Assign(RuntimeValue newValue)
    {
        if (HasAttr(Attr.Readonly)) throw new RuntimeException("无法给只读变量 '" + Name + "' 赋值");
        if (TypeConstraint != BuiltinClasses.Any && !newValue.Type.IsAssignableTo(TypeConstraint))
            throw new RuntimeException(
                "类型错误: 无法将 " + newValue.Type + " 赋值给 '" + Name + "' (声明为 " + TypeConstraint + ")");
        Value = newValue;
    }

    public override string ToString() => Name + ": " + TypeConstraint + " = " + Value;
}
