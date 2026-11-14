namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

internal static class StringMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(StringMembers), ObjectMembers.Set);

    [ClassCtor]
    private static RuntimeValue Init(RuntimeValue arg) => BuiltinClasses.CastToString(arg);

    // ── 长度 ──

    [ClassMethod("Length")]
    private static RuntimeValue Length(RuntimeValue self) => IntVal.Of(((StringVal)self).Value.Length);

    [ClassMethod("IsEmpty")]
    private static RuntimeValue IsEmpty(RuntimeValue self) => new BoolVal(((StringVal)self).Value.Length == 0);

    // ── 取字符 ──
    // `At i` 给**字符**(和 `[1 2].At 0` 给元素对称);越界当场报错,不悄悄给个 `()`
    [ClassMethod("At")]
    private static RuntimeValue At(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is RangeVal)
            throw new RuntimeException(
                Messages.T("runtime.shared.at_needs_index", arg),
                ErrorKind.Argument);
        var v = ((StringVal)self).Value;
        var i = BuiltinClasses.IntArg(arg, "s.At");
        if (i < 0 || i >= v.Length)
            throw new RuntimeException(Messages.T("runtime.shared.index_out_of_range", "s.At", i, v.Length), ErrorKind.Index);
        return new CharVal(v[i]);
    }

    [ClassMethod("Chars")]
    private static RuntimeValue Chars(RuntimeValue self)
        => new ListVal([.. ((StringVal)self).Value.Select(c => (RuntimeValue)new CharVal(c))]);

    // ── 找 ──

    [ClassMethod("Contains")]
    private static RuntimeValue Contains(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((StringVal)self).Value.Contains(BuiltinClasses.TextArg(arg, "s.Contains"), StringComparison.Ordinal));

    [ClassMethod("StartsWith")]
    private static RuntimeValue StartsWith(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((StringVal)self).Value.StartsWith(BuiltinClasses.TextArg(arg, "s.StartsWith"), StringComparison.Ordinal));

    [ClassMethod("EndsWith")]
    private static RuntimeValue EndsWith(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((StringVal)self).Value.EndsWith(BuiltinClasses.TextArg(arg, "s.EndsWith"), StringComparison.Ordinal));

    [ClassMethod("IndexOf")]
    private static RuntimeValue IndexOf(RuntimeValue self, RuntimeValue arg)
    {
        var v = ((StringVal)self).Value;
        var i = v.IndexOf(BuiltinClasses.TextArg(arg, "s.IndexOf"), StringComparison.Ordinal);
        if (i < 0) throw new RuntimeException(Messages.T("runtime.string.index_of_not_found", BuiltinClasses.TextArg(arg, "s.IndexOf")), ErrorKind.Value);
        return IntVal.Of(i);
    }

    [ClassMethod("Find")]
    private static RuntimeValue Find(RuntimeValue self, RuntimeValue arg)
        => IntVal.Of(((StringVal)self).Value.IndexOf(BuiltinClasses.TextArg(arg, "s.Find"), StringComparison.Ordinal));   // 没有给 -1

    [ClassMethod("LastIndexOf")]
    private static RuntimeValue LastIndexOf(RuntimeValue self, RuntimeValue arg)
    {
        var v = ((StringVal)self).Value;
        var i = v.LastIndexOf(BuiltinClasses.TextArg(arg, "s.LastIndexOf"), StringComparison.Ordinal);
        if (i < 0) throw new RuntimeException(Messages.T("runtime.string.last_index_of_not_found", BuiltinClasses.TextArg(arg, "s.LastIndexOf")), ErrorKind.Value);
        return IntVal.Of(i);
    }

    // ── 切 ──

    [ClassMethod("Slice")]
    private static RuntimeValue Slice(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not RangeVal rng)
            throw new RuntimeException(Messages.T("runtime.shared.slice_needs_range", arg.Type), ErrorKind.Argument);

        var v = ((StringVal)self).Value;
        var (from, to, reverse) = SliceBounds(rng, v.Length, "s.Slice");
        var part = v[from..to];
        if (reverse) part = string.Concat(part.Reverse());
        return new StringVal(part);
    }

    [ClassMethod("Take")]
    private static RuntimeValue Take(RuntimeValue self, RuntimeValue arg)
        => new StringVal(((StringVal)self).Value[..Math.Clamp(BuiltinClasses.IntArg(arg, "s.Take"), 0, ((StringVal)self).Value.Length)]);

    [ClassMethod("Skip")]
    private static RuntimeValue Skip(RuntimeValue self, RuntimeValue arg)
        => new StringVal(((StringVal)self).Value[Math.Clamp(BuiltinClasses.IntArg(arg, "s.Skip"), 0, ((StringVal)self).Value.Length)..]);

    // ── 变 ──

    [ClassMethod("Trim")]
    private static RuntimeValue Trim(RuntimeValue self) => new StringVal(((StringVal)self).Value.Trim());

    [ClassMethod("ToUpper")]
    private static RuntimeValue ToUpper(RuntimeValue self) => new StringVal(((StringVal)self).Value.ToUpperInvariant());

    [ClassMethod("ToLower")]
    private static RuntimeValue ToLower(RuntimeValue self) => new StringVal(((StringVal)self).Value.ToLowerInvariant());

    [ClassMethod("Replace")]
    private static RuntimeValue Replace(RuntimeValue self, RuntimeValue arg)
    {
        var from = BuiltinClasses.TextArg(arg, "s.Replace");
        return FunctionVal.From(b => new StringVal(((StringVal)self).Value.Replace(from, BuiltinClasses.TextArg(b, "s.Replace"), StringComparison.Ordinal)));
    }

    [ClassMethod("Repeat")]
    private static RuntimeValue Repeat(RuntimeValue self, RuntimeValue arg) => new StringVal(string.Concat(
        Enumerable.Repeat(((StringVal)self).Value, Math.Max(0, BuiltinClasses.IntArg(arg, "s.Repeat")))));

    [ClassMethod("Reverse")]
    private static RuntimeValue Reverse(RuntimeValue self)
        => new StringVal(new string([.. ((StringVal)self).Value.Reverse()]));

    [ClassMethod("Split")]
    private static RuntimeValue Split(RuntimeValue self, RuntimeValue arg)
    {
        var sep = BuiltinClasses.TextArg(arg, "s.Split");
        if (sep.Length == 0)
            throw new RuntimeException(Messages.T("runtime.string.split_empty_separator"), ErrorKind.Value);
        return new ListVal([.. ((StringVal)self).Value
            .Split(sep, StringSplitOptions.None)
            .Select(p => (RuntimeValue)new StringVal(p))]);
    }

    internal static (int From, int To, bool Reverse) SliceBounds(RangeVal rng, int length, string what, bool clamp = false)
    {
        // 按**元素**那边折算(区间里那些整数):`[1.5..3.5]` 切的是 2..3 那一段。
        // 区间是**降序**的话(`[4..0]`)拿到的是同一段、但**倒着**切 —— 方向也照区间的意思走。
        System.Numerics.BigInteger first, last;
        bool up;
        if (rng.IsBounded())
        {
            (first, last, up) = rng.Walk();
        }
        else if (RangeVal.Infinite(rng.Start, out _) && RangeVal.Infinite(rng.End, out _))
        {
            throw new RuntimeException(Messages.T("runtime.string.slice_both_unbounded"), ErrorKind.Index);
        }
        else if (RangeVal.Infinite(rng.End, out _))
        {
            // `xs[1..]`:一直切到末尾
            (first, up) = rng.Head();
            last = length - 1;
        }
        else
        {
            // `xs[..2]`:从头切到那一头 —— 起点那头的 ∞ 让 `Walk` 折不出来,
            // 但"定得下来的那一端"就是 `Head()` 给的那一格。
            (last, _) = rng.Head();
            first = 0;
            up = true;
        }

        if (up ? first > last : first < last) return (0, 0, false);      // 空区间 → 空的一段

        var lo = System.Numerics.BigInteger.Min(first, last);
        var hi = System.Numerics.BigInteger.Max(first, last);
        if (lo < 0 || hi + 1 > length)
        {
            if (!clamp)
                throw new RuntimeException(Messages.T("runtime.string.slice_out_of_range", what, rng, length), ErrorKind.Index);

            // 切一段**不报错**:越过哪头就收到哪头(`xs.Slice [9..9]` 是空的一段)。
            lo = System.Numerics.BigInteger.Max(lo, 0);
            hi = System.Numerics.BigInteger.Min(hi, length - 1);
            if (lo > hi) return (0, 0, false);
        }

        return ((int)lo, (int)(hi + 1), !up);           // 半开:`hi` 那个元素也要,所以 +1
    }
}
