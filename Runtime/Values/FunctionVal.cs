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

    // **`>>` / `<<` 没有"一个"调用作用域** —— 两侧各自开一层,所以前缀/后缀要发到
    // 每一枚叶子上,各拿一份自己的作用域。不发下去的话,`(say << parse).Prepend inj`
    // 落在 `ComposeVal` 上,`CallScopeOf` 只认 lambda、给出一枚 `Void`,
    // `inj` 里的 `sc.Define` 当场炸。
    //
    // 已经套起来的 `ComposeVal` 不再往下发 —— 那是"这一层的前缀",该紧贴着它跑一次;
    // 发进两侧会让同一段前缀跑两遍(实参、副作用都跟着变)。
    public FunctionVal Prepend(FunctionVal prefix) => this switch
    {
        ControlFunction { Kind: ControlKind.Then } c => new ControlFunction(ControlKind.Then, 1, Spread(c.Args, f => f.Prepend(prefix))),
        _ => new ComposeVal(this, prefix, true),
    };

    public FunctionVal Append(FunctionVal suffix) => this switch
    {
        ControlFunction { Kind: ControlKind.Then } c => new ControlFunction(ControlKind.Then, 1, Spread(c.Args, f => f.Append(suffix))),
        _ => new ComposeVal(this, suffix, false),
    };

    private static RList<RuntimeValue> Spread(RList<RuntimeValue> args, Func<FunctionVal, FunctionVal> f)
    {
        var outArgs = RList<RuntimeValue>.Empty;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args.At(i);
            outArgs = outArgs.Add(a is FunctionVal fn ? f(fn) : a);
        }

        return outArgs;
    }
}
