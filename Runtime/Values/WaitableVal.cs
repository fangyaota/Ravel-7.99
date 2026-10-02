namespace Ravel.Runtime;

/// <summary>一个**等待句柄** —— "那边正在做某件事,做完了会给你一个值"。
///
/// 手边这几样东西交回它:`System.Sleepable`(睡一会儿)、官方扩展里那几条 HTTP
/// (`Native.HttpReq` / `HttpDownload` / `HttpUpload`)。**引擎不替它们等** —— 引擎只给
/// "等"和"取结果"两条原语(`System.WaitAny` / `System.HandleValue`),**谁等、什么时候等
/// 是策略**,而那策略是 `lib/tasks.rav` 里那个调度器(协作式任务,见 CONTEXT 那一节)。
///
/// 包着的是 .NET 那边的 `Task<RuntimeValue>`,所以"等"是真的等 IO、不占解释器的步
/// (HTTP 底下是 `HttpClient.SendAsync`)。
///
/// **句柄不是值**:它没有"两个句柄相不相等"这回事(要比就比最后拿到的值)。
/// `Task` 自己不重载 `Equals`,于是 record 那套逐字段比落下来就是比引用 —— 同一个句柄才相等,
/// 正是要的。
///
/// 打印恒为 `<等待句柄>`:**不把"完成了没有"露出来** —— 那是个会变的量,露出来用例就钉不住了
/// (库那边真要问,`Task` 有的是办法)。</summary>
public sealed record WaitableVal(Task<RuntimeValue> Job) : RuntimeValue
{
    public override ObjectVal Type => BuiltinClasses.Waitable;
    public override string ToString() => "<等待句柄>";
}
