namespace Ravel.Runtime;

public record DefaultVal : RuntimeValue
{
    public static readonly DefaultVal Instance = new();
    public override ObjectVal Type => BuiltinClasses.Every;
    public override string ToString() => "default";
    private DefaultVal() { }
}
