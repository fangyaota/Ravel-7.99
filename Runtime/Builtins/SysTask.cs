namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

internal static class SysTask
{
    [Sys("WaitAny")]
    public static RuntimeValue WaitAny(RuntimeValue a)
    {
        var list = As<ListVal>(a, "WaitAny 的句柄表");
        if (list.Elements.Count == 0)
            throw new RuntimeException(Messages.T("runtime.task.wait_any_empty_handles"), ErrorKind.Argument);

        var jobs = new Task<RuntimeValue>[list.Elements.Count];
        for (var i = 0; i < jobs.Length; i++)
            jobs[i] = As<WaitableVal>(list.Elements[i], "WaitAny 的元素").Job;

        var done = Task.WaitAny(jobs);
        return list.Elements[done];
    }

    [Sys("HandleValue")]
    public static RuntimeValue HandleValue(RuntimeValue a)
    {
        var h = As<WaitableVal>(a, "HandleValue 的句柄");
        if (!h.Job.IsCompleted)
            throw new RuntimeException(Messages.T("runtime.task.handle_not_ready"), ErrorKind.Value);

        return h.Job.Status switch
        {
            TaskStatus.RanToCompletion => h.Job.Result,
            TaskStatus.Faulted => throw Unwrap(h.Job.Exception!),
            _ => throw new RuntimeException(
                $"那个活儿被取消了({h.Job.Status})", ErrorKind.Io),
        };
    }

    private static Exception Unwrap(AggregateException agg)
    {
        foreach (var e in agg.Flatten().InnerExceptions)
            if (e is RuntimeException) return e;

        return new RuntimeException("那个活儿没干成:" + Innermost(agg), ErrorKind.Io);
    }

    private static string Innermost(Exception ex)
    {
        var text = ex.Message;
        for (var x = ex.InnerException; x is not null; x = x.InnerException)
            text += " :: " + x.Message;
        return text;
    }

    [Sys("Sleepable")]
    public static RuntimeValue Sleepable(RuntimeValue a)
    {
        var ms = BuiltinClasses.IntArg(a, "Sleepable 的毫秒数");
        return new WaitableVal(ms <= 0
            ? Task.FromResult<RuntimeValue>(VoidVal.Instance)
            : Task.Delay(ms).ContinueWith(_ => (RuntimeValue)VoidVal.Instance));
    }
}
