namespace Ravel.Runtime;

/// <summary>部分应用的构造器:对象已经建好(类体已跑完、init 收过若干参数),
/// 但 init 还剩参数没收——再给它参数就继续喂 init,直到 init 应用完才把对象交出去。
/// 这让构造器调用和普通函数一样柯里化:`Point 3 4` ≡ `((Point 3) 4)`。
/// 注意同一个半成品被调用多次会作用在同一个对象的 scope 上。</summary>
public sealed record PartialCtor(ObjectVal Target, FunctionVal Partial)
    : FunctionVal(null!, (_, _) => VoidVal.Instance);
