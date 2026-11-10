namespace Ravel.Runtime;

using System.Linq;
using Newtonsoft.Json.Linq;
using Ravel.Runtime.Values;

/// <summary>**`Json` 这个类给它的实例准备什么。**
///
/// 从前这批成员在 `BuiltinClasses.Json.cs` 的 `RegisterJsonMethods()` 里一条条
/// `Json.DefineMethod(…)` 挂上去(那是一张外置的注册表)。现在和 `ObjectMembers` 一个写法:
/// 成员就在**这个类**里,静态方法 + `[ClassMethod]`,由 `MemberSet.Scan` 反射一次收齐。
///
/// 实例方法进的是 `Json` 的**实例表**:实例读得到(`j.Kind ()`)、类对象那一侧读不到
/// (`Json.Kind` → 「类型 'Type' 没有方法 'Kind'」)。唯一的解析入口 `Json.FromString`
/// 是**类对象自己**的成员,不在这儿 —— 它还在 `BuiltinClasses.Json.cs`(`ClassSideMember`)。
///
/// 换算表(原生值 ⇄ `JToken`)也留在 `BuiltinClasses.Json.cs`:那是 `Json v` / `Extract ()`
/// 共用的一道换算,不属于"某个类的实例",`Initializers` 那边也在用(`CastToJson`)。
///
/// (从前这批成员是**每个 Json 值一张表**(`NewJsonMembers`):那时类那一侧没法拦,
/// `Json.Kind` 会被解析成"类上的方法",调用时 `self` 是那个 `ClassVal`、方法体里
/// `((JsonVal)s)` 当场炸成 `!! 解释器内部错误 InvalidCastException`。
/// 两张表分开之后那条理由就没了 —— 而每个值一张表是要付钱的:`Get` / `At` 每取一次子节点
/// 就造一个新 Json 值,每个都带一份 Scope + 十来个 BuiltinMethodVal。
/// 现在和容器那批(`List` / `Set` / `Dict`)一个形状。)</summary>
internal static class JsonMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(JsonMembers), ObjectMembers.Set);

    /// <summary>这棵树是哪种 JSON 形状:`"object"` / `"array"` / `"string"` / `"number"` /
    /// `"bool"` / `"null"`。`Extract ()` 之前先用它看清楚手里是什么。</summary>
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

    /// <summary>JSON 的 null 和 Ravel 的 `()` 是同一个东西。想知道"这个键的值是不是 null"
    /// 就问这一条 —— 别等 `Extract ()` 之后,那时"没有这个键"也一样是 `()`。</summary>
    [ClassMethod("IsNull")]
    private static RuntimeValue IsNull(RuntimeValue self) => new BoolVal(
        ((JsonVal)self).Token.Type is JTokenType.Null or JTokenType.Undefined);

    /// <summary>`t.Get "k"` —— 按名字取一个子节点。取不到是**错误**(不是 `()`):
    /// "没有这个键"和"值是 null"得能分开,想容错就用 `GetOr`。</summary>
    [ClassMethod("Get")]
    private static RuntimeValue Get(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.TextOf(arg, "Json.Get 的键");
        return ((JsonVal)self).Token is JObject o && o.TryGetValue(key, out var child)
            ? new JsonVal(child)
            : throw new RuntimeException($"Json.Get: 没有这个键 '{key}'（对象里有 {BuiltinClasses.KeysOf((JsonVal)self).Count} 个键）", ErrorKind.Key);
    }

    /// <summary>`t.GetOr "k" 0` —— 没有就给替代值;替代值也包成 Json,免得"取到的"和"给的"两种形状。</summary>
    [ClassMethod("GetOr")]
    private static RuntimeValue GetOr(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.TextOf(arg, "Json.GetOr 的键");
        return FunctionVal.From(dflt =>
            ((JsonVal)self).Token is JObject o && o.TryGetValue(key, out var child)
                ? new JsonVal(child)
                : BuiltinClasses.JsonOf(dflt));
    }

    /// <summary>`t.At 3` —— 数组的第 n 个。越界当场报错(和下标的取法一个口径:
    /// 空的和"没有第 n 个"都得说得清)。</summary>
    [ClassMethod("At")]
    private static RuntimeValue At(RuntimeValue self, RuntimeValue arg)
    {
        var i = BuiltinClasses.IntArg(arg, "Json.At");
        var arr = ((JsonVal)self).Token as JArray
            ?? throw new RuntimeException($"Json.At: 这是 {((JsonVal)self).Token.Type}，不是数组", ErrorKind.Type);
        if (i < 0 || i >= arr.Count)
            throw new RuntimeException($"Json.At: 索引 {i} 超出范围（长度 {arr.Count}）", ErrorKind.Index);
        return new JsonVal(arr[i]);
    }

    /// <summary>对象几个键 / 数组几个元素。别的东西(一个数、一个串)**没有"个数"**,当场报错。</summary>
    [ClassMethod("Count")]
    private static RuntimeValue Count(RuntimeValue self) => IntVal.Of(((JsonVal)self).Token switch
    {
        JObject o => o.Count,
        JArray a => a.Count,
        var t => throw new RuntimeException($"Json.Count: 这是 {t.Type}，没有「个数」", ErrorKind.Type),
    });

    /// <summary>对象的键(按 JSON 文本里的顺序)。数组没有键 —— 要下标就用 `At`。</summary>
    [ClassMethod("Keys")]
    private static RuntimeValue Keys(RuntimeValue self) => new ListVal([
        .. BuiltinClasses.KeysOf((JsonVal)self).Select(k => (RuntimeValue)new StringVal(k))]);

    /// <summary>`t.Text ()` 紧凑(实参是 `()`);`t.Text 2` 缩进两格。</summary>
    [ClassMethod("Text")]
    private static RuntimeValue Text(RuntimeValue self, RuntimeValue arg) => new StringVal(arg is IntVal n
        ? BuiltinClasses.WriteJson(((JsonVal)self).Token, n.Value)
        : BuiltinClasses.WriteJson(((JsonVal)self).Token, null));

    /// <summary>**唯一的出口**:这棵树 → 原生值(dict / list / 数 / 串 / 布尔 / `()`)。</summary>
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

    /// <summary>`t.Set "k" v` —— 改/加一个键。交回 `()`。</summary>
    [ClassMethod("Set")]
    private static RuntimeValue SysSet(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.TextOf(arg, "Json.Set 的键");
        return FunctionVal.From(v =>
        {
            BuiltinClasses.ObjOf(self, "Set")[key] = BuiltinClasses.ToToken(v);
            return VoidVal.Instance;
        });
    }

    /// <summary>`t.SetAt 2 v` —— 改第 n 个元素。交回 `()`。</summary>
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

    /// <summary>数组尾巴上接一个(`Add` 只对数组说得通;对象那边是 `Set`)。</summary>
    [ClassMethod("Add")]
    private static RuntimeValue Add(RuntimeValue self, RuntimeValue arg)
    {
        BuiltinClasses.ArrOf(self, "Add").Add(BuiltinClasses.ToToken(arg));
        return VoidVal.Instance;
    }

    /// <summary>删一个键 / 删一个下标 —— 交回"有没有删掉"(和 `dict.Remove` 一个口径)。</summary>
    [ClassMethod("Remove")]
    private static RuntimeValue Remove(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(BuiltinClasses.ObjOf(self, "Remove").Remove(BuiltinClasses.TextOf(arg, "Json.Remove 的键")));

    /// <summary>删第 n 个元素。交回 `()`(和 `SetAt` 对称)。</summary>
    [ClassMethod("RemoveAt")]
    private static RuntimeValue RemoveAt(RuntimeValue self, RuntimeValue arg)
    {
        var i = BuiltinClasses.IntArg(arg, "Json.RemoveAt");
        BuiltinClasses.ArrOf(self, "RemoveAt").RemoveAt(BuiltinClasses.InRange(self, i, "Json.RemoveAt"));
        return VoidVal.Instance;
    }
}
