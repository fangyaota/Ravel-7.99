namespace Ravel.Runtime;

using Ravel.Runtime.Values;

// `Pair` 的成员表。父级走 `SequenceMembers`:一对也是"一串两格",
// 所以 `Count ()` / `First ()` / `ToList ()` / `Foreach` / `Map` / `Fold` 跟着来。
internal sealed class PairMembers : SequenceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(PairMembers), ObjectMembers.Set);

    // `p.Key ()` / `p.Value ()` —— 与 `p.At 0` / `p.At 1` 同义,
    // 但把"我读的就是一对"写在名字上,不用记哪一格是键。
    [ClassMethod("Key")]
    private static RuntimeValue Key(RuntimeValue self) => ((PairVal)self).Key;

    [ClassMethod("Value")]
    private static RuntimeValue Value(RuntimeValue self) => ((PairVal)self).Value;

    // `At` 只认 0 / 1。给出 `()` 来顶"没有第三格"会让 `[a b c] := p` 变成
    // 静默偏差,所以越界照实报。
    [ClassMethod("At")]
    private static RuntimeValue At(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is RangeVal)
            throw new RuntimeException(
                Messages.T("runtime.pair.at_needs_index", arg), ErrorKind.Argument);
        if (arg is not IntVal i)
            throw new RuntimeException(Messages.T("runtime.pair.at_needs_int", arg.Type), ErrorKind.Argument);

        var p = (PairVal)self;
        return i.Value switch
        {
            0 => p.Key,
            1 => p.Value,
            _ => throw new RuntimeException($"pair.At 的索引 {i.Value} 越界 (一对只有 0 / 1 两格)",
                ErrorKind.Index),
        };
    }
}
