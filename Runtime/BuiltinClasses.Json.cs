using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ravel.Runtime;

/// <summary>JSON —— 原生值 ⇄ `JsonVal`(里面那棵 `JToken` 树)。
///
/// 单独一个 partial 文件:换算表加那一串成员比较长,挤进 `Methods.cs` 不好读。
///
/// 两个入口方向分明:
///   `Json v`            原生值(dict / list / 值类型 / ())→ Json
///   `Json.FromString s` 字符串 → Json(唯一的解析入口)
/// 反过来只有一条:`.Extract ()`。
/// </summary>
internal static partial class BuiltinClasses
{
    /// <summary>嵌套上限:防"自己包含自己"的容器(`d.Set "self" d` 之后 `Json d`)
    /// 把栈转爆 —— 和 Newtonsoft 解析那边的 `MaxDepth` 一个量级。</summary>
    private const int MaxJsonDepth = 64;

    /// <summary>`TextArg` 的一个口子 —— 它在 `Methods.cs` 里是 private,而 Json 的成员
    /// 已经搬到 `Runtime/Members/JsonMembers.cs` 去了。收法和消息都还是 `TextArg` 那份。</summary>
    internal static string TextOf(RuntimeValue a, string what) => TextArg(a, what);

    /// <summary>原生值 → JToken。**JSON 里没有的东西当场报错**(自有类、分数、函数……),
    /// 不悄悄降级成字符串 —— 那会让往返丢形状、而且没人发现。
    /// (`JsonMembers` 也用它:交进去的值和 `Json v` 走同一张换算表。)</summary>
    internal static JToken ToJson(RuntimeValue v, int depth = 0)
    {
        if (depth > MaxJsonDepth)
            throw new RuntimeException($"Json: 嵌套超过 {MaxJsonDepth} 层（是不是有容器包含自己？）", ErrorKind.Value);

        return v switch
        {
            DictVal d => new JObject(d.Entries.Select(kv =>
                new JProperty(((StringVal)kv.Key).Value, ToJson(kv.Value, depth + 1)))),
            ListVal l => new JArray(l.Elements.Select(x => ToJson(x, depth + 1))),
            SetVal s => new JArray(s.Elements.Select(x => ToJson(x, depth + 1))),
            IntVal i => new JValue(i.Value),
            RealVal f => new JValue(f.Value),
            BigIntVal b => new JValue(b.Value),          // 原样写数字,不经 double、不丢精度
            StringVal s => new JValue(s.Value),
            CharVal c => new JValue(c.Value.ToString()),
            BoolVal b => new JValue(b.Value),
            VoidVal => JValue.CreateNull(),
            _ => throw new RuntimeException($"Json: {v.Type} 在 JSON 里没有对应"
                + "（先自己转成 dict / list / 数 / 字符串 / 布尔 / ()）", ErrorKind.Value),
        };
    }

    /// <summary>构造器 = 转换器:`Json {"a": 1}` 把原生值包成 Json 值 ——
    /// 于是 `x: Json = {"a": 1}` 这种隐式转换也顺带有了(和 `int` / `string` 一个路子)。</summary>
    private static RuntimeValue CastToJson(RuntimeValue v)
        => v is JsonVal j ? j : new JsonVal(ToJson(v));

    /// <summary>JToken → 原生值。`null` 是 **`()`**(语言级的"没有值")。
    /// 想区分"值是 null"和"没有这个键"就在 Json 那层问 `IsNull ()` / `Kind ()` ——
    /// `Extract ()` 是"我确定要原生值"的那一步。</summary>
    internal static RuntimeValue FromJson(JToken t) => t switch
    {
        JObject o => new DictVal(o.Properties().ToDictionary(
            p => (RuntimeValue)new StringVal(p.Name), p => FromJson(p.Value))),
        JArray a => new ListVal([.. a.Select(FromJson)]),
        JValue v => v.Type switch
        {
            JTokenType.Integer => AsInt(v.Value),
            JTokenType.Float => new RealVal(Convert.ToDouble(v.Value)),
            JTokenType.String => new StringVal(v.Value as string ?? ""),
            JTokenType.Boolean => new BoolVal(v.Value is true),
            JTokenType.Null or JTokenType.Undefined => VoidVal.Instance,
            _ => throw new RuntimeException($"Json.Extract: {v.Type} 不知道怎么转成 Ravel 值", ErrorKind.Value),
        },
        _ => throw new RuntimeException($"Json.Extract: {t.Type} 不知道怎么转成 Ravel 值", ErrorKind.Value),
    };

    /// <summary>JSON 的整数:Ravel 侧装得下就是 `int`,装不下退成 `bigint`
    /// (和数字字面量一个口径)。</summary>
    private static RuntimeValue AsInt(object? o) => o switch
    {
        int i => IntVal.Of(i),
        long l => l is >= int.MinValue and <= int.MaxValue ? IntVal.Of((int)l) : new BigIntVal(l),
        ulong u => u <= int.MaxValue ? IntVal.Of((int)u) : new BigIntVal(u),
        System.Numerics.BigInteger bi => bi >= int.MinValue && bi <= int.MaxValue
            ? IntVal.Of((int)bi) : new BigIntVal(bi),
        _ => throw new RuntimeException($"Json.Extract: 认不出的整数 {o}", ErrorKind.Value),
    };

