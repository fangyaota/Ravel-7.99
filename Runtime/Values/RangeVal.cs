namespace Ravel.Runtime;

using System.Numerics;

/// <summary>一个**区间**:`[1..3]` / `(3..5)` / `[1..5)` / `(1..5]`。
///
/// **端点收任何数值**(int / bigint / float / fraction / bigfraction,混着写也行)。
/// 而**元素是"区间里的整数"**:能枚举的是夹在中间的那些整数 ——
///
///     1..3   → 1 2 3          [1.5..3.5] → 2 3          [0.1..0.9] → 一个都没有
///
/// **方向由两端自己说了算**:起点在终点**后面**就是**倒着数**(`[5..1]` → 5 4 3 2 1),
/// 起点在前就是正着数。于是"空"只剩一种情形 —— 区间里**一个整数都没有**
/// (`(3..3)`、`[0.1..0.9]`、NaN 那几种),不再有"倒过来所以空"这回事。
///
/// 那一对括号各带**一半**的意思(`[` `]` 含那一端、`(` `)` 不含),而且**跟着方向走**:
/// 降序时"开"同样是"把那一端排掉",只不过被排掉的是往下数时紧挨着它的那个整数。
///
/// 形状和 <see cref="IntVal"/> 那一族一样:一个**不可变的值**(C# record),自己不挂成员表 ——
/// 那几条方法住在 `BuiltinClasses.Range.InstanceTable` 里,这个值借 `Type` 那条链去读
/// (基类的伪 Scope,见 <see cref="RuntimeValue.MemberScope"/>)。
///
/// 值语义是白拿的:`Object` 上那条 `==` 对**两个非 `ObjectVal` 的值**比 `Equals`
/// (见 `BuiltinClasses.Operators.cs` 的 `SameValue`),record 逐字段比 —— 于是
/// `[1..3] == [1..3]` 成立。**端点类型不同就不相等**:`[1..3] != [1.0..3.0]`
/// (一个装的是 int、一个装的是 float)。
///
/// 打印出来就是写出来那个样子(`ToString` 拼那对括号),所以 `print [1..3]` 交回原文。
///
/// **界**一律用 bigint 算(端点可能是 bigint、个数也可能超出 int);比较走
/// `CompareNumeric`(和 `<` 那批运算符**同一个**口径 —— 精确那一族不经过 double)。
/// 浮点的 NaN 当**空区间**。
///
/// **±∞ 就是"没有界"的意思** —— `[1..]` / `[..1]` 求值时装的就是它(`+Inf` / `-Inf`,
/// 见 `Interpreter.StepRange`),于是 `[1..]` 和 `[1..Inf]` 是**同一个值**。
/// 方向**由无穷在哪一头定**:∞ 在上头(+∞ 当上界、或 -∞ 当下界)就往大数走,反之往小数走;
/// 而**起手那一头永远是定得下来的那头** —— 所以 `[..1]` 是"从 1 往下数",
/// 不是"从 -∞ 往上数"(那数不出第一站)。两头都是无穷的是**第三态**:整条数轴
/// (`[..]` / `[-Inf..Inf]`),谁都在里头却没有起手那一头 —— 见 <see cref="IsWhole"/>。
///
/// 无限长的区间:**`First` / `IsEmpty` / `Step` / `Contains` / `Covers`、以及
/// `Take` / `foreach` 照常**(它们只问头那一头,或天生停得住),而
/// **`Count` / `Last` / `ToList` 当场报错** —— 那几个要"数到底",无穷答不出来
/// (判据是 <see cref="IsBounded"/>)。**整条数轴更少一步**:连 `First` / `Step` / 迭代
/// 都答不出来(没有起手那一头),能问的只剩 `Contains` / `Covers` 那两条谓词。</summary>
public record RangeVal(RuntimeValue Start, RuntimeValue End, bool StartClosed, bool EndClosed) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Range;

    /// <summary>两头都定得下来吗。**不是**就是无限长 —— `Count` / `Last` / `ToList`
    /// 那几个"要数到底"的问法答不出来(当场报错),别的照常。</summary>
    public bool IsBounded() => !Infinite(Start, out _) && !Infinite(End, out _);

    /// <summary>两头**都是** ±∞ —— **整条数轴**(`[..]`,或明写 `[-Inf..Inf]`)。
    ///
    /// 它是有界无界之外的**第三种**:不是"空"(谁都在里头 —— `Contains` / `Covers` 一律 true),
    /// 也不是"有个方向的无界"(两头都没有,方向就无从提起)。所以**没有起手那一站**:
    /// `First` / `Step` / 迭代 / `Count` / `Last` / `ToList` 全答不出来,各自当场报错。</summary>
    public bool IsWhole() => Infinite(Start, out _) && Infinite(End, out _);

    /// <summary>**起手那一站**与方向。无界也答得出来 —— 头那一头永远是定得下来的那个
    /// (见 `Head`),无穷只在"往哪边走"上说话。端点里有 NaN 就给"到不了"的那组(见 `IsEmpty`)。</summary>
    public (BigInteger First, bool Up) Head()
    {
        if (IsWhole())
            throw new RuntimeException($"全区间没有第一站（{this} —— 谁都在里头，但没有起手那一头）", ErrorKind.Value);
        if (!Ascending(out var up)) return (1, true);              // NaN:空
        // 定得下来的那一头才是第一站:`[Inf..1]` 从 1 起(往上),`[-Inf..1]` 也从 1 起(往下)
        return Infinite(Start, out _)
            ? (Bound(End, EndClosed, goingUp: up), up)
            : (Bound(Start, StartClosed, goingUp: up), up);
    }

    /// <summary>遍历的两头(整数)**加方向**:`Up` 时第一站在下(`[1..3]` → 1,3),
    /// 否则第一站在上、往下走(`[5..1]` → 5,1)。**只对有限的区间成立** —— 无限长没有"末站",
    /// 调用方先问 <see cref="IsBounded"/>(`Head` 是无界也答得出来的那一半)。</summary>
    public (BigInteger First, BigInteger Last, bool Up) Walk()
    {
        if (!Ascending(out var up)) return (1, 0, true);   // NaN:空(下面 `Bound` 折不出来)
        var first = Head().First;
        // 终点那一头:升序取"不大于它的最大整数"、降序取"不小于它的最小整数"(和起点那半相反);
        // 开的那端由 `Bound` 顺着方向往里挪一格。
        return (first, Bound(End, EndClosed, goingUp: !up), up);
    }

    /// <summary>空:端点里有 NaN,或者按着那个方向数**一个整数都没有**。
    /// **无限长的一律不空** —— 至少有一个头(整条数轴更是谁都在里头)。</summary>
    public bool IsEmpty()
    {
        if (IsWhole()) return false;                // 整条数轴,不空
        if (!Ascending(out _)) return true;         // NaN
        if (!IsBounded()) return false;
        var (first, last, up) = Walk();
        return up ? first > last : first < last;
    }

    /// <summary>区间里有多少个整数。空的给 0 —— `Count ()` 用它,装不下就给 bigint。
    /// **无限长数不出来**,当场报错(那是个真答案给不出来的问法,不拿个假数糊弄)。</summary>
    public BigInteger CountValue()
    {
        if (!IsBounded())
            throw new RuntimeException(
                IsWhole()
                    ? $"整条数轴数不出个数（{this} —— 它谁都在里头，但没有起手那一头）"
                    : $"无限长的区间数不出个数（{this} —— 用 Take 取前几个）",
                ErrorKind.Value);
        var (first, last, up) = Walk();
        if (up ? first > last : first < last) return BigInteger.Zero;
        return BigInteger.Abs(last - first) + 1;
    }

    /// <summary>第一个 / 最后一个**元素**(不是端点 —— 端点可能在区间外)。空的时候调用方先拦。
    /// `First` 无界也答得出来(头那一头总是定的);`Last` 要数到底,无限长当场报错。</summary>
    public RuntimeValue FirstElement() => Element(Head().First);

    public RuntimeValue LastElement()
    {
        if (!IsBounded()) throw new RuntimeException($"无限长的区间没有最后一个（{this}）", ErrorKind.Value);
        return Element(Walk().Last);
    }

    /// <summary>遍历的步长:`+1` 正着数、`-1` 倒着数(空区间没意义,调用方先拦)。</summary>
    public int Step() => Head().Up ? 1 : -1;

    /// <summary>元素交 int 还是 bigint:端点里有 bigint,或者两头(头那一头总要问,
    /// 尾那一头问得到才问)超出 int 范围,就给 bigint(值有多大就多大,不静默绕圈)。</summary>
    public bool Wide()
    {
        if (Start is BigIntVal || End is BigIntVal) return true;
        if (IsWhole()) return true;                  // 连起手那一头都没有 —— 别去问 `Head`
        var (first, _) = Head();
        if (first < int.MinValue || first > int.MaxValue) return true;
        if (!IsBounded()) return false;
        var (_, last, _) = Walk();
        return last < int.MinValue || last > int.MaxValue;
    }

    /// <summary>界上的那个整数,按 `Wide ()` 的规矩交 int 或 bigint。</summary>
    public RuntimeValue Element(BigInteger v) => Wide() ? new BigIntVal(v) : IntVal.Of((int)v);

    /// <summary>**落在这一段里吗** —— 按**端点**比,什么数值都行,不要求它是整数:
    /// `[1..10].Covers 2.5` ✓(2.5 确实在 1 和 10 之间)、`[1.5..3.5].Covers 1.7` ✓。
    /// 开闭照旧:`[1..10).Covers 10` ✗。**和方向无关**(倒过来写,盖住的还是那一段)。</summary>
    public bool CoversValue(RuntimeValue v)
    {
        if (!BuiltinClasses.TryAsDouble(v, out _)) return false;     // 不是数,一律不盖(只要那个"是不是数")
        var dStart = BuiltinClasses.CompareNumeric(v, Start, "<");
        var dEnd = BuiltinClasses.CompareNumeric(v, End, "<");
        if (dStart is null || dEnd is null) return false;            // NaN 掺和:谁都盖不住

        // 方向对这个问法没意义:谁小谁是下端,开闭跟着各自那一端走
        // (和**精确**那条路一个口径 —— `bigint` 端点上也不许被抹平)
        var asc = BuiltinClasses.CompareNumeric(Start, End, "<") is not > 0;
        var (lo, hi) = asc ? (dStart.Value, dEnd.Value) : (dEnd.Value, dStart.Value);
        var (loOpen, hiOpen) = asc ? (!StartClosed, !EndClosed) : (!EndClosed, !StartClosed);
        return (loOpen ? lo > 0 : lo >= 0) && (hiOpen ? hi < 0 : hi <= 0);
    }

    /// <summary>这个值算不算区间里的**元素** —— 得是**某个整数**且落在两头之间:
    /// `[1..10].Contains 2` ✓、`Contains 2.0` ✓(2.0 就是整数 2)、`Contains 2.5` ✗
    /// (要问"落不落在这段里"用 `Covers`)。**和方向无关**:同一个区间倒着数,元素还是那些。</summary>
    public bool ContainsValue(RuntimeValue v)
    {
        if (!AsInteger(v, out var n)) return false;
        if (IsWhole()) return true;                                  // 整条数轴:是个整数就在里头
        if (!Ascending(out var up)) return false;                    // NaN:空
        var (first, _) = Head();
        // 无界:整条都在起手那一站的**一边**(往上就只管不小于它、往下只管不大于它)
        if (!IsBounded()) return up ? n >= first : n <= first;
        var (_, last, _) = Walk();
        var lo = BigInteger.Min(first, last);
        var hi = BigInteger.Max(first, last);
        return lo <= n && n <= hi;
    }

    public override string ToString()
        => (StartClosed ? "[" : "(") + Start + ".." + End + (EndClosed ? "]" : ")");

    // ============================================================
    //  端点 → 界
    // ============================================================

    /// <summary>往**大数**走吗。NaN 掺和进来给 false,调用方当成空。
    ///
    /// 两头都定时按大小比(`[5..1]` → 往下)。**有一头是 ±∞ 时由它定** —— 这是唯一说得通
    /// 的读法:`[..1]` 装出来是 `[-Inf..1]`,照大小比会读成"从 -∞ 往上数"(数不出第一站),
    /// 而"∞ 在下头就往小数走"读出来是"从 1 往下数" ✓。
    /// 两头都是无穷没有方向 —— 求值那一步就挡掉了。</summary>
    private bool Ascending(out bool up)
    {
        up = true;
        if (Infinite(Start, out var s)) { up = s > 0; return true; }
        if (Infinite(End, out var e)) { up = e > 0; return true; }

        // 精确那一族不经过 double(`bigint` 端点 2^53 以上会被抹平 —— 见 `CompareNumeric`);
        // NaN 给 null,当空。端点一定是数(`StepRange` 那头验过),所以这里不报类型错。
        var c = BuiltinClasses.CompareNumeric(Start, End, "<");
        if (c is null) return false;
        up = c <= 0;
        return true;
    }

    /// <summary>这一头是不是 ±∞。`sign` 给正负(不是就给 0)。</summary>
    private static bool Infinite(RuntimeValue v, out int sign)
    {
        if (v is RealVal f && double.IsInfinity(f.Value)) { sign = f.Value > 0 ? 1 : -1; return true; }
        sign = 0;
        return false;
    }

    /// <summary>端点折成一个整数界:`goingUp` 取"不小于它的最小整数",否则取"不大于它的最大整数"
    /// (端点本来就是整数就用它自己);开的那端再往里挪一格 —— **往里**是哪个方向由 `goingUp` 说了算。
    /// NaN 给 false,无穷直接报错(给不出界)。</summary>
    private static bool Whole(RuntimeValue v, bool closed, bool goingUp, out BigInteger bound)
    {
        if (!Point(v, goingUp, out bound)) return false;
        if (!closed) bound += goingUp ? 1 : -1;
        return true;
    }

    private static BigInteger Bound(RuntimeValue v, bool closed, bool goingUp)
    {
        if (!Whole(v, closed, goingUp, out var b))
            throw new RuntimeException($"区间的端点不能是 {v} —— 它给不出界", ErrorKind.Value);
        return b;
    }

    /// <summary>端点往 `goingUp` 那边折成一个整数(不管开闭)。**不**处理开的那端。</summary>
    private static bool Point(RuntimeValue v, bool goingUp, out BigInteger bound)
    {
        switch (v)
        {
            case IntVal i:
                bound = i.Value;
                return true;
            case BigIntVal b:
                bound = b.Value;
                return true;
            case RealVal f:
                if (double.IsNaN(f.Value))
                {
                    bound = BigInteger.Zero;
                    return false;
                }

                if (double.IsInfinity(f.Value))
                    throw new RuntimeException($"区间的端点不能是 {f.Value} —— 无穷长的区间给不出界", ErrorKind.Value);

                bound = new BigInteger(goingUp ? Math.Ceiling(f.Value) : Math.Floor(f.Value));
                return true;
            case FractionVal fr:
                bound = Div(fr.Num, fr.Den, goingUp);
                return true;
            case BigFractionVal bf:
                bound = Div(bf.Num, bf.Den, goingUp);
                return true;
            default:
                throw new RuntimeException($"区间的端点需要数值，得到 {v.Type}", ErrorKind.Type);
        }
    }

    /// <summary>`num/den` 往上取整(`goingUp`)或往下取整。分母恒正 —— 分数构造时就归一了
    /// (见 `FractionVal` 的说明)。C# 的整数除法往零截断(负数那边不对),所以按余数符号分一分。</summary>
    private static BigInteger Div(BigInteger num, BigInteger den, bool goingUp)
    {
        if (den.IsZero) throw new RuntimeException("区间的端点是分数，分母不能为零", ErrorKind.ZeroDivision);

        var q = BigInteger.DivRem(num, den, out var r);
        if (r.IsZero) return q;                             // 本来就整
        if (r.Sign > 0) return goingUp ? q + 1 : q;         // 正余数:往上取整要进一
        return goingUp ? q : q - 1;                         // 负余数(刚才往零截断过):往下取整要退一
    }

    /// <summary>这个值算不算"某个整数"(`2.0` 算 2、`2.5` 不算、`6/3` 算 2、`5/2` 不算)。</summary>
    private static bool AsInteger(RuntimeValue v, out BigInteger n)
    {
        switch (v)
        {
            case IntVal i:
                n = i.Value;
                return true;
            case BigIntVal b:
                n = b.Value;
                return true;
            case RealVal f when !double.IsNaN(f.Value) && !double.IsInfinity(f.Value)
                                 && Math.Floor(f.Value) == f.Value:
                n = new BigInteger(f.Value);
                return true;
            case FractionVal fr:
                return WholeFraction(fr.Num, fr.Den, out n);
            case BigFractionVal bf:
                return WholeFraction(bf.Num, bf.Den, out n);
            default:
                n = BigInteger.Zero;
                return false;
        }
    }

    private static bool WholeFraction(BigInteger num, BigInteger den, out BigInteger n)
    {
        n = BigInteger.Zero;
        if (den.IsZero) return false;
        if (!BigInteger.Remainder(num, den).IsZero) return false;
        n = num / den;
        return true;
    }
}
