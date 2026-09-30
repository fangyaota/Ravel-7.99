using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ravel.Runtime;

/// <summary>一个 **JSON 值** —— 里面包着一棵 Newtonsoft 的 <see cref="JToken"/> 树。
/// 形状和 <see cref="ListVal"/> 一样:`members` 喂基类,不占数据字段(数据字段只有 `Token`)。
///
/// 为什么不是"解析出来直接变成 dict / list":那样 null、整数与小数的写法、键的顺序
/// 都得在解析那一刻被迫选一次。留着这棵树就**先看再转** —— 用 `Get` / `At` / `Count` /
/// `Kind` 看,想好了再 `Extract ()` 拿原生值。
///
/// 为什么是 `JToken` 而不是 `JObject`/`JArray`:任何 JSON 片段都算一个 Json 值
/// (`Json.FromString "3"` 是个数),`JToken` 是它们的公共基类。
///
/// `JToken` **不用 Dispose**(不是 IDisposable),所以这里不必持有别的活东西。</summary>
public record JsonVal : ObjectVal
{
    public JToken Token { get; init; }

    public JsonVal(JToken token, Scope? members = null)
        : base(BuiltinClasses.Json, members ?? new Scope())
        => Token = token;

    /// <summary>显示成**紧凑的 JSON 文本**(和 `t.Text ()` 一样)——
    /// `print j` 一眼看得出结构,而不是 `Json {...}` 那种字段快照。</summary>
    public override string ToString() => Token.ToString(Formatting.None);
}
