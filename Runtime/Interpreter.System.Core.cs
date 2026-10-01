namespace Ravel.Runtime;

/// <summary>语言级的那几件小事:`property` / `assert` / `exit` / `ravel`(模块切换)。
/// 还有「是不是忘了调用?」那个诊断开关 —— 引擎自己那几条"提醒一声但不拦你"的东西。
///
/// (从前 `Property` / `Assert` / `Exit` / `RavelMod` 和随机数那几条挤在同一个分组方法里,
/// 只是因为它们都是"顺手加的";现在各归各的主题。)</summary>
public partial class Interpreter
{
    [Sys("Property")]
    private static RuntimeValue MakeProperty(RuntimeValue g, RuntimeValue s)
    {
        if (g is not FunctionVal gf) throw new RuntimeException("property 需要 getter 函数", ErrorKind.Argument);
        if (s is not FunctionVal sf) throw new RuntimeException("property 需要 setter 函数", ErrorKind.Argument);
        return new PropertyVal(gf, sf);
    }

    [Sys("Assert")]
    private static RuntimeValue Assert(RuntimeValue cond, RuntimeValue msg)
    {
        if (cond is not BoolVal b)
            throw new RuntimeException("assert 需要 bool 参数", ErrorKind.Argument);
        if (!b.Value)
            throw new RuntimeException(msg is StringVal s ? s.Value : "assertion failed: " + Show(cond), ErrorKind.Assert);
        return VoidVal.Instance;
    }

    /// <summary>参数**必须是字符串**:从前非字符串会退化成空消息,CLI 打出一个光秃秃的 `Error:`
    /// —— `exit 0` 看起来像解释器坏了。要结束程序就写一条消息(`exit "bye"`)。</summary>
    [Sys("Exit")]
    private static RuntimeValue Exit(RuntimeValue a) => throw new ExitException(As<StringVal>(a, "exit 的消息").Value);

    [Sys("RavelMod")]
    private RuntimeValue RavelMod(RuntimeValue a) => EnterModule(As<StringVal>(a, "ravel 的模块名").Value);

    /// <summary>「是不是忘了调用?」:块里**不是最后一条**的语句,值求出来是个函数就在 stderr 上
    /// 提醒一句(为什么只管非最后一条、为什么默认关,见 <see cref="WarnForgotCall"/>)。
    /// CLI 的 `--warn` 在第一条语句之前就把它打开;脚本里也能随时开/关 —— 比如只想盯住某一段:
    ///
    ///     System.WarnForgotCall true
    ///     … 可疑的那几行 …
    ///     System.WarnForgotCall false</summary>
    [Sys("WarnForgotCall")]
    private RuntimeValue SetWarnForgotCall(RuntimeValue a)
    {
        WarnForgotCall = As<BoolVal>(a, "WarnForgotCall").Value;
        return VoidVal.Instance;
    }
}
