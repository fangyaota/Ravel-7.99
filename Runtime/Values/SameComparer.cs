namespace Ravel.Runtime;

/// <summary>
/// 字典键 / 集合元素的比较口径：和 `===` 同一套（`BuiltinClasses.StrictSame`）。
/// 类型不同就不等（`1` 与 `1.0` 是两格）；值语义比值；容器 / 类实例 / 函数 / 模块 / 类型比引用。
/// </summary>
internal sealed class SameComparer : IEqualityComparer<RuntimeValue>
{
    internal static readonly SameComparer Instance = new();

    public bool Equals(RuntimeValue? a, RuntimeValue? b)
        => a is not null && b is not null && BuiltinClasses.StrictSame(a, b);

    // StrictSame 认为相等的一定要给出同一个哈希：
    // 值语义的（数和串那些）按 record 的内容哈希；其余的 `===` 是引用相等，就按引用哈希。
    public int GetHashCode(RuntimeValue v)
        => v is ObjectVal o && o.Type is not ClassVal { IsValueLike: true }
            ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v)
            : v.GetHashCode();
}
