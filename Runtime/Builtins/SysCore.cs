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
        if (g is not FunctionVal gf) throw new RuntimeException(Messages.T("runtime.builtin.property_requires_getter"), ErrorKind.Argument);
        return FunctionVal.From(s => s is FunctionVal sf
            ? new PropertyVal(gf, sf)
            : throw new RuntimeException("property 需要 setter 函数", ErrorKind.Argument));
    }

    [Sys("Assert")]
    public static RuntimeValue Assert(RuntimeValue cond, RuntimeValue msg)
    {
        if (cond is not BoolVal b)
            throw new RuntimeException(Messages.T("runtime.builtin.assert_requires_bool"), ErrorKind.Argument);
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

    // 定义处注解没过 —— 拒收,和模式那一格同一条口径(`|` 接得住、`try` 接不住)。
    // 文案在这儿拼,不走 `string`/`typeof`:那两个名字用户可能遮蔽掉(`string := C`)。
    // `x : T = _` 的值:`default T`。只有"有零值"的类型配得上 —— 别的当场报错,
    // 而不是拿 `T ()` 当零值(`()` 是"没给实参",不是"要一个空的 T")。
    [Sys("Default")]
    public static RuntimeValue Default(RuntimeValue type)
        => type is ObjectVal t ? BuiltinClasses.DefaultValue(t)
            : throw new RuntimeException($"default 要一个类型，得到 {type.Type}", ErrorKind.Type);

    [Sys("RejectType")]
    public static RuntimeValue RejectType(RuntimeValue value, RuntimeValue type)
        => BuiltinClasses.NewException($"这一格要的是 {type}，得到 {value.Type}", ErrorKind.Type);

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
