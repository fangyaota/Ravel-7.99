using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ravel.Runtime;

public record JsonVal : ObjectVal
{
    public JToken Token { get; init; }

    public JsonVal(JToken token, Scope? members = null)
        : base(BuiltinClasses.Json, members)
        => Token = token;

    public override string ToString() => Token.ToString(Formatting.None);
}
