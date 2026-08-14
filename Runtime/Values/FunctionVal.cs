namespace Ravel.Runtime;

/// <summary>函数值——Scope 绑定在实例上，可随时更换</summary>
public record FunctionVal : RuntimeValue
{
    public Func<RuntimeValue, Step> Trampolined { get; set; }
    public virtual string? Name { get; set; }
    public Scope Scope { get; set; }

    public override RuntimeType Type => RuntimeType.Function;
    public override string ToString() => Name != null ? "<function " + Name + ">" : "<function>";

    public FunctionVal(Scope scope, Func<Scope, RuntimeValue, Step> rawBody)
    {
        Scope = scope;
        Trampolined = a => rawBody(Scope, a);
    }

    public static FunctionVal FromDirect(Func<RuntimeValue, RuntimeValue> f)
        => new(null!, (_, a) => new Done(f(a)));

    public static FunctionVal FromTrampolined(Func<RuntimeValue, Step> f)
        => new(null!, (_, a) => f(a));

    /// <summary>把二元内置函数包装成可柯里化调用的 FunctionVal（f a b ≡ (f a) b）</summary>
    public static FunctionVal FromTrampolined2(Func<RuntimeValue, RuntimeValue, Step> f)
        => FromTrampolined(a1 => Interpreter.ToDone(FromTrampolined(a2 => f(a1, a2))));

    /// <summary>三元版（if 用：if {c} {t} {e} ≡ ((if c) t) e）</summary>
    public static FunctionVal FromTrampolined3(Func<RuntimeValue, RuntimeValue, RuntimeValue, Step> f)
        => FromTrampolined(a1 => Interpreter.ToDone(
            FromTrampolined(a2 => Interpreter.ToDone(
                FromTrampolined(a3 => f(a1, a2, a3))))));

    /// <summary>前置执行一个块，再执行本函数（参数原样转发）→ Compose 控制帧</summary>
    public FunctionVal Prepend(BlockVal prefix) => new ComposeVal(this, prefix, true);

    /// <summary>执行本函数后，追加执行一个块 → Compose 控制帧</summary>
    public FunctionVal Append(BlockVal suffix) => new ComposeVal(this, suffix, false);
}
