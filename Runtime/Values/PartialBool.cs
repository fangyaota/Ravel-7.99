namespace Ravel.Runtime;

/// <summary>真值半成品:true/false 收到第一个块后先返回它,等第二个块到齐再执行选中的那个。
/// 像 PartialCtor 一样靠 CallInto 分派,自身不驱动求值——所以不需要新的 ControlKind。</summary>
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

    /// <summary>盖掉 record 的自动 dump,用基类的 &lt;function&gt;</summary>
    public override string ToString() => base.ToString();
}
