namespace Ravel.Runtime;

/// <summary>控制内建种类(Alternate/ClassInit 等为求值器内部合成)</summary>
public enum ControlKind { While, If, With, Foreach, CallCC, Using, Eval, Alternate, ClassInit, Compose, ClassOp, CallAssign, CallReturn, CtorApply }

/// <summary>控制内建值:最终阶段是纯数据(Kind+Arity+已收集参数),求值器识别后推控制帧。
/// Body 只是占位——CallInto 在 default 分支之前就匹配了 ControlFunction。</summary>
public sealed record ControlFunction(ControlKind Kind, int Arity, RList<RuntimeValue> Args)
    : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    /// <summary>未收满参数时累积,返回新的 ControlFunction</summary>
    public ControlFunction Accumulate(RuntimeValue a) => this with { Args = Args.Add(a) };

    /// <summary>再来一个参数是否收满</summary>
    public bool IsFinalAfter(int incoming) => Args.Count + incoming >= Arity;
}
