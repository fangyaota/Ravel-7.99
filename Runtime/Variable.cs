namespace Ravel.Runtime;

public class Variable(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
{
    public string Name { get; } = name;
    public Statement? Site { get; internal set; }
    public ObjectVal TypeConstraint { get; internal set; } = typeConstraint;
    public RuntimeValue Value { get; private set; } = initialValue;
    private readonly HashSet<string> _attrs = [];

    public bool HasAttr(string a) => _attrs.Contains(a);
    public void SetAttr(string a) => _attrs.Add(a);
    public IEnumerable<string> Attrs => _attrs;

    public void Assign(RuntimeValue newValue)
    {
        CheckWritable();
        Value = newValue;
    }

    public void CheckWritable()
    {
        if (HasAttr(Attr.Readonly)) throw new RuntimeException("无法给只读变量 '" + Name + "' 赋值", ErrorKind.Access);
    }

    public void ReplaceSlot(RuntimeValue newValue)
    {
        CheckWritable();
        Value = newValue;
    }

    public override string ToString() => Name + ": " + TypeConstraint + " = " + Value;
}
