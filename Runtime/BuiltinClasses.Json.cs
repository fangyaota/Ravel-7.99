using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ravel.Runtime;

internal static partial class BuiltinClasses
{
    private const int MaxJsonDepth = 64;

    internal static string TextOf(RuntimeValue a, string what) => TextArg(a, what);

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

    private static RuntimeValue CastToJson(RuntimeValue v)
        => v is JsonVal j ? j : new JsonVal(ToJson(v));

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

    private static RuntimeValue AsInt(object? o) => o switch
    {
        int i => IntVal.Of(i),
        long l => l is >= int.MinValue and <= int.MaxValue ? IntVal.Of((int)l) : new BigIntVal(l),
        ulong u => u <= int.MaxValue ? IntVal.Of((int)u) : new BigIntVal(u),
        System.Numerics.BigInteger bi => bi >= int.MinValue && bi <= int.MaxValue
            ? IntVal.Of((int)bi) : new BigIntVal(bi),
        _ => throw new RuntimeException($"Json.Extract: 认不出的整数 {o}", ErrorKind.Value),
    };

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

    internal static int InRange(RuntimeValue s, int i, string what)
    {
        var n = ((JArray)((JsonVal)s).Token).Count;
        if (i < 0 || i >= n)
            throw new RuntimeException($"{what}: 索引 {i} 超出范围（长度 {n}）", ErrorKind.Index);
        return i;
    }

    internal static JToken ToToken(RuntimeValue v) => v is JsonVal j ? j.Token : ToJson(v);
}
