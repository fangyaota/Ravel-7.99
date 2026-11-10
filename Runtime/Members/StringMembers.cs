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
        var v = ((StringVal)self).Value;
        var i = BuiltinClasses.IntArg(arg, "s.At");
        if (i < 0 || i >= v.Length)
            throw new RuntimeException($"s.At: 索引 {i} 超出范围（长度 {v.Length}）", ErrorKind.Index);
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
        if (i < 0) throw new RuntimeException($"s.IndexOf: 找不到 {BuiltinClasses.TextArg(arg, "s.IndexOf")}", ErrorKind.Value);
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
        if (i < 0) throw new RuntimeException($"s.LastIndexOf: 找不到 {BuiltinClasses.TextArg(arg, "s.LastIndexOf")}", ErrorKind.Value);
        return IntVal.Of(i);
    }

    // ── 切 ──

    [ClassMethod("Slice")]
    private static RuntimeValue Slice(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not RangeVal rng)
            throw new RuntimeException($"s.Slice 收一个区间（如 `s.Slice [1..3)`），得到 {arg.Type}", ErrorKind.Argument);

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
            throw new RuntimeException("s.Split: 分隔符不能是空串（想逐个字符就用 s.Chars ()）", ErrorKind.Value);
        return new ListVal([.. ((StringVal)self).Value
            .Split(sep, StringSplitOptions.None)
            .Select(p => (RuntimeValue)new StringVal(p))]);
    }

    private static (int From, int To, bool Reverse) SliceBounds(RangeVal rng, int length, string what)
    {
        // 按**元素**那边折算(区间里那些整数):`[1.5..3.5]` 切的是 2..3 那一段。
        // 区间是**降序**的话(`[4..0]`)拿到的是同一段、但**倒着**切 —— 方向也照区间的意思走。
        var (first, last, up) = rng.Walk();
        if (up ? first > last : first < last) return (0, 0, false);      // 空区间 → 空的一段

        var lo = System.Numerics.BigInteger.Min(first, last);
        var hi = System.Numerics.BigInteger.Max(first, last);
        if (lo < 0 || hi + 1 > length)
            throw new RuntimeException($"{what}: {rng} 越界（长度 {length}）", ErrorKind.Index);

        return ((int)lo, (int)(hi + 1), !up);           // 半开:`hi` 那个元素也要,所以 +1
    }
}
