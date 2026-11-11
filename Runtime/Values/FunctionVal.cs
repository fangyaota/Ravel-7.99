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

    // **`>>` / `<<` 两侧各自开一层作用域**,而"前缀"是"**在这个函数被调之前**先跑"
    // —— 组合里最先被调的是左边那枚,所以前缀只往那边发:
    //
    //     (f >> g).Prepend inj   ==   (f.Prepend inj) >> g
    //     (f >> g).Append  inj   ==   f >> (g.Append inj)
    //
    // 每一层只改动**一端**,链子上的叶子各管自己的那一头;两头发的话同一段前缀会跑两遍。
    // 从前只把 `ComposeVal` 套在组合表面上,`CallScopeOf` 只认 lambda、给出一枚 `Void`,
    // 于是 `inj` 里的 `sc.Define` 当场炸(「类型 'Void' 没有方法 'Define'」)。
    public FunctionVal Prepend(FunctionVal prefix) => this switch
    {
        ControlFunction { Kind: ControlKind.Then } c when c.Args.Count > 0
            => c with { Args = Replace(c.Args, 0, f => f.Prepend(prefix)) },
        _ => new ComposeVal(this, prefix, true),
    };

    public FunctionVal Append(FunctionVal suffix) => this switch
    {
        ControlFunction { Kind: ControlKind.Then } c when c.Args.Count > 0
            => c with { Args = Replace(c.Args, c.Args.Count - 1, f => f.Append(suffix)) },
        _ => new ComposeVal(this, suffix, false),
    };

    // 只换一格,别的不动(链子上其它叶子保持原样)
    private static RList<RuntimeValue> Replace(RList<RuntimeValue> args, int index, Func<FunctionVal, FunctionVal> f)
    {
        var outArgs = RList<RuntimeValue>.Empty;
        for (var i = 0; i < args.Count; i++)
        {
            var a = args.At(i);
            outArgs = outArgs.Add(i == index && a is FunctionVal fn ? f(fn) : a);
        }

        return outArgs;
    }
}
