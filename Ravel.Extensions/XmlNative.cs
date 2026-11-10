namespace Ravel.Extensions;

using System.Xml;
using System.Xml.Linq;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Native")]
internal static class XmlNative
{
    [RavelFn("XmlParse")]
    public static RuntimeValue XmlParse(RuntimeValue text) => Bad("解析 XML", () =>
    {
        var doc = XDocument.Parse(Text(text, "XmlParse 的文本").Value);
        var root = doc.Root ?? throw Fail("这段 XML 没有根元素");
        // `Node` 对认不出的节点(注释、处理指令)给 null —— 根位置不可能不是元素,
        // 但这儿还是照实接住:让编译器闭嘴的那个 `!` 会在真出岔子时变成空引用
        return Node(root) ?? throw Fail("这段 XML 的根不是元素");
    });

    [RavelFn("XmlRender")]
    public static RuntimeValue XmlRender(RuntimeValue node) => Bad("渲染 XML", () =>
        new StringVal(Build(Dict(node, "XmlRender 的节点")).ToString()));

    // ── 下面是自己人 ──

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

    private static DictVal Attrs(XElement e)
        => new(e.Attributes().Where(a => !a.IsNamespaceDeclaration).ToDictionary(
            a => (RuntimeValue)new StringVal(a.Name.LocalName),
            a => (RuntimeValue)new StringVal(a.Value)));

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
