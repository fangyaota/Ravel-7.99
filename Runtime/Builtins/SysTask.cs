namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

/// <summary>**等待句柄**那两条原语 —— 引擎这一侧只有这些,调度在库里(`lib/tasks.rav`)。
///
/// 分工是刻意的:引擎知道"怎么等一个 .NET 任务"(那是本机的事),库知道"手头这几个任务
/// 该谁先跑"(那是策略)。所以这儿没有 `Spawn`/`Yield`/`Await` —— 那些全是 `callcc` 那套
/// 现成原语搭出来的,写法照 `lib/generator.rav` 的 `GeneratorCursor`(`lib/tasks.rav` 顶上
/// 写着为什么必须照它)。
///
/// **`WaitAny` 会阻塞 OS 线程**,这是设计的一部分而不是将就:单 OS 线程的协作式调度器
/// 手里没活干的时候,本来就该把线程交给操作系统去等 IO —— 那和"空转轮询"是两回事。
/// 顺便,它就是那条"没有任务可跑时别把 CPU 烧掉"的闸。</summary>
internal static class SysTask
{
    /// <summary>`System.WaitAny [h…]` —— 等这一组句柄里**最先完成的那一个**,交回它。
    ///
    /// 空表当场报错(没有"等到天荒地老"这回事:那是调度器自己的 bug,不是用户的)。
    /// 里面夹了不是句柄的东西也当场报错 —— 和别的 `[Sys]` 一个口径。
    ///
    /// **出错的那个句柄照样交回**:错误要等 `HandleValue` 那一头才现形,这样调度器
    /// 能把"谁完成了"和"结果是什么"分开处理(它得先知道是谁,才能把它放回就绪队列)。</summary>
    [Sys("WaitAny")]
    public static RuntimeValue WaitAny(RuntimeValue a)
    {
        var list = As<ListVal>(a, "WaitAny 的句柄表");
        if (list.Elements.Count == 0)
            throw new RuntimeException("WaitAny 要一组句柄(空表没什么可等的)", ErrorKind.Argument);

        var jobs = new Task<RuntimeValue>[list.Elements.Count];
        for (var i = 0; i < jobs.Length; i++)
            jobs[i] = As<WaitableVal>(list.Elements[i], "WaitAny 的元素").Job;

        var done = Task.WaitAny(jobs);
        return list.Elements[done];
    }

    /// <summary>`System.HandleValue h` —— 从**已经完成**的句柄上取结果。没完成就是内部
    /// 用错了(调用方该先 `WaitAny`),报错的口气照着"这是引擎/库的 bug"来。
    ///
    /// 那边抛了(网络断了、超时了)就转成 Ravel 错误抛出去 —— 和"同步版"那几条 HTTP
    /// 直接抛是同一个形状,于是 `try` 那一头一个字都不用改。**内层异常接在消息后面**:
    /// `HttpRequestException` 自己那句话常常什么都没有,缘由在里层。</summary>
    [Sys("HandleValue")]
    public static RuntimeValue HandleValue(RuntimeValue a)
    {
        var h = As<WaitableVal>(a, "HandleValue 的句柄");
        if (!h.Job.IsCompleted)
            throw new RuntimeException("这个句柄还没完成 —— 取结果之前得先等它(WaitAny)", ErrorKind.Value);

        return h.Job.Status switch
        {
            TaskStatus.RanToCompletion => h.Job.Result,
            TaskStatus.Faulted => throw new RuntimeException(
                "那个活儿没干成:" + Innermost(h.Job.Exception!), ErrorKind.Io),
            _ => throw new RuntimeException(
                $"那个活儿被取消了({h.Job.Status})", ErrorKind.Io),
        };
    }

    /// <summary>最内层那句消息 —— 包了几层就接几层(和 `Cli/Program.cs` 的 `Inner` 一个道理:
    /// `AggregateException` 外面那层什么线索都没有)。</summary>
    private static string Innermost(Exception ex)
    {
        var text = ex.Message;
        for (var x = ex.InnerException; x is not null; x = x.InnerException)
            text += " :: " + x.Message;
        return text;
    }

    /// <summary>`System.Sleepable ms` —— 睡一会儿,但**交回一个句柄**而不是占住线程
    /// (睡 0 或负数就是"立刻就好":和 `System.Sleep` 那条一个规矩)。
    ///
    /// 它和 `System.Sleep` 并存是有意的:那条**阻塞**,适合"我就想停下来"的脚本;
    /// 这一条是给任务用的 —— 调度器手上还有别的活儿时,睡觉那个任务不该把线程占着。</summary>
    [Sys("Sleepable")]
    public static RuntimeValue Sleepable(RuntimeValue a)
    {
        var ms = BuiltinClasses.IntArg(a, "Sleepable 的毫秒数");
        return new WaitableVal(ms <= 0
            ? Task.FromResult<RuntimeValue>(VoidVal.Instance)
            : Task.Delay(ms).ContinueWith(_ => (RuntimeValue)VoidVal.Instance));
    }
}
