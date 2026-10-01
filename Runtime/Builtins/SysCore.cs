namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

/// <summary>语言级的那几件小事:`property` / `assert` / `exit` / `ravel`(模块切换)。
/// 还有「是不是忘了调用?」那个诊断开关 —— 引擎自己那几条"提醒一声但不拦你"的东西。
///
/// (从前 `Property` / `Assert` / `Exit` / `RavelMod` 和随机数那几条挤在同一个分组方法里,
/// 只是因为它们都是"顺手加的";现在各归各的主题。)</summary>
internal static class SysCore
{
    [Sys("Property")]
    public static RuntimeValue MakeProperty(RuntimeValue g, RuntimeValue s)
    {
        if (g is not FunctionVal gf) throw new RuntimeException("property 需要 getter 函数", ErrorKind.Argument);
        if (s is not FunctionVal sf) throw new RuntimeException("property 需要 setter 函数", ErrorKind.Argument);
        return new PropertyVal(gf, sf);
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

    /// <summary>参数**必须是字符串**:从前非字符串会退化成空消息,CLI 打出一个光秃秃的 `Error:`
    /// —— `exit 0` 看起来像解释器坏了。要结束程序就写一条消息(`exit "bye"`)。</summary>
    [Sys("Exit")]
    public static RuntimeValue Exit(RuntimeValue a) => throw new ExitException(As<StringVal>(a, "exit 的消息").Value);

    [Sys("RavelMod")]
    public static RuntimeValue RavelMod(Interpreter self, RuntimeValue a)
        => self.EnterModule(As<StringVal>(a, "ravel 的模块名").Value);

    /// <summary>「是不是忘了调用?」:块里**不是最后一条**的语句,值求出来是个函数就在 stderr 上
    /// 提醒一句(为什么只管非最后一条、为什么默认关,见 <see cref="WarnForgotCall"/>)。
    /// CLI 的 `--warn` 在第一条语句之前就把它打开;脚本里也能随时开/关 —— 比如只想盯住某一段:
    ///
    ///     System.WarnForgotCall true
    ///     … 可疑的那几行 …
    ///     System.WarnForgotCall false</summary>
    [Sys("WarnForgotCall")]
    public static RuntimeValue SetWarnForgotCall(Interpreter self, RuntimeValue a)
    {
        self.WarnForgotCall = As<BoolVal>(a, "WarnForgotCall").Value;
        return VoidVal.Instance;
    }
    // ── 引擎自己的两样状态:错误交给谁、模块正加载到哪儿 ──
    // 它们是**库与引擎之间的桥**(见 predefined.rav 的 `callcc` 与 exceptions.rav 的 `onError`),
    // 所以放在"语言级小件"这一主题里。

    /// <summary>库注册的"错误交给谁":引擎冒泡上来一个错误时只做一件事 —— 把这个函数调起来。
    /// 引擎**不认识** handler 栈(那是 `lib/exceptions.rav` 的状态,就在 Ravel 里那个 list 上),
    /// 有没有人接、没人接怎么办由库决定。</summary>
    [Sys("SetErrorHook")]
    public static RuntimeValue SetErrorHook(Interpreter self, RuntimeValue a)
    {
        self.ErrorHook = a as FunctionVal
            ?? throw new RuntimeException($"SetErrorHook 要一个函数，得到 {a.Type}", ErrorKind.Argument);
        return VoidVal.Instance;
    }

    /// <summary>库在"没人接"时调它 —— 把引擎这次交出去的那个异常**原样**抛出</summary>
    [Sys("Unhandled")]
    public static RuntimeValue HandBack(Interpreter self, RuntimeValue e) => self.Unhandled(e);

    /// <summary>拍一份当前的模块加载状态的快照(续延要带着它一起跳)</summary>
    [Sys("LoadingState")]
    public static RuntimeValue LoadingState(Interpreter self, RuntimeValue _) => self.SnapshotLoading();

    /// <summary>把模块加载状态还原成快照那一份</summary>
    [Sys("RestoreLoading")]
    public static RuntimeValue RestoreLoadingFrom(Interpreter self, RuntimeValue snap) => self.RestoreLoading(snap);
}
