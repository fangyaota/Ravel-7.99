namespace Ravel.Runtime;

/// <summary>跑一段 `VmCode` 的帧。**和别的帧最大的分别:它是可变的。**
///
/// 别的帧是持久化 record —— `WithResult` 一出就是 `this with { … }`,新的一份,
/// 于是 `callcc` 捕获整条链时天然安全。VM 这边反过来做:帧**原地改**
/// (`Ip` 往前走、操作数栈原地压),省掉那一步一拷 —— 代价是**捕获时必须克隆**
/// (见 <see cref="Snapshot"/> 和 `StepCallCC`)。
///
/// 它和引擎其余部分的接口就一条:**`WithResult` = 收下这个值、接着跑自己**。
/// 这正是整个引擎的推进方式(`Return` 就是 `_top = f.Parent.WithResult(v)`),
/// 所以 VM 帧不用改任何别的部件就能挂上去:被调方跑完把值交回来,它活了。
///
/// 作用域**按引用共享**(克隆时不拷)—— 和别的帧一样:引擎里 `Scope` 本来就是可变的、
/// 从来不随帧拷贝(`Frame.Scope` 那个 `set` 就是为它留的)。</summary>
public sealed record VmFrame : Frame
{
    public required VmCode Code { get; init; }

    /// <summary>操作数栈。**字段不是属性** —— 要能被 `Array.Resize` 换掉</summary>
    public RuntimeValue[] Stack = [];

    public int Ip;
    public int Sp;

    /// <summary>收下一个值:压栈,然后**交回自己**接着跑。
    /// (别的帧在这儿会 `with` 出一份新的;这个不改身份,所以 `Return` 之后 `_top` 还是它。)</summary>
    public override Frame WithResult(RuntimeValue v)
    {
        if (Sp == Stack.Length) Array.Resize(ref Stack, Sp == 0 ? 4 : Sp * 2);
        Stack[Sp++] = v;
        return this;
    }

    /// <summary>克隆一份 —— **只给 `callcc` 捕获用**:捕获链是只读的(多发射要能反复跳),
    /// 而本帧可变,所以进捕获链的必须是副本。操作数栈要拷、`Ip` 要带上;
    /// **作用域不拷**(和别的帧一个规矩:引擎里 Scope 从来不随帧拷贝)。</summary>
    public VmFrame Snapshot(Frame? parent)
    {
        var copy = new VmFrame
        {
            Code = Code,
            Stack = new RuntimeValue[Stack.Length],
            Parent = parent,
            Scope = Scope,
            CallSite = CallSite,
        };
        Array.Copy(Stack, copy.Stack, Sp);
        copy.Ip = Ip;
        copy.Sp = Sp;
        return copy;
    }

    // 别的帧是"值",按字段比;这个是"执行状态",按**身份**比 —— 和 class 一个行为。
    // (引擎里有地方拿帧做相等判断,不这么写会掉进"两个不同的 VM 帧因为字段一样而相等"。)
    public bool Equals(VmFrame? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
