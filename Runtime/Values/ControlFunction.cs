namespace Ravel.Runtime;

/// <summary>控制内建种类(Alternate/ClassInit 为求值器内部合成)</summary>
public enum ControlKind { While, If, With, Foreach, CallCC, Using, Eval, Alternate, ClassInit, Compose }

/// <summary>控制内建值:最终阶段是纯数据(Kind+Arity+已收集参数),求值器识别后推控制帧</summary>
public sealed record ControlFunction(ControlKind Kind, int Arity, RList<RuntimeValue> Args)
    : FunctionVal(null!, (_, _) => new Done(VoidVal.Instance))
{
    /// <summary>未收满参数时累积,返回新的 ControlFunction</summary>
    public ControlFunction Accumulate(RuntimeValue a) => this with { Args = Args.Add(a) };

    /// <summary>再来一个参数是否收满</summary>
    public bool IsFinalAfter(int incoming) => Args.Count + incoming >= Arity;
}
