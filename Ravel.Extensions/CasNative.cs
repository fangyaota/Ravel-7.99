namespace Ravel.Extensions;

using AngouriMath;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Cas")]
internal static class CasNative
{
    // ============================================================
    //  帮手(`CasExprClass` 那边共用 —— 所以是 internal)
    // ============================================================

    internal static string Str(RuntimeValue v, string what) => Text(v, what).Value;

    internal static Entity Parse(RuntimeValue v, string what) => ParseText(Str(v, what));

    internal static Entity ParseText(string src)
        => MathS.Parse(src).Switch(
            e => e,
            f => throw Fail($"Cas: 解析不了 {src} —— {f}"));

    internal static Entity.Variable Var(RuntimeValue v, string what) => MathS.Var(Str(v, what));

    internal static Entity Get(RuntimeValue v, string what) => v switch
    {
        CasExprVal e => e.E,
        IntVal or RealVal or BigIntVal or FractionVal or BigFractionVal => ParseText(Show(v)),
        _ => Parse(v, what),
    };

    internal static RuntimeValue Wrap(Entity e) => new CasExprVal(e);

    internal static RuntimeValue Guard(Func<RuntimeValue> f)
    {
        try { return f(); }
        catch (RuntimeException) { throw; }
        catch (Exception ex) { throw Fail($"Cas: {ex.Message}"); }
    }

    // ============================================================
    //  化简 / 变形
    // ============================================================

    [RavelFn("Simplify")] public static RuntimeValue Simplify(RuntimeValue s)
        => Guard(() => Wrap(Get(s, "Simplify").Simplify()));

    [RavelFn("Expand")] public static RuntimeValue Expand(RuntimeValue s)
        => Guard(() => Wrap(Get(s, "Expand").Expand()));

    [RavelFn("Factor")] public static RuntimeValue Factor(RuntimeValue s)
        => Guard(() => Wrap(Get(s, "Factor").Factorize()));

    // ============================================================
    //  微积分
    // ============================================================

    [RavelFn("Diff")] public static RuntimeValue Diff(RuntimeValue s, RuntimeValue v)
        => Guard(() => Wrap(Get(s, "Diff").Differentiate(Var(v, "Diff"))));

    [RavelFn("DiffN")] public static RuntimeValue DiffN(RuntimeValue s, RuntimeValue v, RuntimeValue n)
        => Guard(() => Wrap(Get(s, "DiffN").Differentiate(Var(v, "DiffN"), Int(n, "DiffN"))));

    [RavelFn("Integrate")] public static RuntimeValue Integrate(RuntimeValue s, RuntimeValue v)
        => Guard(() => Wrap(Get(s, "Integrate").Integrate(Var(v, "Integrate"))));

    [RavelFn("Limit")] public static RuntimeValue Limit(RuntimeValue s, RuntimeValue v, RuntimeValue at)
        => Guard(() => Wrap(Get(s, "Limit").Limit(Var(v, "Limit"), Parse(at, "Limit"))));

    // ============================================================
    //  解方程 / 代入
    // ============================================================

    [RavelFn("Solve")] public static RuntimeValue Solve(RuntimeValue s, RuntimeValue v)
        => Guard(() => Wrap(Get(s, "Solve").Solve(Var(v, "Solve"))));

    [RavelFn("Subst")] public static RuntimeValue Subst(RuntimeValue s, RuntimeValue v, RuntimeValue val)
        => Guard(() => Wrap(Get(s, "Subst").Substitute(Var(v, "Subst"), Parse(val, "Subst"))));

    // ============================================================
    //  出去:数值 / LaTeX
    // ============================================================

    [RavelFn("Eval")] public static RuntimeValue Eval(RuntimeValue s)
        => Guard(() => new RealVal(Get(s, "Eval").EvalNumerical().RealPart.AsDouble()));

    [RavelFn("Latex")] public static RuntimeValue Latex(RuntimeValue s)
        => Guard(() => new StringVal(Get(s, "Latex").Latexize()));
}
