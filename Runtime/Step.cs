namespace Ravel.Runtime;

public abstract record Step
{
    public static RuntimeValue Run(Step start)
    {
        var s = start;
        while (s is More m) s = m.Next();
        if (s is Escape e) return e.Value;
        if (s is Error err) throw new ExitException(err.Message);
        return ((Done)s).Value;
    }
}

public record Done(RuntimeValue Value) : Step;
public record More(Func<Step> Next) : Step;
public record Escape(RuntimeValue Value) : Step;
public record Error(string Message) : Step;
/// <summary>callcc 标记——Then 在此处捕获 continuation 实现多发续延</summary>
public record CallCC(FunctionVal Fn) : Step;
