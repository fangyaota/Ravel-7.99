namespace Ravel.Extensions;

using System.Xml;
using System.Xml.Linq;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>XML 的**两条**原语 —— 解析、渲染。就这两条,别的全在 `lib/xml.rav` 里。
///
/// 交过来的树是**普通的 dict / list**,不是又一个 `JsonVal` 那样的包装类型:
///
///     元素  {"kind": "element" "name": "a" "attrs": {名字: 值} "kids": [ … ]}
///     文本  {"kind": "text" "text": "…"}
///
/// 于是 Ravel 那一半能直接**看、直接改**(改的就是这棵 dict 树),`Xml.Render` 再把同一形状
/// 打回文本 —— 不需要为"改一个 XML"再发明一套类型,也不必给引擎添一个 `XmlVal`。
/// (对照着看 `Json`:那边是引擎自己包了一棵树,因为 JSON 要参与 `Extract` / `Kind` 那些;
/// XML 这条路上没有那层需求,少一层包装就少一层要记的规矩。)
///
/// **空白文本节点丢掉**:`<a>\n  <b/>\n</a>` 里那些缩进用的空白对写代码的人有意义、
/// 对读数据的人全是噪音,留着的话每一层都得先滤一遍才看得见东西。代价是
/// `<a> </a>` 里那个有意义的空格也一起没了 —— 要就写 `<a xml:space="preserve">` 那种
/// 本来也留不住,所以不假装支持。
///
/// **命名空间整个不参与**:元素和属性都按**局部名**认(`soap:Body` 就是 `Body`),
/// `xmlns:` 那些声明**丢掉**。留一半("前缀算名字的一部分、作用域不管")只会让人猜
/// 哪些名字才是完整的;这门语言里也没有"命名空间"这个概念,不如说清楚:**它不在这儿**。
/// 代价是带命名空间的文档回不去原样(`Xml.Render` 出来是干净的无前缀版本)。
/// 为什么是扩展:落到 `System.Xml.Linq` 上,写不出 Ravel 源码。</summary>
[RavelModule("Native")]
internal static class XmlNative
{
    /// <summary>一段 XML → 一棵树(根元素那个 dict)</summary>
    [RavelFn("XmlParse")]
    public static RuntimeValue XmlParse(RuntimeValue text) => Bad("解析 XML", () =>
    {
        var doc = XDocument.Parse(Text(text, "XmlParse 的文本").Value);
        var root = doc.Root ?? throw Fail("这段 XML 没有根元素");
        // `Node` 对认不出的节点(注释、处理指令)给 null —— 根位置不可能不是元素,
        // 但这儿还是照实接住:让编译器闭嘴的那个 `!` 会在真出岔子时变成空引用
        return Node(root) ?? throw Fail("这段 XML 的根不是元素");
    });

    /// <summary>一棵树 → 文本。**缩进过的**(BCL 的默认):空白文本节点进来时就丢了,
    /// 所以打出来一律是它自己排的版 —— 同一个树每次给同一串,钉得住。</summary>
    [RavelFn("XmlRender")]
    public static RuntimeValue XmlRender(RuntimeValue node) => Bad("渲染 XML", () =>
        new StringVal(Build(Dict(node, "XmlRender 的节点")).ToString()));

    // ── 下面是自己人 ──

    /// <summary>`XNode` → 那两种 dict。认不出的(注释、处理指令)直接不收</summary>
    private static RuntimeValue? Node(XNode n) => n switch
    {
        XElement e => new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("kind")] = new StringVal("element"),
            [new StringVal("name")] = new StringVal(e.Name.LocalName),
            [new StringVal("attrs")] = Attrs(e),
            [new StringVal("kids")] = new ListVal([.. e.Nodes().Select(Node).OfType<RuntimeValue>()]),
        }),
        // `XCData` 排在前面:`XCData : XText`,顺序反了就是"这条永远轮不到"
        XCData c => TextNode("text", c.Value),                 // CDATA 就是文本,只是写法不同
        XText t => TextNode("text", t.Value),
        _ => null,
    };

    private static RuntimeValue TextNode(string kind, string value)
        => new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("kind")] = new StringVal(kind),
            [new StringVal("text")] = new StringVal(value),
        });

    /// <summary>属性表。`xmlns:` 那些**声明**不收 —— 它们是命名空间那套的记账,
    /// 不是数据(见类型注释里那段取舍)</summary>
    private static DictVal Attrs(XElement e)
        => new(e.Attributes().Where(a => !a.IsNamespaceDeclaration).ToDictionary(
            a => (RuntimeValue)new StringVal(a.Name.LocalName),
            a => (RuntimeValue)new StringVal(a.Value)));

    /// <summary>那两种 dict → `XNode`</summary>
    private static XNode Build(DictVal d)
    {
        if (OptText(d, "kind", "element") == "text") return new XText(OptText(d, "text", ""));

        var name = OptText(d, "name", "");
        if (name.Length == 0) throw Fail("XmlRender: 元素没有名字");

        // **不走 `XName.Get`**:那个认的是 `{命名空间}局部名` 那套写法,而这儿的前缀
        // 就是名字的一部分(`soap:Body` 原样进、原样出)。
        var e = new XElement(name);
        if (Opt(d, "attrs") is DictVal attrs)
            foreach (var (k, v) in attrs.Entries)
                e.SetAttributeValue(Text(k, "属性名").Value, Show(v));

        if (Opt(d, "kids") is ListVal kids)
            foreach (var kid in kids.Elements)
                e.Add(Build(Dict(kid, "XmlRender 的子节点")));

        return e;
    }

    /// <summary>XML 那一批的兜底:`XmlException` 是"这段文本不是 XML",别的多是编码/IO那类</summary>
    private static RuntimeValue Bad(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is XmlException or FormatException or ArgumentException
                                   or InvalidOperationException)
        {
            // `XmlException` 的消息里带行号列号,是**有用的**,原样带上
            throw Fail($"{what}失败: {ex.Message}");
        }
    }
}
