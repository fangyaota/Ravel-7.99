namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`Dict` 这个类给它的实例准备什么。**
///
/// 字典自己那几条:按键取/放/问/删,把键、值铺成表,以及"明知可能没有"的 `GetOr`。
/// 序列那一批从 `SequenceMembers` 继承 —— 字典在序列这个身份下给的是**值**
/// (按键找用 `Has` / `Keys ()`)。
///
/// 几条带 `Sys` 前缀的是**底层**名字(`dict.SysGet` 之类):语法的下标写法
/// (`d "k"` / `d "k" = v`)和 `lib/` 里的实现都落到它们身上,普通用户的读法是
/// `d "k"`。前缀就是提醒"这是我让你别直接调的"。
///
/// 从前这些挤在 `RegisterDictMethods` 里;搬过来之后字典有哪些成员就是这个类有哪几个
/// `[ClassMethod]`。</summary>
internal sealed class DictMembers : SequenceMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(DictMembers), ObjectMembers.Set);

    [ClassMethod("SysGet")]
    private static RuntimeValue SysGet(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.KeyArg(arg, "dict.SysGet 的键");
        var d = (DictVal)self;
        if (d.Entries.TryGetValue(key, out var v)) return v;
        throw new RuntimeException($"键不存在: {key}", ErrorKind.Key);
    }

    [ClassMethod("SysSet")]
    private static RuntimeValue SysSet(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.KeyArg(arg, "dict.SysSet 的键");
        return FunctionVal.From(v =>
        {
            ((DictVal)self).Entries[key] = v;
            return VoidVal.Instance;
        });
    }

    [ClassMethod("SysHas")]
    private static RuntimeValue SysHas(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((DictVal)self).Entries.ContainsKey(BuiltinClasses.KeyArg(arg, "dict.SysHas 的键")));

    [ClassMethod("Keys")]
    private static RuntimeValue Keys(RuntimeValue self) => new ListVal([.. ((DictVal)self).Entries.Keys]);

    [ClassMethod("Values")]
    private static RuntimeValue Values(RuntimeValue self) => new ListVal([.. ((DictVal)self).Entries.Values]);

    /// <summary>有没有删掉(C# 也交回 bool)。</summary>
    [ClassMethod("SysRemove")]
    private static RuntimeValue SysRemove(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((DictVal)self).Entries.Remove(BuiltinClasses.KeyArg(arg, "dict.SysRemove 的键")));

    [ClassMethod("HasValue")]
    private static RuntimeValue HasValue(RuntimeValue self, RuntimeValue arg)
        => new BoolVal(((DictVal)self).Entries.ContainsValue(arg));

    /// <summary>`d.GetOr "k" 0` —— 有就取值,没有就给替代值(C# 的 GetValueOrDefault)。
    /// 默认的 `Get` 是**响亮**的(键不存在就报错),这个是"明知可能没有"时用的。
    /// 柯里化:第一参是键,交回的函数再收替代值。</summary>
    [ClassMethod("SysGetOr")]
    private static RuntimeValue SysGetOr(RuntimeValue self, RuntimeValue arg)
    {
        var key = BuiltinClasses.KeyArg(arg, "dict.SysGetOr 的键");
        var d = (DictVal)self;
        return d.Entries.TryGetValue(key, out var v)
            ? FunctionVal.From(_ => v)        // 有:替代值收下但不用
            : FunctionVal.From(fallback => fallback);
    }

    [ClassMethod("Clear")]
    private static RuntimeValue Clear(RuntimeValue self)
    {
        ((DictVal)self).Entries.Clear();
        return VoidVal.Instance;
    }
}
