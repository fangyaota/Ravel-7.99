namespace Ravel.Extensions;

using AngouriMath;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>Cas 模块 —— **符号计算**(计算机代数),底层是 **AngouriMath 2.5.0**(MIT)。
///
/// 这是"外部的 CAS 怎么进 Ravel"那条路的样子:一个官方扩展,`[RavelModule("Cas")]`,
/// 由 `lib/native.rav` 那句 `using "plugins/Ravel.Extensions.dll"` 装进来 ——
/// 和 `Math` / `Sqlite` / `Hash` 那几格**走的是同一条路**,引擎那边一个字没改。
///
/// **这一版是"字符串进、字符串出"**:表达式没有变成 Ravel 的值类型 ——
/// `Entity` 没有抱进一个新的 `RuntimeValue` 子类里。所以是 `Cas.Diff "x^3" "x"` 而不是
/// `(x^3).Diff x`。先把通路跑通;真要当库用,下一步是让 `Cas.Parse` 交回一个 Ravel 值、
/// 把 `Simplify` / `Diff` 做成它的成员(那要新加一个 `RuntimeValue`,是另一笔)。
///
/// 三条约定(和 `MathNative` 一个路子):
/// - **入参是字符串**:表达式用 AngouriMath 自己那套语法(`x^2 + 2x + 1` / `sin(x)` /
///   `x^3 - 1/3`),不是 Ravel 的语法。`ToString ()` 交回的也是那一套(能再喂回去)。
/// - **`Eval` 是唯一的"回到数"**:别的都交字符串。它交 **real**
///   (`AngouriMath` 那边算完的数值)。
/// - **异常一律就地翻成 `RuntimeException`**:AngouriMath 自己抛的是
///   `ParseException` / `ArgumentException` 那些,而 Ravel 的 `try` **只接得住
///   `RuntimeException`** —— 不拦的话用户包不住,进程直接被打掉(同 `MathNative` 那条)。</summary>
[RavelModule("Cas")]
internal static class CasNative
{
    // ============================================================
    //  帮手
    // ============================================================

    private static string Str(RuntimeValue v, string what) => Text(v, what).Value;

    /// <summary>剥字符串 → 表达式。**2.5.0 的 `MathS.Parse` 交的是
    /// `Either<Entity, Failure<…>>`** —— 解析在**类型上**就可失败。走 `Switch` 拆:
    /// 右边那半直接翻成 `RuntimeException`,把人话给出来。
    ///
    /// **别图省事用那个显式转换**(`(Entity)parsed`):它拆失败时抛的是
    /// `InvalidCastException`,消息是英文的 "Specified cast is not valid" ——
    /// 用户看到那句话完全不知道是自己式子写错了。</summary>
    private static Entity Expr(RuntimeValue v, string what)
    {
        var src = Str(v, what);
        return MathS.Parse(src).Switch(
            e => e,
            f => throw Fail($"Cas: 解析不了 {src} —— {f}"));
    }

    private static Entity.Variable Var(RuntimeValue v, string what) => MathS.Var(Str(v, what));

    /// <summary>交给 Ravel:按 AngouriMath 那套语法打印(能再 `Parse` 回去)。</summary>
    private static RuntimeValue Out(Entity e) => new StringVal(e.ToString());

    /// <summary>这几种失败 C# 侧抛的不是 `RuntimeException`,Ravel 的 `try` 接不住 —— 就地翻。</summary>
    private static RuntimeValue Guard(Func<RuntimeValue> f)
    {
        try { return f(); }
        catch (RuntimeException) { throw; }
        catch (Exception ex) { throw Fail($"Cas: {ex.Message}"); }
    }

    // ============================================================
    //  化简 / 变形
    // ============================================================

    [RavelFn("Simplify")] public static RuntimeValue Simplify(RuntimeValue s)
        => Guard(() => Out(Expr(s, "Simplify").Simplify()));

    [RavelFn("Expand")] public static RuntimeValue Expand(RuntimeValue s)
        => Guard(() => Out(Expr(s, "Expand").Expand()));

    [RavelFn("Factor")] public static RuntimeValue Factor(RuntimeValue s)
        => Guard(() => Out(Expr(s, "Factor").Factorize()));

    // ============================================================
    //  微积分
    // ============================================================

    /// <summary>求导 —— 对第二个参数那个变量(`Cas.Diff "x^3" "x"` → `3 * x^2`)。</summary>
    [RavelFn("Diff")] public static RuntimeValue Diff(RuntimeValue s, RuntimeValue v)
        => Guard(() => Out(Expr(s, "Diff").Differentiate(Var(v, "Diff"))));

    /// <summary>求高阶导 —— 第三个参数是阶数(`Cas.DiffN "x^3" "x" 2` → `6 * x`)。</summary>
    [RavelFn("DiffN")] public static RuntimeValue DiffN(RuntimeValue s, RuntimeValue v, RuntimeValue n)
        => Guard(() => Out(Expr(s, "DiffN").Differentiate(Var(v, "DiffN"), Int(n, "DiffN"))));

    /// <summary>不定积分(不带常数项)。</summary>
    [RavelFn("Integrate")] public static RuntimeValue Integrate(RuntimeValue s, RuntimeValue v)
        => Guard(() => Out(Expr(s, "Integrate").Integrate(Var(v, "Integrate"))));

    [RavelFn("Limit")] public static RuntimeValue Limit(RuntimeValue s, RuntimeValue v, RuntimeValue at)
        => Guard(() => Out(Expr(s, "Limit").Limit(Var(v, "Limit"), Expr(at, "Limit"))));

    // ============================================================
    //  解方程 / 代入
    // ============================================================

    /// <summary>解方程 —— 交回的是一**组**解(AngouriMath 那套集合写法,`{ … }`)。
    ///
    /// 用的是 `Solve` 不是 `SolveEquation`:后者只认"能反解出单个值"的形状,
    /// `x^2 = 4` 这种会报「Inverting this node would need a set-valued answer」——
    /// 而多解恰恰是解方程最常碰到的。</summary>
    [RavelFn("Solve")] public static RuntimeValue Solve(RuntimeValue s, RuntimeValue v)
        => Guard(() => Out(Expr(s, "Solve").Solve(Var(v, "Solve"))));

    /// <summary>把式子里的变量换成另一个式子(`Cas.Subst "x^2" "x" "y+1"`)。</summary>
    [RavelFn("Subst")] public static RuntimeValue Subst(RuntimeValue s, RuntimeValue v, RuntimeValue val)
        => Guard(() => Out(Expr(s, "Subst").Substitute(Var(v, "Subst"), Expr(val, "Subst"))));

    // ============================================================
    //  出去:数值 / LaTeX
    // ============================================================

    /// <summary>算成**数**(交 real)—— 唯一不交字符串的那条。式子里的常数(π / e)也认。
    ///
    /// `EvalNumerical ()` 交的是 `Entity.Number.Complex`,取它的实部 ——
    /// 纯实数的式子虚部是 0,取实部就是答案。</summary>
    [RavelFn("Eval")] public static RuntimeValue Eval(RuntimeValue s)
        => Guard(() => new RealVal(Expr(s, "Eval").EvalNumerical().RealPart.AsDouble()));

    [RavelFn("Latex")] public static RuntimeValue Latex(RuntimeValue s)
        => Guard(() => new StringVal(Expr(s, "Latex").Latexize()));
}
