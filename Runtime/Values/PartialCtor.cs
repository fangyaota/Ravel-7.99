namespace Ravel.Runtime;

/// <summary>部分应用的构造器:对象已经建好(类体已跑完、init 收过若干参数),
/// 但 init 还剩参数没收——再给它参数就继续喂 init,直到 init 应用完才把对象交出去。
/// 这让构造器调用和普通函数一样柯里化:`Point 3 4` ≡ `((Point 3) 4)`。
/// 注意同一个半成品被调用多次会作用在同一个对象的 scope 上。</summary>
public sealed record PartialCtor(ObjectVal Target, FunctionVal Partial)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("PartialCtor"))
{
    /// <summary>盖掉 record 的自动 dump:带上**建的是哪个类**,以及 init 还等着哪个参数、
    /// 已经喂过哪些 —— 和普通柯里化一个读法(`Point 1` → `<function Point (y: int) => … 已收 x=1>`)。</summary>
    public override string ToString()
        => Partial is LambdaVal lam
            ? "<function " + Target.Type.DisplayName + " " + lam.Describe() + ">"
            : "<function " + Target.Type.DisplayName + " " + Partial + ">";
}
