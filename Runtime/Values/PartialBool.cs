namespace Ravel.Runtime;

public sealed record PartialBool : FunctionVal
{
    public bool Value { get; init; }
    public BlockVal Then { get; init; }

    public PartialBool(bool value, BlockVal then)
        : base(null!, (_, _) => FunctionVal.PlaceholderBody("PartialBool"))
    {
        Value = value;
        Then = then;
        // 它**就是**半成品 —— 还等着第二个块,用不着谁来标(见 FunctionVal.IsPartial)
        IsPartial = true;
    }

    public override string ToString() => base.ToString();
}
