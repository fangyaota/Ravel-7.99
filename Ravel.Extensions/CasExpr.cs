namespace Ravel.Extensions;

using AngouriMath;
using Ravel.Runtime;

/// <summary>一个**符号表达式** —— AngouriMath 的 `Entity` 包成 Ravel 的值。
///
/// **继承的是 `RuntimeValue` 不是 `ObjectVal`**(和 `Range` / `fraction` / `int` 一个待遇):
/// 它是个**值** —— `==` 比**内容**(record 逐字段比,而 `Entity.Equals` 本身就是结构比较)。
/// `Ravel.Structures` 那几个(`StackVal` 之流)正好相反,它们继承 `ObjectVal`:那些是**容器**,
/// 会变、按身份认。
///
/// 类型对象是 **`Cas.Expr`** —— `[RavelClass]` 那个名字 + 同一个类上的 `[RavelModule("Cas")]`
/// 合起来的效果(见 `PluginApi.InstallClass`:有模块就把类名登记进那个模块)。于是:
///
///     e := Cas.Expr "x^2 + 1"      # 构造(类可以当构造器调)
///     e.Diff "x"                    # 方法**交回一个新的 CasExpr**,能接着点
///     e.Solve "x"
///
/// 方法和 `Cas.Diff` 那批 `[RavelFn]` 是**同一份实现**(都走 `CasNative`),
/// 差别只在"表达式先攥在手里"还是"随手传个字符串"。</summary>
public record CasExprVal : RuntimeValue
{
    public Entity E { get; }

    public CasExprVal(Entity e) => E = e;

    public override ObjectVal Type => PluginKit.ClassOf("Expr", "符号表达式");

    public override string ToString() => E.ToString();
}

/// <summary>`Cas.Expr` 这个类的方法表 —— 插件定义类的那套:`[RavelClass]` 说出类名,
/// `[ClassCtor]` 是构造器,`[ClassMethod]` 是成员(签名一律 `(self, 0~2 个实参)`,
/// 多出来的那个参数在 Ravel 那边是柯里化的)。
///
/// 每一条都**交回一个新的 `CasExpr`**(`Eval` 交 `real`、`Latex` / `Text` 交字符串)——
/// 于是能一路点下去:`(Cas.Expr "x^2 - 1").Factor ().Solve "x"`。</summary>
[RavelModule("Cas")]
[RavelClass("Expr")]
internal static class CasExprClass
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue src) => CasNative.Wrap(CasNative.Parse(src, "Cas.Expr"));

    // ---- 化简 / 变形 ----
    [ClassMethod("Simplify")] public static RuntimeValue Simplify(RuntimeValue self)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(self, "Simplify").Simplify()));

    [ClassMethod("Expand")] public static RuntimeValue Expand(RuntimeValue self)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(self, "Expand").Expand()));

    [ClassMethod("Factor")] public static RuntimeValue Factor(RuntimeValue self)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(self, "Factor").Factorize()));

    // ---- 微积分 ----
    [ClassMethod("Diff")] public static RuntimeValue Diff(RuntimeValue self, RuntimeValue v)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(self, "Diff").Differentiate(CasNative.Var(v, "Diff"))));

    [ClassMethod("DiffN")] public static RuntimeValue DiffN(RuntimeValue self, RuntimeValue v, RuntimeValue n)
        => CasNative.Guard(() => CasNative.Wrap(
            CasNative.Get(self, "DiffN").Differentiate(CasNative.Var(v, "DiffN"), PluginKit.Int(n, "DiffN"))));

    [ClassMethod("Integrate")] public static RuntimeValue Integrate(RuntimeValue self, RuntimeValue v)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(self, "Integrate").Integrate(CasNative.Var(v, "Integrate"))));

    [ClassMethod("Limit")] public static RuntimeValue Limit(RuntimeValue self, RuntimeValue v, RuntimeValue at)
        => CasNative.Guard(() => CasNative.Wrap(
            CasNative.Get(self, "Limit").Limit(CasNative.Var(v, "Limit"), CasNative.Parse(at, "Limit"))));

    // ---- 解方程 / 代入 ----
    [ClassMethod("Solve")] public static RuntimeValue Solve(RuntimeValue self, RuntimeValue v)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(self, "Solve").Solve(CasNative.Var(v, "Solve"))));

    [ClassMethod("Subst")] public static RuntimeValue Subst(RuntimeValue self, RuntimeValue v, RuntimeValue val)
        => CasNative.Guard(() => CasNative.Wrap(
            CasNative.Get(self, "Subst").Substitute(CasNative.Var(v, "Subst"), CasNative.Parse(val, "Subst"))));

    // ---- 出去:数 / 字符串 ----
    /// <summary>算成**数**(交 real)—— 唯一不交 `CasExpr` 的那条。</summary>
    [ClassMethod("Eval")] public static RuntimeValue Eval(RuntimeValue self)
        => CasNative.Guard(() => new RealVal(CasNative.Get(self, "Eval").EvalNumerical().RealPart.AsDouble()));

    [ClassMethod("Latex")] public static RuntimeValue Latex(RuntimeValue self)
        => CasNative.Guard(() => new StringVal(CasNative.Get(self, "Latex").Latexize()));

    /// <summary>那套语法打印出来的样子(和 `print` / `ToString ()` 一个口径)。</summary>
    [ClassMethod("Text")] public static RuntimeValue Text(RuntimeValue self)
        => CasNative.Guard(() => new StringVal(CasNative.Get(self, "Text").ToString()));
}
