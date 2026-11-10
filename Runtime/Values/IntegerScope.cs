namespace Ravel.Runtime;

/// <summary>`int` 自己的成员表 —— 一个整数一张，算子按名字懒建。</summary>
internal sealed class IntegerScope(IntVal self) : Values.ValueTable(self)
{
    protected override IEnumerable<string> AllNames =>
    [
        "+", "-", "*", "/", "%", "<<", ">>", "<<<", ">>>", "**",
        "==", "!=", "<", ">", "<=", ">=", "&", "|", "^", "ToString", "init",
    ];

    protected override Variable? Create(string name)
    {
        if (BuiltinClasses.IntegerOperator(name) is { } impl) return Operator(name, impl);
        return name switch
        {
            "ToString" => Method(name, _ => new StringVal(self.Value.ToString())),
            "init" => Field(name, BuiltinClasses.IntCaster!),
            _ => null,
        };
    }
}
