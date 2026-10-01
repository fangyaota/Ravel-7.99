namespace Ravel.Runtime;

/// <summary>一个**区间**:`[1..3]` / `(3..5)` / `[1..5)` / `(1..5]`。
///
/// 形状和 <see cref="IntVal"/> 那一族一样:一个**不可变的值**(C# record),
/// 自己不挂成员表 —— 那几条方法(`Start ()` / `Count ()` / `Contains n` …)住在
/// `BuiltinClasses.Range.InstanceTable` 里,这个值借 <c>Type</c> 那条链去读
/// (基类的伪 Scope,见 <see cref="RuntimeValue.MemberScope"/>)。
///
/// 值语义是白拿的:`Object` 上那条 `==` 对**两个非 `ObjectVal` 的值**比 `Equals`
/// (见 `BuiltinClasses.Operators.cs` 的 `SameValue`),而 record 的 `Equals` 逐个字段比 ——
/// 于是 `[1..3] == [1..3]` 成立、`[1..3] == [1..4]` 不成立。
///
/// 打印出来就是写出来那个样子(`ToString` 拼那对括号),所以 `print [1..3]` 会交回原文。
///
/// **不在这儿判空**:`Start &gt; End` 或者一个开区间套着同样的两端,都是**空区间**
/// (跟 Python 的 `range (3, 1)` 一个态度),`Count ()` 给 0、遍历一次都不走。</summary>
public record RangeVal(int Start, int End, bool StartClosed, bool EndClosed) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Range;

    /// <summary>这个区间里的整数有几个(空的给 0)。**O(1)**,不铺表。</summary>
    public int CountValue()
    {
        // 先把"含不含那两端"折算成闭区间 [lo, hi]:不含就各往里收一格
        var lo = StartClosed ? Start : Start + 1;
        var hi = EndClosed ? End : End - 1;
        if (lo > hi) return 0;
        // 用 long 算差:int 两端都取到极值时 (int.MaxValue - int.MinValue) 会溢出
        return (int)((long)hi - lo + 1);
    }

    /// <summary>闭区间的那两端(`CountValue () == 0` 时无意义)。遍历和 ToList 用它。</summary>
    public (int Lo, int Hi) Bounds() => (StartClosed ? Start : Start + 1, EndClosed ? End : End - 1);

    public bool ContainsValue(int n)
    {
        var (lo, hi) = Bounds();
        return n >= lo && n <= hi;
    }

    public override string ToString()
        => (StartClosed ? "[" : "(") + Start + ".." + End + (EndClosed ? "]" : ")");
}
