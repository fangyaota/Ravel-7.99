namespace Ravel.Runtime;

public record BoolVal : FunctionVal
{
    public bool Value { get; init; }

    public BoolVal(bool value) : base(null!, (_, _) => FunctionVal.PlaceholderBody("BoolVal"))
    {
        // 基类把 ClassType 填成了 `Function`(那是给普通函数用的),bool 的元类是 `Bool` ——
        // **必须显式覆盖**,`??=` 在这里不管用(ClassType 已经不是 null 了)。
        ClassType = BuiltinClasses.Bool;
        Value = value;
    }

    public override ObjectVal Type => ClassType ??= BuiltinClasses.Bool;

    private Scope? _table;

    public override Scope Scope => _table ??= new Values.ValueTable(this);

    public virtual bool Equals(BoolVal? other) => other is not null && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString().ToLower();
}
