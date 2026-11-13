namespace Ravel.Runtime;

using System.Linq;

internal static partial class BuiltinClasses
{

    private static void RegisterMethods()
    {
        RegisterJsonMethods();
        // 其余各内置类的方法已经搬进 `Runtime/Members/*.cs`(每类一份,跟着类走):
        // `Object`/`Ravel` → ObjectMembers,`String` → StringMembers,`Char` → CharMembers,
        // `Range` → RangeMembers,`Type` → TypeMembers,`Function` → FunctionMembers,
        // `Scope` → ScopeMembers,`Property` → PropertyMembers,
        // `list`/`set`/`dict` 的序列方法 → SequenceMembers(三种容器共用一份实现)。
        // 那些名字现在由 `ClassVal.Members` 那份成员工厂给出,这里不再注册。
        // 高阶序列方法(`SeqMethod`)与类型查询(`TraitQuery`)也一样 —— 见
        // `SequenceMembers` / `TypeMembers` 里那些 `[ClassValue]` 字段。
    }

    // `Object` / `Ravel` 的成员搬到了 `Runtime/Members/ObjectMembers.cs`（按类型自己管）。
    // 这里不再有它们的注册 —— 那些名字由 `ClassVal.Members` 那份成员工厂给出。

    internal static string TextArg(RuntimeValue a, string what) => a switch
    {
        StringVal s => s.Value,
        CharVal c => c.Value.ToString(),
        _ => throw new RuntimeException($"{what} 需要 string 或 char 参数，得到 {a.Type}", ErrorKind.Argument),
    };

    internal static bool CanBeKey(ObjectVal t) => t is ClassVal c && c.IsValueLike;

    // 字典 / 集合的键不再挑类型:任何值都能当键,比较口径是 `===`(见 SameComparer)。
    internal static RuntimeValue KeyArg(RuntimeValue a, string _what) => a;

    // 扩展自己的字典还要"能按值比"的键(图顶点那种) —— 这条仍旧挑值类型。
    internal static RuntimeValue StrictKeyArg(RuntimeValue a, string what)
        => CanBeKey(a.Type)
            ? a
            : throw new RuntimeException($"{what}得是值类型（数 / 串 / 字符 / 布尔 / `()`），得到 {a.Type}", ErrorKind.Type);

    internal static PropertyVal DefaultProperty()
        => new(FunctionVal.From(_ => EmptyFunction()), FunctionVal.From(_ => VoidVal.Instance));

    internal static FunctionVal EmptyFunction() => FunctionVal.From(_ => VoidVal.Instance);

    private static RuntimeValue Rebind(RuntimeValue value, Scope dst) => value switch
    {
        LambdaVal lam => lam with { CaptureScope = dst },
        PropertyVal pv => pv with { Getter = RebindFn(pv.Getter, dst), Setter = RebindFn(pv.Setter, dst) },
        _ => value,
    };

    private static FunctionVal RebindFn(FunctionVal f, Scope dst) => f switch
    {
        LambdaVal lam => lam with { CaptureScope = dst },
        BlockVal blk => blk with { CaptureScope = dst },
        _ => f,
    };

    internal static RuntimeValue CopyValue(RuntimeValue v) => v switch
    {
        // 容器的成员表跟着副本走 —— 和 CopyObject 一个道理(它们也是 ObjectVal)
        ListVal l => CopyTableInto(new ListVal([.. l.Elements]), l),
        SetVal s => CopyTableInto(new SetVal([.. s.Elements]), s),
        DictVal d => CopyTableInto(new DictVal(new Dictionary<RuntimeValue, RuntimeValue>(d.Entries)), d),
        // Json 值:**浅拷**——那棵 `JToken` 树一起用(Ravel 这边没有改它的手段,`Get`/`At`
        // 也是包个新 Json 指向同一棵树),要拷的是**成员表**(副本上 `tag = …` 才找得到)
        JsonVal j => CopyTableInto(new JsonVal(j.Token), j),
        // 必须排在 ObjectVal 之前:这四类现在也是 ObjectVal
        FunctionVal or ModuleVal or PropertyVal or ScopeVal => v,
        ObjectVal o => CopyObject(o),
        _ => v,
    };

    internal static bool HasOwnTable(RuntimeValue v)
        => v is ObjectVal and not (FunctionVal or ModuleVal or PropertyVal or ScopeVal);

    internal static ObjectVal CopyObject(ObjectVal src)
        => CopyTableInto(new ObjectVal(src.ClassType, null), src);

    private static T CopyTableInto<T>(T copy, ObjectVal src) where T : ObjectVal
    {
        var dst = copy.Scope;
        // 源表可能没有词法父(顶层对象):这时副本也就不挂父,别拿 null 去 Reparent
        if (src.Scope.Parent is { } lexical) dst.Reparent(lexical);
        foreach (var kv in src.Scope.Variables)
        {
            var value = Rebind(kv.Value.Value, dst);
            // attrs 也要带过去:丢了 by 的话副本上的 `v = x` 不走 setter,
            // 丢了 readonly/private/core 就等于副本绕过了这些约束
            var v = dst.DefineOrReplace(kv.Key, value);
            foreach (var a in kv.Value.Attrs) v.SetAttr(a);
        }

        dst.DefineOrReplace(ObjectVal.ThisMember, copy);
        return copy;
    }
}
