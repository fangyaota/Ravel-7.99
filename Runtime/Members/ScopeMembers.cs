namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

internal static class ScopeMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ScopeMembers), ObjectMembers.Set);

    [ClassMethod("Push")]
    private static RuntimeValue Push(RuntimeValue self)
    {
        var scope = ((ScopeVal)self).Inner;
        return new ScopeVal(scope.Push());
    }

    [ClassMethod("Define")]
    private static RuntimeValue Define(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not StringVal name)
            throw new RuntimeException("scope.Define 需要字符串名称", ErrorKind.Argument);
        var scope = ((ScopeVal)self).Inner;
        return FunctionVal.From(tv =>
        {
            if (tv is not ObjectVal t)
                throw new RuntimeException("scope.Define 需要 type 参数", ErrorKind.Argument);
            scope.Define(name.Value, VoidVal.Instance);
            return VoidVal.Instance;
        });
    }

    [ClassMethod("Lookup")]
    private static RuntimeValue Lookup(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not StringVal name)
            throw new RuntimeException("scope.Lookup 需要字符串参数", ErrorKind.Argument);
        return Wrap(((ScopeVal)self).Inner.Lookup(name.Value));
    }

    [ClassMethod("Remove")]
    private static RuntimeValue Remove(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not StringVal name)
            throw new RuntimeException("scope.Remove 需要字符串名称", ErrorKind.Argument);
        ((ScopeVal)self).Inner.RemoveHere(name.Value);
        return VoidVal.Instance;
    }

    [ClassMethod("Keys")]
    private static RuntimeValue Keys(RuntimeValue self)
        => new ListVal([.. ((ScopeVal)self).Inner.MemberNames.Select(n => (RuntimeValue)new StringVal(n))]);

    [ClassMethod("Values")]
    private static RuntimeValue Values(RuntimeValue self)
        => new ListVal([.. ((ScopeVal)self).Inner.Variables.Select(kv => Wrap(kv.Value))]);

    [ClassMethod("Count")]
    private static RuntimeValue Count(RuntimeValue self) => IntVal.Of(((ScopeVal)self).Inner.MemberNames.Count());

    [ClassMethod("Variables")]
    private static RuntimeValue Variables(RuntimeValue self)
    {
        var scope = ((ScopeVal)self).Inner;
        var d = new Dictionary<RuntimeValue, RuntimeValue>();
        foreach (var kv in scope.Variables)
        {
            if (kv.Key is ObjectVal.ThisMember or ObjectVal.BlockMember or ObjectVal.ThisTypeMember) continue;
            d[new StringVal(kv.Key)] = Wrap(kv.Value);      // 变量名本来就是字符串
        }

        return new DictVal(d);
    }

    private static PropertyVal Wrap(Variable vr)
        => new(
            FunctionVal.From(_ => vr.Value),
            FunctionVal.From(v =>
            {
                vr.Assign(v);
                return VoidVal.Instance;
            }),
            vr);
}
