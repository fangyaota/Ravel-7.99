namespace Ravel.Runtime;

/// <summary>类型值——本身是可调用函数，行为委托给 RuntimeType.Initializer</summary>
public record TypeVal(RuntimeType Value) : FunctionVal(
    Value.Initializer?.Scope ?? null!,
    (_, a) => Value.Initializer?.Body(a)
              ?? throw new RuntimeException($"类型 {Value.Name} 不能作为构造器调用"))
{
    public override RuntimeType Type => Value.Metaclass ?? RuntimeType.Type;
    /// <summary>类型名——代理 RuntimeType.Name，::= 命名时直接落到类型描述符上</summary>
    public override string? Name
    {
        get => Value.Name;
        set => Value.Name = value ?? "";
    }
    public override string ToString() => Value.Name;
}
