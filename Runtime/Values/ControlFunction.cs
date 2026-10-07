namespace Ravel.Runtime;

/// <summary>控制帧种类。With/Using/Eval/CallCC/RejectStop 是用户可见的控制内建(注册进 System 模块);
/// 其余是求值器内部合成的帧。if/while/foreach 不在其中——它们在 predefined.rav 用 Ravel 写。</summary>
public enum ControlKind { With, CallCC, Using, Eval, Alternate, ClassInit, ImplMake, SeqOp, Compose, Then, ClassOp, TraitOp, CallAssign, CallReturn, CtorApply, RejectStop }

/// <summary>控制内建值:最终阶段是纯数据(Kind+Arity+已收集参数),求值器识别后推控制帧。
/// Body 只是占位——CallInto 在 default 分支之前就匹配了 ControlFunction。</summary>
public sealed record ControlFunction(ControlKind Kind, int Arity, RList<RuntimeValue> Args)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("ControlFunction"))
{
    /// <summary>**声明了类型的那几格** —— 下标 = 第几个参数(从 0 数),`null` = 那一格没声明。
    ///
    /// 控制帧从前只有"还差几个"(`Arity`),收参数**不查类型**;而带类型的闭包那条路
    /// (`NativeClosure` / `LambdaVal`)在 `CallInto` 里先 `Accepts` 再抛、转换由带类型的
    /// `Define` 做。要用上同一套,得有个地方放"这格要什么"。现在只有 `impl` 的**实现体**
    /// 用它(见 `InstallInterfaceInit`):声明成 `Block`,于是 `{ … }` 收得下、`default`
    /// (型是 `Every`,底类型)也收得下,而 `5` / 一个 lambda 当场拒收。
    ///
    /// 名字只用在报错上(`参数 '实现体' 需要 Block，得到 Integer`)—— 控制帧的参数是引擎
    /// 内部的,印"第 3 个"没意义。</summary>
    public IReadOnlyList<(string Name, ObjectVal Type)?>? Declared { get; init; }

    /// <summary>未收满参数时累积,返回新的 ControlFunction</summary>
    public ControlFunction Accumulate(RuntimeValue a) => this with { Args = Args.Add(a) };

    /// <summary>再来一个参数是否收满</summary>
    public bool IsFinalAfter(int incoming) => Args.Count + incoming >= Arity;

    /// <summary>盖掉 record 的自动 dump,顺带带上还没收满的参数个数</summary>
    public override string ToString() => Kind == ControlKind.Then && Args.Count == 2
        ? $"<function {Args.At(0)} >> {Args.At(1)}>"
        : $"<builtin {Kind} {Args.Count}/{Arity}>";
}
