namespace Ravel.Runtime;

/// <summary>Ravel 模块实例</summary>
public record ModuleVal(RuntimeType ModType, Scope ModuleScope) : RuntimeValue
{
    public override RuntimeType Type => ModType;
    public override string ToString() => $"<module {ModType.Name}>";
}
