namespace Ravel.Runtime;

using Ravel.Runtime.Values;

public record ClassVal : FunctionVal
{
    public ClassVal(ClassVal? classType, Scope? members)
        : base(null!, (_, _) => VoidVal.Instance, members)
    {
        ClassType = classType ?? this;
    }

    internal bool IsValueLike;

    internal IMemberSet? Members { get; set; }

    public override string ToString() => DisplayName;
}
