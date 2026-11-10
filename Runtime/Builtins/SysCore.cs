namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

internal static class SysCore
{
    [Sys("Property")]
    public static RuntimeValue MakeProperty(RuntimeValue g)
    {
        // `property ()` 就是那一对什么都不做的 getter/setter(`by a: int = _` 脱糖落在这里)。
        if (g is VoidVal) return BuiltinClasses.DefaultProperty();
        if (g is not FunctionVal gf) throw new RuntimeException("property 需要 getter 函数", ErrorKind.Argument);
        return FunctionVal.From(s => s is FunctionVal sf
            ? new PropertyVal(gf, sf)
            : throw new RuntimeException("property 需要 setter 函数", ErrorKind.Argument));
    }

    [Sys("Assert")]
    public static RuntimeValue Assert(RuntimeValue cond, RuntimeValue msg)
    {
        if (cond is not BoolVal b)
            throw new RuntimeException("assert 需要 bool 参数", ErrorKind.Argument);
        if (!b.Value)
            throw new RuntimeException(msg is StringVal s ? s.Value : "assertion failed: " + Show(cond), ErrorKind.Assert);
        return VoidVal.Instance;
    }

    [Sys("Exit")]
    public static RuntimeValue Exit(RuntimeValue a) => throw new ExitException(As<StringVal>(a, "exit 的消息").Value);

    [Sys("RavelMod")]
    public static RuntimeValue RavelMod(Interpreter self, RuntimeValue a)
        => self.EnterModule(As<StringVal>(a, "ravel 的模块名").Value);

    [Sys("WarnForgotCall")]
    public static RuntimeValue SetWarnForgotCall(Interpreter self, RuntimeValue a)
    {
        self.WarnForgotCall = As<BoolVal>(a, "WarnForgotCall").Value;
        return VoidVal.Instance;
    }
    // ── 引擎自己的两样状态:错误交给谁、模块正加载到哪儿 ──
    // 它们是**库与引擎之间的桥**(见 predefined.rav 的 `callcc` 与 exceptions.rav 的 `onError`),
    // 所以放在"语言级小件"这一主题里。

    [Sys("SetErrorHook")]
    public static RuntimeValue SetErrorHook(Interpreter self, RuntimeValue a)
    {
        self.ErrorHook = a as FunctionVal
            ?? throw new RuntimeException($"SetErrorHook 要一个函数，得到 {a.Type}", ErrorKind.Argument);
        return VoidVal.Instance;
    }

    [Sys("Reject")]
    public static RuntimeValue Reject(Interpreter self, RuntimeValue msg)
    {
        // **只要 `TypeError`** —— 和 `throw`(要一个 `Exception`)一个口径,只是更窄:
        // "这一支不收这个参数"就是**类型/形状对不上**那一类(引擎那边 `|` 的交替接的
        // 也正是 `ErrorKind.Type`,两者是同一件事)。
        //
        // 收字符串 / 收随便什么的话,写错的人只会看到那句通用的「这一支不收这个参数」——
        // 他要说的那句话被**静默**吞掉(守卫生成的就是字符串,消息一直是丢的)。
        if (msg is not ObjectVal { IsClass: false } o || !o.ClassType.IsAssignableTo(BuiltinClasses.TypeError))
            throw new RuntimeException(
                $"System.Reject 要一个 TypeError（`System.Reject (TypeError \"…\")`），得到 {msg.Type}",
                ErrorKind.Argument);

        throw new RejectedException(BuiltinClasses.ExceptionMessage(msg) ?? "这一支不收这个参数");
    }

    // 定义处注解没过 —— 拒收,和模式那一格同一条口径(`|` 接得住、`try` 接不住)。
    // 文案在这儿拼,不走 `string`/`typeof`:那两个名字用户可能遮蔽掉(`string := C`)。
    [Sys("RejectType")]
    public static RuntimeValue RejectType(RuntimeValue value, RuntimeValue type)
        => throw new RejectedException($"这一格要的是 {type}，得到 {value.Type}");

    [Sys("Unhandled")]
    public static RuntimeValue HandBack(Interpreter self, RuntimeValue e) => self.Unhandled(e);

    [Sys("LoadingState")]
    public static RuntimeValue LoadingState(Interpreter self, RuntimeValue _) => self.SnapshotLoading();

    [Sys("RestoreLoading")]
    public static RuntimeValue RestoreLoadingFrom(Interpreter self, RuntimeValue snap) => self.RestoreLoading(snap);

    [Sys("FormatError")]
    public static RuntimeValue FormatError(Interpreter self, RuntimeValue e)
    {
        var msg = BuiltinClasses.ExceptionMessage(e);
        return new StringVal(self.Handed is { } ex && ex.Message == msg
            ? ErrorReport.Format(ex)
            : msg ?? Show(e));
    }
}
