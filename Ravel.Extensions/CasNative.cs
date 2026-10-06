namespace Ravel.Extensions;

using AngouriMath;
using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>Cas 模块 —— **符号计算**(计算机代数),底层是 **AngouriMath 2.5.0**(MIT)。
///
/// **引擎那边一个字没改**:一个官方扩展(`[RavelModule("Cas")]`),由 `lib/cas.rav` 那句
/// `using "native.rav"` 装进来 —— 和 `Math` / `Hash` / `Sqlite` 走的是同一条路。
///
/// 两份脸,同一份实现:
///
///     Cas.Diff "x^3" "x"            # 随手传字符串(这里这一份)
///     (Cas.Expr "x^3").Diff "x"     # 先攥成值再点下去(见 `CasExpr.cs`)
///
/// **表达式是个真的 Ravel 值**(`CasExprVal`)。字符串那一份交回的也是它 ——
/// 于是 `Cas.Diff "x^3" "x"` 的返回值照样能接着 `.Factor ()`。
///
/// 三条约定:
/// - **入参用 AngouriMath 那套语法**(`x^2 + 2x + 1` / `sin(x)`),不是 Ravel 的;
///   `Cas.Expr` / `print` 交回的也是那一套(能再喂回去)。
/// - **不自动化简**:`Diff` / `DiffN` 交的是库里算出来的原样(`DiffN "x^3" "x" 2` 给
///   `2 * x * 3`),要整齐再套 `Simplify` —— 贵不贵由调用方说了算,没有隐藏开销。
/// - **异常一律就地翻成 `RuntimeException`**(见 `Guard`):AngouriMath 抛的是
///   `ParseException` / `ArgumentException` 那些,而 Ravel 的 `try` **只接得住
///   `RuntimeException`** —— 不翻的话用户包不住,进程直接被打掉。</summary>
[RavelModule("Cas")]
internal static class CasNative
{
    // ============================================================
    //  帮手(`CasExprClass` 那边共用 —— 所以是 internal)
    // ============================================================

    internal static string Str(RuntimeValue v, string what) => Text(v, what).Value;

    /// <summary>剥字符串 → 表达式。**2.5.0 的 `MathS.Parse` 交的是
    /// `Either<Entity, Failure<…>>`** —— 解析在**类型上**就可失败。走 `Switch` 拆:
    /// 右边那半直接翻成 `RuntimeException`,把人话给出来。
    ///
    /// **别图省事用那个隐式转换**(`(Entity)parsed`):它拆失败时抛的是
    /// `InvalidCastException`,消息是英文的 "Specified cast is not valid" ——
    /// 用户看到那句话完全不知道是自己式子写错了。</summary>
    internal static Entity Parse(RuntimeValue v, string what)
    {
        var src = Str(v, what);
        return MathS.Parse(src).Switch(
            e => e,
            f => throw Fail($"Cas: 解析不了 {src} —— {f}"));
    }

    internal static Entity.Variable Var(RuntimeValue v, string what) => MathS.Var(Str(v, what));

    /// <summary>从 Ravel 值里取出表达式。**两种都收**:`CasExpr`(值那一份)和字符串
    /// (顺手写那一种)—— 于是 `e.Diff "x"` 和 `Cas.Diff e "x"` 都说得通。</summary>
    internal static Entity Get(RuntimeValue v, string what) => v switch
    {
        CasExprVal e => e.E,
        _ => Parse(v, what),
    };

    /// <summary>包成 Ravel 值 —— 方法都交回一个新的 `CasExpr`,于是能接着点下去。</summary>
    internal static RuntimeValue Wrap(Entity e) => new CasExprVal(e);

    /// <summary>这几种失败 C# 侧抛的不是 `RuntimeException`,Ravel 的 `try` 接不住 —— 就地翻。</summary>
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

    /// <summary>求导 —— 对第二个参数那个变量(`Cas.Diff "x^3" "x"` → `3 * x^2`)。</summary>
    [RavelFn("Diff")] public static RuntimeValue Diff(RuntimeValue s, RuntimeValue v)
        => Guard(() => Wrap(Get(s, "Diff").Differentiate(Var(v, "Diff"))));

    /// <summary>求高阶导 —— 第三个参数是阶数(`Cas.DiffN "x^3" "x" 2`)。</summary>
    [RavelFn("DiffN")] public static RuntimeValue DiffN(RuntimeValue s, RuntimeValue v, RuntimeValue n)
        => Guard(() => Wrap(Get(s, "DiffN").Differentiate(Var(v, "DiffN"), Int(n, "DiffN"))));

    /// <summary>不定积分(自带常数项 `+ C`)。</summary>
    [RavelFn("Integrate")] public static RuntimeValue Integrate(RuntimeValue s, RuntimeValue v)
        => Guard(() => Wrap(Get(s, "Integrate").Integrate(Var(v, "Integrate"))));

    [RavelFn("Limit")] public static RuntimeValue Limit(RuntimeValue s, RuntimeValue v, RuntimeValue at)
        => Guard(() => Wrap(Get(s, "Limit").Limit(Var(v, "Limit"), Parse(at, "Limit"))));

    // ============================================================
    //  解方程 / 代入
    // ============================================================

    /// <summary>解方程 —— 交回的是一**组**解(AngouriMath 那套集合写法,`{ … }`)。
    ///
    /// 用的是 `Solve` 不是 `SolveEquation`:后者只认"能反解出单个值"的形状,
    /// `x^2 = 4` 这种会报「Inverting this node would need a set-valued answer」——
    /// 而多解恰恰是解方程最常碰到的。</summary>
    [RavelFn("Solve")] public static RuntimeValue Solve(RuntimeValue s, RuntimeValue v)
        => Guard(() => Wrap(Get(s, "Solve").Solve(Var(v, "Solve"))));

    /// <summary>把式子里的变量换成另一个式子(`Cas.Subst "x^2" "x" "y+1"`)。</summary>
    [RavelFn("Subst")] public static RuntimeValue Subst(RuntimeValue s, RuntimeValue v, RuntimeValue val)
        => Guard(() => Wrap(Get(s, "Subst").Substitute(Var(v, "Subst"), Parse(val, "Subst"))));

    // ============================================================
    //  出去:数值 / LaTeX
    // ============================================================

    /// <summary>算成**数**(交 real)—— 唯一不交表达式的那条。式子里的常数(π / e)也认。
    ///
    /// `EvalNumerical ()` 交的是 `Entity.Number.Complex`,取它的实部 ——
    /// 纯实数的式子虚部是 0,取实部就是答案。</summary>
    [RavelFn("Eval")] public static RuntimeValue Eval(RuntimeValue s)
        => Guard(() => new RealVal(Get(s, "Eval").EvalNumerical().RealPart.AsDouble()));

    [RavelFn("Latex")] public static RuntimeValue Latex(RuntimeValue s)
        => Guard(() => new StringVal(Get(s, "Latex").Latexize()));
}
