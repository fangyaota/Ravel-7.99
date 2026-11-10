namespace Ravel.Runtime;

public record DotNetTypeVal : ObjectVal
{
    public Type DotNetType { get; init; }

    public DotNetTypeVal(Type t, Scope? members = null)
        : base(BuiltinClasses.DotNetType, members ?? new Scope())
        => DotNetType = t;

    public override string ToString() => ShowDepth.Guard(() => "DotNetType " + DotNetType.FullName);
}
