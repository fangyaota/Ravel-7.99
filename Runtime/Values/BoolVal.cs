namespace Ravel.Runtime;

/// <summary>bool 值。它**必须是 FunctionVal** —— 类型表里 `Bool &lt;: Function`
/// (true/false 可调用:`true {a} {b}` 选一个块跑),值这边不跟上就会出现
/// 「类型系统说有这个方法、值却接不住」:走 inherits 查到 Function 的
/// Name/scope/prepend,拿到 self 是 BoolVal,`(FunctionVal)s` 直接抛
/// InvalidCastException —— 它不是 RuntimeException,Ravel 的 try 接不住,程序被打掉。
/// BlockVal 一直是这么接的,BoolVal 是漏掉的那个。
/// Body 不会被调用:求值器在 CallInto 里按类型分派(见 BoolVal 分支)。
///
/// **相等性按值**:`true` 与 `1 == 1` 算出来的 true 必须是同一个东西。
/// 不写这两句就会落到 `ObjectVal` 的身份比较上(record 合成的那个更早是被
/// `FunctionVal` 里那个每次新建的捕获委托挡住的),于是 `{true}.Contains (1 == 1)`
/// 给 false —— 而 `{1 2 3}.Contains (1 + 2)` 给 true,同一个语言里两套答案。
/// `HashSet` 先比哈希,所以 `Equals` 和 `GetHashCode` 两句都要。</summary>
public record BoolVal : FunctionVal
{
    public bool Value { get; init; }

    public BoolVal(bool value) : base(null!, (_, _) => VoidVal.Instance)
    {
        // 基类把 ClassType 填成了 `Function`(那是给普通函数用的),bool 的元类是 `Bool` ——
        // **必须显式覆盖**,`??=` 在这里不管用(ClassType 已经不是 null 了)。
        ClassType = BuiltinClasses.Bool;
        Value = value;
    }

    /// <summary>元类是 `Bool`。这里只是兜底:静态初始化期间 `BuiltinClasses.Bool` 可能还没造出来。</summary>
    public override ObjectVal Type => ClassType ??= BuiltinClasses.Bool;

    /// <summary>按值比 —— 见类文档。</summary>
    public virtual bool Equals(BoolVal? other) => other is not null && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString().ToLower();
}
