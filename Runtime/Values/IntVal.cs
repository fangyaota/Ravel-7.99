namespace Ravel.Runtime;

/// <summary>整数。**小的一批是共享的**,别 `new` —— 走 <see cref="Of"/>。
///
/// 为什么值得:值是**引用类型**(`RuntimeValue` 是抽象 record),所以每一个整数都是
/// 一次堆分配。而紧循环里那些整数(`i`、`i + 1`、下标、长度、`0` / `1` 这种常量)
/// 绝大多数落在一个很小的范围里 —— 那批全在池子里,一次都不分配。
///
/// 构造器是**私有**的,不是"最好别用":私有才让编译器把每一处 `new IntVal(…)` 都报出来，
/// 一个都漏不掉。要加一个新的整数,写 `IntVal.Of (x)`。
///
/// 池子本身不可变(record 的属性是 `init`),所以共享是安全的 —— 谁都不会改到别人那一枚。</summary>
public record IntVal : RuntimeValue
{
    /// <summary>池子的两端(**含**)。下界取负一点是因为下标算差值、`-1` 这类很常见;
    /// 上界 4096 覆盖长度、下标、小常量,再往上就开始是真正的"数据"了,那批不值得囤。</summary>
    private const int Lo = -128;
    private const int Hi = 4096;

    private static readonly IntVal[] Pool = Build();

    private static IntVal[] Build()
    {
        var pool = new IntVal[Hi - Lo + 1];
        for (var i = 0; i < pool.Length; i++) pool[i] = new IntVal(Lo + i);
        return pool;
    }

    public int Value { get; private init; }

    private IntVal(int value) => Value = value;

    /// <summary>造一个整数 —— **一律走这条**。落在池子里的直接交回那一枚,不再分配。</summary>
    public static IntVal Of(int value)
        => value >= Lo && value <= Hi ? Pool[value - Lo] : new IntVal(value);

    public override ObjectVal Type => BuiltinClasses.Int;

    public override string ToString() => Value.ToString();
}
