namespace Ravel.Runtime;

/// <summary>Property = getter + setter 对。
///
/// 非原子值 —— 有**自己的成员表**,见 <see cref="ObjectVal"/>。</summary>
public record PropertyVal : ObjectVal
{
    public FunctionVal Getter { get; init; }
    public FunctionVal Setter { get; init; }
    public List<string>? Attrs { get; init; }

    public PropertyVal(FunctionVal getter, FunctionVal setter, List<string>? attrs = null, Scope? members = null)
        : base(BuiltinClasses.Property, members ?? new Scope())
    {
        Getter = getter;
        Setter = setter;
        Attrs = attrs;
    }

    public override string ToString() => "<property>";
}
