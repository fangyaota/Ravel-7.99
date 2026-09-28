namespace Ravel.Runtime;

/// <summary>callcc 续延:捕获帧链引用,调用时还原并塞结果(持久帧→多发射天然支持)。调用走 CallInto 的 ContinuationVal 分支。
///
/// 两份**控制状态**的快照,续延恢复时一起还原(见 `Interpreter.RestoreControlState`):
/// `Handlers` = Ravel 那边 `Ex.HandlerStack` 的内容(没有那个列表就是 null);
/// `Loading` = 模块加载栈 `_loading`(栈顶在前)。
///
/// 非带上不可的理由:它们都是**副作用式的状态**,不在帧链里 —— 帧链一被丢弃,靠帧的正常收尾
/// 去摘 handler / 退 `_loading` 的那一步就再也不会执行。逃出 `try` 体于是留下僵尸 handler,
/// 逃出模块体于是让那个模块永远"正在加载"(之后 `using` 它被误报成循环引用)。
/// 复制一小份的代价可以忽略。</summary>
public sealed record ContinuationVal(Frame Captured, ListVal? Handlers = null, string[]? Loading = null)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("ContinuationVal"))
{
    /// <summary>盖掉 record 的自动 dump(否则会把整条帧链打出来)</summary>
    public override string ToString() => "<continuation>";
}
