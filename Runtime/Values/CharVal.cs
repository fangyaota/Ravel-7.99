namespace Ravel.Runtime;

/// <summary>一个**字符**。值是 .NET 的 `char` —— 也就是**一个 UTF-16 码元**,
/// 和 `string.Length` / `s.At i` 的口径一致(`"😀".Length` 是 2,`"😀".At 0` 拿到的是那个
/// 代理对的前一半)。要在"一个用户眼里的字符"上做文章,得先自己把码位切出来。
///
/// 它和数、字符串一样是**值类型**(`Char <: ValueType`):能当字典的键、能比大小、
/// 能进集合去重、`IComparable` / `IKey` 都认它。</summary>
public record CharVal(char Value) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Char;

    /// <summary>显示成那个字符本身(和 `print "a"` 打出 `a` 一个口径,不带引号)——
    /// 于是 `print ['a' "a"]` 打出 `[a a]`,和 `1` / `"1"` 那对一样的取舍:
    /// **打印是给眼睛看的,不是值的身份证**(要分清楚就 `typeof`)。</summary>
    public override string ToString() => Value.ToString();
}