    /// <summary>`Json.FromString s` —— 解析。**不用 `JToken.Parse`**:那个不给调
    /// `DateParseHandling`(不关掉的话 `"2020-01-01"` 会被悄悄变成日期),也看不到结尾还有没有
    /// 多余的东西。</summary>
    private static JsonVal ParseJson(string text)
    {
        using var reader = new JsonTextReader(new StringReader(text))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
        };
        try
        {
            var token = JToken.ReadFrom(reader);
            if (reader.Read())                           // 一个 JSON 文档只该有一个值
                throw new RuntimeException($"Json.FromString: 第一个值后面还有东西（位置 {reader.LineNumber}:{reader.LinePosition}）", ErrorKind.Value);
            return new JsonVal(token);
        }
        catch (JsonReaderException ex)
        {
            // ⚠️ 它不在 `Fs` 的 catch 白名单里 —— 不自己接住的话会当成"解释器内部错误"
            // 把程序打掉 Ravel 的 try 接不住(见 Ravel.Extensions/MathNative.cs 那段注释)
            throw new RuntimeException(
                $"Json.FromString: 第 {ex.LineNumber + 1} 行第 {ex.LinePosition + 1} 列读不动 —— {ex.Message}", ErrorKind.Value);
        }
    }

    /// <summary>写回文本:`indent` 为空(即 `t.Text ()`)就紧凑,给了格数就缩进。</summary>
    internal static string WriteJson(JToken t, int? indent)
    {
        if (indent is not { } n) return t.ToString(Formatting.None);

        using var sw = new StringWriter();
        using (var w = new JsonTextWriter(sw)
        {
            Formatting = Formatting.Indented,
            Indentation = n,
            IndentChar = ' ',
        })
        {
            t.WriteTo(w);
        }

        return sw.ToString();
    }

    internal static JsonVal JsonOf(RuntimeValue v) => new(ToJson(v));

    /// <summary>Json **类对象自己**的成员 —— 眼下只有 `Json.FromString s`。
    ///
    /// 给**实例**的那十几条(`Kind` / `Get` / `At` / `Set` ……)已经搬到
    /// `Runtime/Members/JsonMembers.cs`:每个类自己带一份成员工厂,`MemberSet.Scan`
    /// 反射一次收齐。它们进的是 `Json` 的**实例表**(实例读得到 `j.Kind ()`、
    /// 类对象那一侧读不到 `Json.Kind`),和"类对象自己的表"是两张表,见
    /// `ClassVal.InstanceTable`。
    ///
    /// (从前这批成员是**每个 Json 值一张表**(`NewJsonMembers`):那时类那一侧没法拦,
    /// `Json.Kind` 会被解析成"类上的方法",调用时 `self` 是那个 `ClassVal`、方法体里
    /// `((JsonVal)s)` 当场炸成 `!! 解释器内部错误 InvalidCastException`。
    /// 两张表分开之后那条理由就没了 —— 而每个值一张表是要付钱的:`Get` / `At` 每取一次子节点
    /// 就造一个新 Json 值,每个都带一份 Scope + 十来个 BuiltinMethodVal。
    /// 现在和容器那批(`List` / `Set` / `Dict`)一个形状。)</summary>
    private static void RegisterJsonMethods()
    {
        // 唯一的解析入口:挂在**类对象**上(即 `Json.FromString s`,和 `Type.Default` 那条同款)——
        // 读它的就是类对象自己,所以进类那张表(`ClassSideMember`),不进给实例的表
        ClassSideMember(Json, "FromString",
            new BuiltinMethodVal((_, a) => ParseJson(TextArg(a, "Json.FromString"))));

        // 给实例的那十几条(Kind / IsNull / Get / GetOr / At / Count / Keys / Text / Extract /
        // Set / SetAt / Add / Remove / RemoveAt)都在 `Runtime/Members/JsonMembers.cs` ——
        // 和 `Object` 的成员一个写法:静态方法 + `[ClassMethod]`,由 `MemberSet.Scan` 一次收齐。
    }

    internal static List<string> KeysOf(JsonVal j) => j.Token is JObject o
        ? [.. o.Properties().Select(p => p.Name)]
        : throw new RuntimeException($"Json.Keys: 这是 {j.Token.Type}，不是对象", ErrorKind.Type);

    internal static JObject ObjOf(RuntimeValue s, string what) => ((JsonVal)s).Token as JObject
        ?? throw new RuntimeException($"Json.{what}: 这是 {((JsonVal)s).Token.Type}，不是对象", ErrorKind.Type);

    internal static JArray ArrOf(RuntimeValue s, string what) => ((JsonVal)s).Token as JArray
        ?? throw new RuntimeException($"Json.{what}: 这是 {((JsonVal)s).Token.Type}，不是数组", ErrorKind.Type);

    /// <summary>下标越界当场报错(读的那条 `At` 就是这么办的),交回那个下标本身好接着用</summary>
    internal static int InRange(RuntimeValue s, int i, string what)
    {
        var n = ((JArray)((JsonVal)s).Token).Count;
        if (i < 0 || i >= n)
            throw new RuntimeException($"{what}: 索引 {i} 超出范围（长度 {n}）", ErrorKind.Index);
        return i;
    }

    /// <summary>要写进去的那个值:Json 值原样用它的树,别的走 `ToJson`(和 `Json v` 一张表)。
    /// 给不出来的(自有类、函数……)在那儿当场报错,不悄悄降级。</summary>
    internal static JToken ToToken(RuntimeValue v) => v is JsonVal j ? j.Token : ToJson(v);
}
