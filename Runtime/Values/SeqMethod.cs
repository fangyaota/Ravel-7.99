namespace Ravel.Runtime;

/// <summary>容器上"逐个跑一遍"的方法是**哪一件事**。一个模式一件,求值器里一张 switch 管完 ——
/// 它们的骨架是同一套:取一个元素 → 调一次用户函数 → 看结果接着走。</summary>
public enum SeqMode { Each, Map, Where, Fold, All, Any, Find, SortBy }

/// <summary>容器上的高阶序列方法(Each / Map / Where / Fold / All / Any / Find / SortBy)
/// 作为**成员**的那个值。
///
/// 它非得是个控制帧不可,不能像别的方法那样写个 C# 闭包:这些方法要**调 Ravel 函数**,
/// 而原生闭包是同步的 C# 调用 —— 调不了(调 Ravel 函数 = 往帧栈上推帧)。
/// 于是:读成员时绑好接收者(<see cref="ISelfBinding"/>),绑完交出去一个 `ControlFunction`,
/// 求值器见到它就推 <see cref="ControlKind.SeqOp"/> 帧,一个元素一步地走。
///
/// `Arity` 是**参数个数**(self 和模式先占了两格):收一个函数的那些是 3;
/// `Fold` 收两个(初值 + 函数)是 4 —— 柯里化喂满才推帧,和普通函数一样。</summary>
public sealed record SeqMethod(SeqMode Mode, int Arity)
    : FunctionVal(null!, (_, self) => new ControlFunction(ControlKind.SeqOp, Arity,
        RList<RuntimeValue>.Empty.Add(self).Add(new IntVal((int)Mode)))), ISelfBinding
{
    /// <summary>报错文案里用的名字(和别的成员一样,名字挂在 `Name` 成员上)。</summary>
    public static string Label(SeqMode m) => m switch
    {
        SeqMode.Each => "Each",
        SeqMode.Map => "Map",
        SeqMode.Where => "Where",
        SeqMode.Fold => "Fold",
        SeqMode.All => "All",
        SeqMode.Any => "Any",
        SeqMode.Find => "Find",
        _ => "SortBy",
    };
}
