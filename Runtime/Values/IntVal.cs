namespace Ravel.Runtime;

public record IntVal : RuntimeValue
{
    private const int Lo = -128;
    private const int Hi = 4096;

    // 成员表**不放在这儿**:一个整数一张,由 `RuntimeValue.MemberScope` 的
    // `ConditionalWeakTable` 懒建(见那边的注释)。放在 record 里会多一个字段 ——
    // 合成的相等得手写,而且池子里那几枚一旦被人读过成员就不再等于自己(踩过)。
    // 于是这里只剩"按值比",和 `1 == 1` 一个意思。

    public virtual bool Equals(IntVal? other) => other is not null && other.Value == Value;

    public override int GetHashCode() => Value;

    private static readonly IntVal[] Pool = Build();

    private static IntVal[] Build()
    {
        var pool = new IntVal[Hi - Lo + 1];
        for (var i = 0; i < pool.Length; i++) pool[i] = new IntVal(Lo + i);
        return pool;
    }

    public int Value { get; private init; }

    private IntVal(int value) => Value = value;

    public static IntVal Of(int value)
        => value >= Lo && value <= Hi ? Pool[value - Lo] : new IntVal(value);

    public override ObjectVal Type => BuiltinClasses.Int;

    public override string ToString() => Value.ToString();
}
