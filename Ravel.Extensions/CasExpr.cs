namespace Ravel.Extensions;

using AngouriMath;
using Ravel.Runtime;

public record CasExprVal : RuntimeValue
{
    public Entity E { get; }

    public CasExprVal(Entity e) => E = e;

    public override ObjectVal Type => PluginKit.ClassOf("Expr", "符号表达式");

    public override string ToString() => E.ToString();
}

[RavelModule("Cas")]
[RavelClass("Expr", Implements = "IValue")]
internal static class CasExprClass
{
    [ClassCtor]
    public static RuntimeValue New(RuntimeValue src) => CasNative.Wrap(CasNative.Parse(src, "Cas.Expr"));

    // ---- 运算符:把两个式子接起来 ----
    //
    // `Cas.Expr "x" + Cas.Expr "1"` → `x + 1`。两边**谁都能是数** (`Cas.Expr "x" + 1`),
    // 也能是字符串 —— 走的是 `CasNative.Get` 那条"三种都收"(`CasExpr` / 数 / 字符串)。
    //
    // 这几个是**新式运算符**(`+ - * /`)—— 从前 `a + b` 只认内置那几种数,
    // 现在插件也能给自己那个类挂一条(见 `ClassOpAttribute`)。

    [ClassOp("+")] public static RuntimeValue Add(RuntimeValue a, RuntimeValue b)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(a, "+") + CasNative.Get(b, "+")));

    [ClassOp("-")] public static RuntimeValue Sub(RuntimeValue a, RuntimeValue b)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(a, "-") - CasNative.Get(b, "-")));

    [ClassOp("*")] public static RuntimeValue Mul(RuntimeValue a, RuntimeValue b)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(a, "*") * CasNative.Get(b, "*")));

    [ClassOp("/")] public static RuntimeValue Div(RuntimeValue a, RuntimeValue b)
        => CasNative.Guard(() => CasNative.Wrap(CasNative.Get(a, "/") / CasNative.Get(b, "/")));

    [ClassOp("**")] public static RuntimeValue Pow(RuntimeValue a, RuntimeValue b)
        => CasNative.Guard(() => CasNative.Wrap(MathS.Pow(CasNative.Get(a, "**"), CasNative.Get(b, "**"))));

    [ClassOp("==")] public static RuntimeValue Eq(RuntimeValue a, RuntimeValue b)
        => CasNative.Guard(() => new BoolVal(CasNative.Get(a, "==").Equals(CasNative.Get(b, "=="))));

    [ClassOp("!=")] public static RuntimeValue Ne(RuntimeValue a, RuntimeValue b)
        => CasNative.Guard(() => new BoolVal(!CasNative.Get(a, "!=").Equals(CasNative.Get(b, "!="))));

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
    [ClassMethod("Eval")] public static RuntimeValue Eval(RuntimeValue self)
        => CasNative.Guard(() => new RealVal(CasNative.Get(self, "Eval").EvalNumerical().RealPart.AsDouble()));

    [ClassMethod("Latex")] public static RuntimeValue Latex(RuntimeValue self)
        => CasNative.Guard(() => new StringVal(CasNative.Get(self, "Latex").Latexize()));

    [ClassMethod("Text")] public static RuntimeValue Text(RuntimeValue self)
        => CasNative.Guard(() => new StringVal(CasNative.Get(self, "Text").ToString()));
}
