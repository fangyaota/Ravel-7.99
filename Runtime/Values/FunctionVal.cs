namespace Ravel.Runtime;

/// <summary>函数值——Scope 绑定在实例上，可随时更换。
/// Body 一律是同步的「参数 → 结果」:求值器用帧栈表达延迟,不需要 Step 包装。</summary>
public record FunctionVal : RuntimeValue, IFunction
{
    public Func<RuntimeValue, RuntimeValue> Body { get; set; }
    public virtual string? Name { get; set; }
    public Scope Scope { get; set; }

    public override ObjectVal Type => BuiltinClasses.Function;
    public override string ToString() => Name != null ? "<function " + Name + ">" : "<function>";

    /// <summary>需要捕获作用域时用(scope 参与签名,Body 里可读 Scope)</summary>
    public FunctionVal(Scope scope, Func<Scope, RuntimeValue, RuntimeValue> rawBody)
    {
        Scope = scope;
        Body = a => rawBody(Scope, a);
    }

    /// <summary>单参内置函数(最常见)</summary>
    public static FunctionVal From(Func<RuntimeValue, RuntimeValue> f)
        => new(null!, (_, a) => f(a));

    /// <summary>二元内置函数,柯里化:f a b ≡ (f a) b</summary>
    public static FunctionVal From(Func<RuntimeValue, RuntimeValue, RuntimeValue> f)
        => From(a1 => From(a2 => f(a1, a2)));

    /// <summary>三元版（if 用：if {c} {t} {e} ≡ ((if c) t) e）</summary>
    public static FunctionVal From(Func<RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue> f)
        => From(a1 => From(a2 => From(a3 => f(a1, a2, a3))));

    /// <summary>前置执行一个块，再执行本函数（参数原样转发）→ Compose 控制帧</summary>
    public FunctionVal Prepend(BlockVal prefix) => new ComposeVal(this, prefix, true);

    /// <summary>执行本函数后，追加执行一个块 → Compose 控制帧</summary>
    public FunctionVal Append(BlockVal suffix) => new ComposeVal(this, suffix, false);
}
