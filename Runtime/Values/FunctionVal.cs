namespace Ravel.Runtime;

/// <summary>函数值——Scope 绑定在实例上，可随时更换</summary>
public record FunctionVal : RuntimeValue
{
    public Func<RuntimeValue[], Step> Trampolined { get; set; }
    public virtual string? Name { get; set; }
    public Scope Scope { get; set; }

    public override RuntimeType Type => RuntimeType.Function;
    public override string ToString() => Name != null ? "<function " + Name + ">" : "<function>";

    public FunctionVal(Scope scope, Func<Scope, RuntimeValue[], Step> rawBody)
    {
        Scope = scope;
        Trampolined = args => rawBody(Scope, args);
    }

    public static FunctionVal FromDirect(Func<RuntimeValue[], RuntimeValue> f)
        => new(null!, (_, args) => new Done(f(args)));

    public static FunctionVal FromTrampolined(Func<RuntimeValue[], Step> f)
        => new(null!, (_, args) => f(args));

    /// <summary>前置执行一个块，再执行本函数（参数原样转发）</summary>
    public FunctionVal Prepend(BlockVal prefix)
    {
        var original = this;
        var bindScope = prefix.Scope;
        return new FunctionVal(bindScope, (scope, args) =>
        {
            var saved = Interpreter.Current!.CurrentScope;
            Interpreter.Current.CurrentScope = scope;
            var pre = Interpreter.Current.EvalBlockExec(prefix.Block);
            return Interpreter.Then(pre, _ =>
            {
                Interpreter.Current.CurrentScope = saved;
                return original.Trampolined(args);
            });
        });
    }

    /// <summary>执行本函数后，追加执行一个块</summary>
    public FunctionVal Append(BlockVal suffix)
    {
        var original = this;
        var bindScope = suffix.Scope;
        return new FunctionVal(bindScope, (scope, args) =>
        {
            var saved = Interpreter.Current!.CurrentScope;
            Interpreter.Current.CurrentScope = scope;
            var body = original.Trampolined(args);
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
