namespace Ravel.Runtime;

/// <summary>类型值——本身是可调用函数（作为构造器/转换器）</summary>
public record TypeVal(RuntimeType Value) : FunctionVal(null!, (_, _) => new Error("类型不能直接调用"))
{
    public override RuntimeType Type => RuntimeType.Type;
    public override string ToString() => Value.Name;
}
