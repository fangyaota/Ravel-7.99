namespace Ravel.Runtime;

/// <summary>类型值——本身是可调用函数，行为委托给 RuntimeType.Initializer</summary>
public record TypeVal(RuntimeType Value) : FunctionVal(
    Value.Initializer?.Scope ?? null!,
    (_, args) => Value.Initializer?.Trampolined(args)
                 ?? throw new RuntimeException($"类型 {Value.Name} 不能作为构造器调用"))
{
    public override RuntimeType Type => RuntimeType.Type;
    public override string ToString() => Value.Name;
}
