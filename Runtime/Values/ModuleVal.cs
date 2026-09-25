namespace Ravel.Runtime;

/// <summary>Ravel 模块实例</summary>
public record ModuleVal(ObjectVal ModType, Scope ModuleScope) : RuntimeValue
{
    public override ObjectVal Type => ModType;
    public override string ToString() => $"<module {ModType.Name}>";
}
