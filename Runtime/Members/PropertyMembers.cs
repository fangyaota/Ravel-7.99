namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

internal static class PropertyMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(PropertyMembers), ObjectMembers.Set);

    [ClassMethod("Attrs")]
    private static RuntimeValue Attrs(RuntimeValue self)
        => new ListVal([.. (((PropertyVal)self).Var?.Attrs ?? []).Select(a => new StringVal(a))]);
}
