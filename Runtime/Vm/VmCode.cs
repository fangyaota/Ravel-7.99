namespace Ravel.Runtime;

/// <summary>一条指令。**定长、扁平** —— 一个 `int` 的操作码加最多两个操作数,
/// 存在一个数组里按下标推进。树遍历那边每个节点要一个 `NodeFrame` 对象,
/// 这边什么都不分配。</summary>
public enum Op
{
    /// <summary>把常量表第 a 个压栈</summary>
    Const,
    /// <summary>读一个变量(名字表第 a 个)压栈</summary>
    Load,
    /// <summary>弹一个值,`=` 写进名字表第 a 个(沿链找),再把它压回去(赋值表达式的值)</summary>
    Store,
    /// <summary>弹一个值,`:=` 定义成名字表第 a 个,再压回去</summary>
    Define,
    /// <summary>弹参数、弹函数,调它(结果会回到本帧的操作数栈上)</summary>
    Call,
    /// <summary>弹右、弹左,算运算符(名字表第 a 个),结果压栈</summary>
    Bin,
    /// <summary>无条件跳</summary>
    Jump,
    /// <summary>弹一个值,是假就跳</summary>
    JumpIfFalse,
    /// <summary>把一个值丢掉(不是最后一条语句的语句,值没人要)</summary>
    Drop,
    /// <summary>把栈顶那个值包成闭包压回去(名字表第 a 个是参数名)</summary>
    MakeLambda,
    /// <summary>返回:弹栈顶交给父帧</summary>
    Ret,
}

/// <summary>编译出来的一个函数体:一串指令 + 两张表。
///
/// **一个 `VmCode` 就是一个 lambda 的体**,`Ip` 从 0 开始跑,跑到 `Ret` 为止。
/// 参数在入口时由 `VmFrame` 自己 define 进作用域(和树遍历的 `LambdaVal` 那一臂一样)。</summary>
public sealed class VmCode
{
    public required Op[] Ops { get; init; }
    public required int[] A { get; init; }
    public required RuntimeValue[] Consts { get; init; }
    public required string[] Names { get; init; }

    public int Length => Ops.Length;
}
