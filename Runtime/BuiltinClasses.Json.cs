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

    /// <summary>原生值 → JToken。**JSON 里没有的东西当场报错**(自有类、分数、函数……),
    /// 不悄悄降级成字符串 —— 那会让往返丢形状、而且没人发现。</summary>
    private static JToken ToJson(RuntimeValue v, int depth = 0)
    {
        if (depth > MaxJsonDepth)
            throw new RuntimeException($"Json: 嵌套超过 {MaxJsonDepth} 层（是不是有容器包含自己？）");

        return v switch
        {
            DictVal d => new JObject(d.Entries.Select(kv =>
                new JProperty(((StringVal)kv.Key).Value, ToJson(kv.Value, depth + 1)))),
            ListVal l => new JArray(l.Elements.Select(x => ToJson(x, depth + 1))),
            SetVal s => new JArray(s.Elements.Select(x => ToJson(x, depth + 1))),
            IntVal i => new JValue(i.Value),
            FloatVal f => new JValue(f.Value),
            BigIntVal b => new JValue(b.Value),          // 原样写数字,不经 double、不丢精度
            StringVal s => new JValue(s.Value),
            CharVal c => new JValue(c.Value.ToString()),
            BoolVal b => new JValue(b.Value),
            VoidVal => JValue.CreateNull(),
            _ => throw new RuntimeException($"Json: {v.Type} 在 JSON 里没有对应"
                + "（先自己转成 dict / list / 数 / 字符串 / 布尔 / ()）"),
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
            JTokenType.Float => new FloatVal(Convert.ToDouble(v.Value)),
            JTokenType.String => new StringVal(v.Value as string ?? ""),
            JTokenType.Boolean => new BoolVal(v.Value is true),
            JTokenType.Null or JTokenType.Undefined => VoidVal.Instance,
            _ => throw new RuntimeException($"Json.Extract: {v.Type} 不知道怎么转成 Ravel 值"),
        },
        _ => throw new RuntimeException($"Json.Extract: {t.Type} 不知道怎么转成 Ravel 值"),
    };

    /// <summary>JSON 的整数:Ravel 侧装得下就是 `int`,装不下退成 `bigint`
    /// (和数字字面量一个口径)。</summary>
    private static RuntimeValue AsInt(object? o) => o switch
    {
        int i => new IntVal(i),
        long l => l is >= int.MinValue and <= int.MaxValue ? new IntVal((int)l) : new BigIntVal(l),
        ulong u => u <= int.MaxValue ? new IntVal((int)u) : new BigIntVal(u),
        System.Numerics.BigInteger bi => bi >= int.MinValue && bi <= int.MaxValue
            ? new IntVal((int)bi) : new BigIntVal(bi),
        _ => throw new RuntimeException($"Json.Extract: 认不出的整数 {o}"),
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
                throw new RuntimeException($"Json.FromString: 第一个值后面还有东西（位置 {reader.LineNumber}:{reader.LinePosition}）");
            return new JsonVal(token);
        }
        catch (JsonReaderException ex)
        {
            // ⚠️ 它不在 `Fs` 的 catch 白名单里 —— 不自己接住的话会当成"解释器内部错误"
            // 把程序打掉 Ravel 的 try 接不住(见 Interpreter.Math.cs 那段注释)
            throw new RuntimeException(
                $"Json.FromString: 第 {ex.LineNumber + 1} 行第 {ex.LinePosition + 1} 列读不动 —— {ex.Message}");
        }
    }

    /// <summary>写回文本:`indent` 为空(即 `t.Text ()`)就紧凑,给了格数就缩进。</summary>
    private static string WriteJson(JToken t, int? indent)
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

    private static JsonVal JsonOf(RuntimeValue v) => new(ToJson(v));

    /// <summary>Json 那批成员(按类型分组,和别处一样)</summary>
    /// <summary>注册 `Json` 这个**类型**上的东西。只有一条:解析。
    ///
    /// 注意**不是** `Json.DefineMethod (…)` 一下子把实例方法也挂上去 —— 见
    /// <see cref="NewJsonMembers"/> 那段说明。</summary>
    private static void RegisterJsonMethods()
    {
        // 唯一的解析入口:挂在**类对象**上(即 `Json.FromString s`,和 `Type.Default` 那条同款)
        Json.DefineMethod("FromString", (_, a) => ParseJson(TextArg(a, "Json.FromString")));
    }

    /// <summary>一个 Json 值**自己的**成员表 —— 那批实例方法定义在这儿。
    ///
    /// **为什么不挂到类上**(像 `List.DefineMethod("Add", …)` 那样):类对象的成员表就是
    /// **类自己的那张 scope**,于是从类对象上也能读到 —— `Json.Kind` 会被解析成"类上的
    /// 方法",调用时 `self` 是那个 `ClassVal`,方法体里 `((JsonVal)s)` 当场炸成
    /// `!! 解释器内部错误 InvalidCastException`(C# 异常,Ravel 的 `try` 接不住)。
    ///
    /// 而 `JsonVal` 是 `ObjectVal`,**本来就有自己的表**(`ObjectVal.Scope`)——
    /// 实例方法就该放这儿:实例读得到 ✓、类对象读不到(于是 `Json.Kind` 是干净利落的
    /// 「没有方法」)✓、每个值一张表也意味着 `j.tag := 1` 只影响那一个值 ✓。
    ///
    /// (容器那批没这么办:`List`/`Set`/`Dict` 的值是程序里最常造的东西,给每个值定义
    /// 十几个成员太亏,所以它们的实例方法留在类上 —— 那边 `list.Add 2` 一样的炸法,
    /// 是同一件事的另一个取舍。)</summary>
    internal static Scope NewJsonMembers()
    {
        var scope = new Scope();
        void Def(string name, Func<RuntimeValue, RuntimeValue, RuntimeValue> impl)
            => scope.DefineOrReplace(name, Function, new BuiltinMethodVal(impl) { Name = name });

        Def("Kind", (s, _) => new StringVal(((JsonVal)s).Token.Type switch
        {
            JTokenType.Object => "object",
            JTokenType.Array => "array",
            JTokenType.String => "string",
            JTokenType.Integer or JTokenType.Float => "number",
            JTokenType.Boolean => "bool",
            _ => "null",
        }));

        Def("IsNull", (s, _) => new BoolVal(
            ((JsonVal)s).Token.Type is JTokenType.Null or JTokenType.Undefined));

        Def("Get", (s, a) =>
        {
            var key = TextArg(a, "Json.Get 的键");
            return ((JsonVal)s).Token is JObject o && o.TryGetValue(key, out var child)
                ? new JsonVal(child)
                : throw new RuntimeException($"Json.Get: 没有这个键 '{key}'（对象里有 {KeysOf((JsonVal)s).Count} 个键）");
        });

        // `t.GetOr "k" 0` —— 没有就给替代值;替代值也包成 Json,免得"取到的"和"给的"两种形状
        Def("GetOr", (s, a) =>
        {
            var key = TextArg(a, "Json.GetOr 的键");
            return FunctionVal.From(dflt =>
                ((JsonVal)s).Token is JObject o && o.TryGetValue(key, out var child)
                    ? new JsonVal(child)
                    : JsonOf(dflt));
        });

        Def("At", (s, a) =>
        {
            var i = IntArg(a, "Json.At");
            var arr = ((JsonVal)s).Token as JArray
                ?? throw new RuntimeException($"Json.At: 这是 {((JsonVal)s).Token.Type}，不是数组");
            if (i < 0 || i >= arr.Count)
                throw new RuntimeException($"Json.At: 索引 {i} 超出范围（长度 {arr.Count}）");
            return new JsonVal(arr[i]);
        });

        Def("Count", (s, _) => new IntVal(((JsonVal)s).Token switch
        {
            JObject o => o.Count,
            JArray a => a.Count,
            var t => throw new RuntimeException($"Json.Count: 这是 {t.Type}，没有「个数」"),
        }));

        Def("Keys", (s, _) => new ListVal([
            .. KeysOf((JsonVal)s).Select(k => (RuntimeValue)new StringVal(k))]));

        // `t.Text ()` 紧凑(实参是 ());`t.Text 2` 缩进两格
        Def("Text", (s, a) => new StringVal(a is IntVal n
            ? WriteJson(((JsonVal)s).Token, n.Value)
            : WriteJson(((JsonVal)s).Token, null)));

        Def("Extract", (s, _) => FromJson(((JsonVal)s).Token));

        return scope;
    }

    private static List<string> KeysOf(JsonVal j) => j.Token is JObject o
        ? [.. o.Properties().Select(p => p.Name)]
        : throw new RuntimeException($"Json.Keys: 这是 {j.Token.Type}，不是对象");
}
