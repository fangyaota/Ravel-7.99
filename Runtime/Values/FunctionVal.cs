namespace Ravel.Runtime;

public record FunctionVal : ObjectVal
{
    public Func<RuntimeValue, RuntimeValue> Body { get; set; }

    public Scope CaptureScope { get; set; }

    public override ObjectVal Type => ClassType ??= BuiltinClasses.Function;

    public override string ToString() => Name != null ? "<function " + Name + ">" : "<function>";

    public bool IsPartial { get; set; }

    public FunctionVal(Scope captureScope, Func<Scope, RuntimeValue, RuntimeValue> rawBody, Scope? members = null)
        : base(null!, members)
    {
        // 基类 ctor 的 `?? this` 是给根元类 `type` 留的自指,函数得改回 Function。
        // (ClassVal 的 ctor 随后会把它改成元类。)
        ClassType = BuiltinClasses.Function;
        CaptureScope = captureScope;
        Body = a => rawBody(CaptureScope, a);
    }

    public static RuntimeValue PlaceholderBody(string what)
        => throw new RuntimeException($"{what} 的 Body 不该被调用 —— 它由求值器按类型分派(见 CallInto)，这里漏了一个 case");

    public static FunctionVal From(Func<RuntimeValue, RuntimeValue> f)
        => new(null!, (_, a) => f(a));

    public static FunctionVal Direct(Func<RuntimeValue, RuntimeValue> f)
        => new(null!, (_, a) => f(a)) { Body = f };

    public static FunctionVal Direct(Func<RuntimeValue, RuntimeValue, RuntimeValue> f)
        => Direct(a1 => Partial(a2 => f(a1, a2)));

    public static FunctionVal Direct(Func<RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue> f)
        => Direct(a1 => Partial(a2 => Partial(a3 => f(a1, a2, a3))));

    public static FunctionVal From(Func<RuntimeValue, RuntimeValue, RuntimeValue> f)
        => From(a1 => Partial(a2 => f(a1, a2)));

    public static FunctionVal From(Func<RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue> f)
        => From(a1 => Partial(a2 => Partial(a3 => f(a1, a2, a3))));

    private static FunctionVal Partial(Func<RuntimeValue, RuntimeValue> f)
        => new(null!, (_, a) => f(a)) { IsPartial = true };

    public FunctionVal Prepend(FunctionVal prefix) => new ComposeVal(this, prefix, true);

    public FunctionVal Append(FunctionVal suffix) => new ComposeVal(this, suffix, false);
}
