namespace Ravel.Runtime;

/// <summary>一个作用域当值用(`currentScope ()` / `Function.scope ()` 的结果)。
///
/// 非原子值 —— 有**自己的成员表**,见 <see cref="ObjectVal"/>。里面那个作用域叫 `Inner`:
/// `Scope` 这个名字归继承来的成员表(和 <see cref="FunctionVal.CaptureScope"/> 分开的理由一样,
/// 一个名字不能既是"我包着的那个作用域"又是"我身上的成员表")。</summary>
public record ScopeVal : ObjectVal
{
    /// <summary>这个值包着的那个作用域(词法作用域,不是成员表)。</summary>
    public Scope Inner { get; init; }

    public ScopeVal(Scope inner, Scope? members = null)
        : base(BuiltinClasses.ScopeType, members ?? new Scope())
        => Inner = inner;

    public override string ToString() => "<scope>";
}
