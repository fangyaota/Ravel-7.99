namespace Ravel.Runtime;

/// <summary>同步调用结果——显式帧栈已取代 CPS trampoline,仅保留 Done(延迟步骤由帧栈表达)</summary>
public abstract record Step
{
    public static RuntimeValue Run(Step s) => ((Done)s).Value;
}

public record Done(RuntimeValue Value) : Step;
