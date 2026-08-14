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
    public static FunctionVal Curried(Func<RuntimeValue, RuntimeValue, Step> f)
        => FromTrampolined(a1 => Interpreter.ToDone(FromTrampolined(a2 => f(a1, a2))));

    /// <summary>三元版（if 用：if {c} {t} {e} ≡ ((if c) t) e）</summary>
    public static FunctionVal Curried3(Func<RuntimeValue, RuntimeValue, RuntimeValue, Step> f)
        => FromTrampolined(a1 => Interpreter.ToDone(
            FromTrampolined(a2 => Interpreter.ToDone(
                FromTrampolined(a3 => f(a1, a2, a3))))));

    /// <summary>前置执行一个块，再执行本函数（参数原样转发）</summary>
    public FunctionVal Prepend(BlockVal prefix)
    {
        var original = this;
        var bindScope = prefix.Scope;
        return new FunctionVal(bindScope, (scope, a) =>
        {
            var saved = Interpreter.Current!.CurrentScope;
            Interpreter.Current.CurrentScope = scope;
            var pre = Interpreter.Current.EvalBlockExec(prefix.Block);
            return Interpreter.Then(pre, _ =>
            {
                Interpreter.Current.CurrentScope = saved;
                return original.Trampolined(a);
            });
        });
    }

    /// <summary>执行本函数后，追加执行一个块</summary>
    public FunctionVal Append(BlockVal suffix)
    {
        var original = this;
        var bindScope = suffix.Scope;
        return new FunctionVal(bindScope, (scope, a) =>
        {
            var saved = Interpreter.Current!.CurrentScope;
            Interpreter.Current.CurrentScope = scope;
            var body = original.Trampolined(a);
            return Interpreter.Then(body, result =>
            {
                var post = Interpreter.Current.EvalBlockExec(suffix.Block);
                return Interpreter.Then(post, _ =>
                {
                    Interpreter.Current.CurrentScope = saved;
                    return Interpreter.ToDone(result);
                });
            });
        });
    }
}
