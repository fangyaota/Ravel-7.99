namespace Ravel.Runtime;

// 一对(`键` / `值`)——**值类型**。
//
// 为什么不拿 `[k v]` 两格的 list 顶替:list 是**可变**的,`==` / 哈希 / 去重
// 都按引用走 —— 同一份内容的两格既当不了字典的键,也没法按内容比。
// `Pair` 造出来就不能改,所以能安全地当键:相等按 `StrictSame` 逐格比,
// 哈希与它同一口径(见 `Runtime/Values/SameComparer.cs` 的使用方式)。
public record PairVal : ObjectVal
{
    public RuntimeValue Key { get; init; }

    public RuntimeValue Value { get; init; }

    public PairVal(RuntimeValue key, RuntimeValue value, Scope? members = null)
        : base(BuiltinClasses.Pair, members)
    {
        Key = key;
        Value = value;
    }

    // 左边那一格本身又是一对时要套括号:`(1 -> 2) -> 3` 与 `1 -> 2 -> 3` 是两棵树
    // (后者右结合 = `1 -> (2 -> 3)`),光看字面分不出来。
    public override string ToString() => ShowDepth.Guard(() => Cell(Key) + " -> " + Value);

    private static string Cell(RuntimeValue v) => v is PairVal p ? "(" + p + ")" : v.ToString();

    // **只比两格的内容**,不比 `ObjectVal` 身上那层(类型 + 每个实例各一份的成员作用域)。
    // 默认的 record `Equals` 会把父记录的字段也带上,而每建一对都会新配一个作用域 ——
    // 那样同内容的两对永远是"不相等"。
    public virtual bool Equals(PairVal? other)
        => other is not null
           && BuiltinClasses.StrictSame(Key, other.Key)
           && BuiltinClasses.StrictSame(Value, other.Value);

    // 环只能从"可变容器间接套住自己"造出来(`l := []; p := Pair l 1; l.Add p`)。
    // 深到一定层数就退回常量 0 —— 哈希只影响散列分布,不影响正确性;
    // 真递归下去会把栈打穿。
    [ThreadStatic] private static int _hashDepth;

    public override int GetHashCode()
    {
        if (_hashDepth > 64) return 0;
        _hashDepth++;
        try
        {
            return HashCode.Combine(Key, Value);
        }
        finally
        {
            _hashDepth--;
        }
    }
}
