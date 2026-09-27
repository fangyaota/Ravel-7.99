namespace Ravel.Runtime;

/// <summary>控制帧种类。With/Using/Eval/CallCC 是用户可见的控制内建(注册进 System 模块);
/// 其余是求值器内部合成的帧。if/while/foreach 不在其中——它们在 predefined.rav 用 Ravel 写。</summary>
public enum ControlKind { With, CallCC, Using, Eval, Alternate, ClassInit, ImplMake, Compose, ClassOp, CallAssign, CallReturn, CtorApply }

/// <summary>控制内建值:最终阶段是纯数据(Kind+Arity+已收集参数),求值器识别后推控制帧。
/// Body 只是占位——CallInto 在 default 分支之前就匹配了 ControlFunction。</summary>
public sealed record ControlFunction(ControlKind Kind, int Arity, RList<RuntimeValue> Args)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("ControlFunction"))
{
    /// <summary>未收满参数时累积,返回新的 ControlFunction</summary>
    public ControlFunction Accumulate(RuntimeValue a) => this with { Args = Args.Add(a) };

    /// <summary>再来一个参数是否收满</summary>
    public bool IsFinalAfter(int incoming) => Args.Count + incoming >= Arity;

    /// <summary>盖掉 record 的自动 dump,顺带带上还没收满的参数个数</summary>
    public override string ToString() => $"<builtin {Kind} {Args.Count}/{Arity}>";
}
