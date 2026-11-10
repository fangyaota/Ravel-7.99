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

    // 类型对不上,但**不是**"这一支不收"(`Reject` 那条会让 `|` 接走、`try` 接不住)——
    // 它是真错误,和引擎自己报的那些一路走(所以 `try` / 错误钩子照接)。
    // 收 `TypeError` 实例或字符串:前者给全字段,后者就是那句消息。
    [Sys("TypeMismatch")]
    public static RuntimeValue TypeMismatch(RuntimeValue value, RuntimeValue type)
        => throw new RuntimeException(
            $"类型不匹配: 无法将 {value.Type} 赋值给 {type}（不是父子）—— 要转就明写 `{type} …`",
            ErrorKind.Type);

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
