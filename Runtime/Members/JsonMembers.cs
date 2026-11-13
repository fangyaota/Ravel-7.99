namespace Ravel.Runtime;

using System.Linq;
using Newtonsoft.Json.Linq;
using Ravel.Runtime.Values;

internal static class JsonMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(JsonMembers), ObjectMembers.Set);

    [ClassMethod("Kind")]
    private static RuntimeValue Kind(RuntimeValue self) => new StringVal(((JsonVal)self).Token.Type switch
    {
        JTokenType.Object => "object",
        JTokenType.Array => "array",
        JTokenType.String => "string",
        JTokenType.Integer or JTokenType.Float => "number",
        JTokenType.Boolean => "bool",
        _ => "null",
    });

    [ClassMethod("IsNull")]
    private static RuntimeValue IsNull(RuntimeValue self) => new BoolVal(
        ((JsonVal)self).Token.Type is JTokenType.Null or JTokenType.Undefined);

    [ClassMethod("Get")]
    private static RuntimeValue Get(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.TextOf(arg, "Json.Get 的键");
        return ((JsonVal)self).Token is JObject o && o.TryGetValue(key, out var child)
            ? new JsonVal(child)
            : throw new RuntimeException($"Json.Get: 没有这个键 '{key}'（对象里有 {BuiltinClasses.KeysOf((JsonVal)self).Count} 个键）", ErrorKind.Key);
    }

    [ClassMethod("GetOr")]
    private static RuntimeValue GetOr(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.TextOf(arg, "Json.GetOr 的键");
        return FunctionVal.From(dflt =>
            ((JsonVal)self).Token is JObject o && o.TryGetValue(key, out var child)
                ? new JsonVal(child)
                : BuiltinClasses.JsonOf(dflt));
    }

    [ClassMethod("Slice")]
    private static RuntimeValue Slice(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not RangeVal rng)
            throw new RuntimeException($"Json.Slice 收一个区间（`j[1..3]`），得到 {arg.Type}", ErrorKind.Argument);

        var arr = ((JsonVal)self).Token as JArray
            ?? throw new RuntimeException($"Json.Slice: 这是 {((JsonVal)self).Token.Type}，不是数组", ErrorKind.Type);
        var (from, to, reverse) = StringMembers.SliceBounds(rng, arr.Count, "Json.Slice");
        IEnumerable<JToken> items = arr.Skip(from).Take(to - from);
        if (reverse) items = items.Reverse();
        return new JsonVal(new JArray([.. items.Select(t => t.DeepClone())]));
    }

    [ClassMethod("At")]
    private static RuntimeValue At(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is RangeVal)
            throw new RuntimeException(
                $"Json.At 收一个下标（切一段用 Json.Slice —— `j[1..3]` 就走它），得到区间 {arg}",
                ErrorKind.Argument);

        var i = BuiltinClasses.IntArg(arg, "Json.At");
        var arr = ((JsonVal)self).Token as JArray
            ?? throw new RuntimeException($"Json.At: 这是 {((JsonVal)self).Token.Type}，不是数组", ErrorKind.Type);
        if (i < 0 || i >= arr.Count)
            throw new RuntimeException($"Json.At: 索引 {i} 超出范围（长度 {arr.Count}）", ErrorKind.Index);
        return new JsonVal(arr[i]);
    }

    [ClassMethod("Count")]
    private static RuntimeValue Count(RuntimeValue self) => IntVal.Of(((JsonVal)self).Token switch
    {
        JObject o => o.Count,
        JArray a => a.Count,
        var t => throw new RuntimeException($"Json.Count: 这是 {t.Type}，没有「个数」", ErrorKind.Type),
    });

    [ClassMethod("Keys")]
    private static RuntimeValue Keys(RuntimeValue self) => new ListVal([
        .. BuiltinClasses.KeysOf((JsonVal)self).Select(k => (RuntimeValue)new StringVal(k))]);

    [ClassMethod("Text")]
    private static RuntimeValue Text(RuntimeValue self, RuntimeValue arg) => new StringVal(arg is IntVal n
        ? BuiltinClasses.WriteJson(((JsonVal)self).Token, n.Value)
        : BuiltinClasses.WriteJson(((JsonVal)self).Token, null));

    [ClassMethod("Extract")]
    private static RuntimeValue Extract(RuntimeValue self) => BuiltinClasses.FromJson(((JsonVal)self).Token);

    // ── 改 ──
    // 这层原本只有"看"的成员 —— 「先看再转」,改是 `Extract ()` 那一步的事。加这几条是因为
    // **改一个字段别让整棵树往返一趟**(Extract → 改 → `Json` 再包回去);
    // 而且底下那棵树(`JObject` / `JArray`)本来就是可变的,不给它一条路只是白白绕远。
    //
    // 交出去的**值**是原生值(走 `ToJson`,和 `Json v` 同一张换算表),也可以直接给一个
    // Json 值。改的是**这一个 Json 值自己**(和 dict / list 一样按身份看),不是那份原始 dict ——
    // `Json v` 建的时候就把树拷出来了。

    [ClassMethod("Set")]
    private static RuntimeValue Assign(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.TextOf(arg, "Json.Set 的键");
        return FunctionVal.From(v =>
        {
            BuiltinClasses.ObjOf(self, "Set")[key] = BuiltinClasses.ToToken(v);
            return VoidVal.Instance;
        });
    }

    [ClassMethod("SetAt")]
    private static RuntimeValue SetAt(RuntimeValue self, RuntimeValue arg)
    {
        var i = BuiltinClasses.IntArg(arg, "Json.SetAt");
        return FunctionVal.From(v =>
        {
            BuiltinClasses.ArrOf(self, "SetAt")[BuiltinClasses.InRange(self, i, "Json.SetAt")] = BuiltinClasses.ToToken(v);
            return VoidVal.Instance;
        });
    }

    [ClassMethod("Add")]
    private static RuntimeValue Add(RuntimeValue self, RuntimeValue arg)
    {
        BuiltinClasses.ArrOf(self, "Add").Add(BuiltinClasses.ToToken(arg));
        return VoidVal.Instance;
    }

    [ClassMethod("Remove")]
    private static RuntimeValue Remove(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(BuiltinClasses.ObjOf(self, "Remove").Remove(BuiltinClasses.TextOf(arg, "Json.Remove 的键")));

    [ClassMethod("RemoveAt")]
    private static RuntimeValue RemoveAt(RuntimeValue self, RuntimeValue arg)
    {
        var i = BuiltinClasses.IntArg(arg, "Json.RemoveAt");
        BuiltinClasses.ArrOf(self, "RemoveAt").RemoveAt(BuiltinClasses.InRange(self, i, "Json.RemoveAt"));
        return VoidVal.Instance;
    }
}
