namespace Ravel.Runtime;

using System.Linq;
using Ravel.Runtime.Values;

/// <summary>**`Scope` 这个类给它的实例准备什么。**
///
/// `scope` 就是那层"名字 → 变量"的表:推一层(`Push`)、写一个(`Define`)、读一个
/// (`Lookup`)、删本层一个(`Remove`),再有几个**看里面有什么**的问法
/// (`Keys` / `Values` / `Count` / `Variables`)。
///
/// `Lookup` / `Values` / `Variables` 交回的东西都把变量**包成 property**
/// (读走 getter、写走 setter)而不是直接摊一个值 —— 于是"改它"和"改原来那个变量"
/// 是同一件事,不必再提供一个 `Set`。包法只有一处(`Wrap`),从前 `Lookup` 和
/// `Variables` 各写了一遍。
///
/// `Remove` 的语义值得单说:和 `Lookup` 相反,**找不到不报错**,当"本来就没有" ——
/// `Remove` 这个名字说的就是这个,而且 `IDict` 的 `Remove` 槽要靠它。
///
/// 从前这一批在 `RegisterScopeMethods` 里。</summary>
internal static class ScopeMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ScopeMembers), ObjectMembers.Set);

    /// <summary>推一层新的(词法父是当前这层),交回**新的 scope**,不改自己。</summary>
    [ClassMethod("Push")]
    private static RuntimeValue Push(RuntimeValue self)
    {
        var scope = ((ScopeVal)self).Inner;
        return new ScopeVal(scope.Push());
    }

    /// <summary>柯里化:先收名字,交回的函数再收**类型对象**,写进去的值是 `()`。
    /// 收类型(不是实例值)是刻意的 —— 作用域里存的是"这个名字声明的类型"。</summary>
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
            scope.Define(name.Value, t, VoidVal.Instance);
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

    /// <summary>**删掉本层的一个名字** —— `IDict` 的 `Remove` 槽要它(作用域从前只有增改,没有删)。
    /// 和 `Lookup` 不同:找不到**不报错**,当"本来就没有" —— `Remove` 的语义就是这个。</summary>
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

    /// <summary>整张表铺成字典。**跳过 `this` / `block` / `type` 那三个机制名** ——
    /// 它们不是用户写的变量,铺进去只会让 `Variables ()` 的结果看起来比实际多三个
    /// (而且值是闭包,打印出来还是一大坨)。</summary>
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

    /// <summary>把一个变量包成 property(getter 读、setter 写),attrs 原样带上。
    /// `scope.Lookup` 和 `scope.Variables` 都要这一套 —— 从前各写了一遍。
    ///
    /// 直接收 `Variable`(而不是名字)是故意的:`Variables` 正在遍历的那份变量就是它,
    /// 再 `Lookup` 一次等于把同一个名字查两遍(而且是沿链查,不是查那层那一格)。</summary>
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
