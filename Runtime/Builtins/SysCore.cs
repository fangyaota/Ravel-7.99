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

    /// <summary>`|` 的交替里"这一支不收这个参数" —— 抛一枚 <see cref="RejectedException"/>。
    /// 参数守卫是解析器生成的调用打到这儿;手写也可以用(`reject "…"`)。
    ///
    /// **两种实参都收**:一句现成的字符串,或者一个 `Exception`(`reject (TypeError "…")`,
    /// 消息从它的 `.Message` 上取)。
    /// **从前只认后者** —— 传字符串会**静默**回落到那句通用的「这一支不收这个参数」,
    /// 写的那句话一个字都到不了用户眼前(守卫生成的正是字符串,所以守卫的消息
    /// 一直是丢的)。</summary>
    [Sys("Reject")]
    public static RuntimeValue Reject(Interpreter self, RuntimeValue msg)
        => throw new RejectedException(
               msg is StringVal s ? s.Value
               : BuiltinClasses.ExceptionMessage(msg) ?? "这一支不收这个参数");

    /// <summary>库在"没人接"时调它 —— 把引擎这次交出去的那个异常**原样**抛出</summary>
    [Sys("Unhandled")]
    public static RuntimeValue HandBack(Interpreter self, RuntimeValue e) => self.Unhandled(e);

    /// <summary>拍一份当前的模块加载状态的快照(续延要带着它一起跳)</summary>
    [Sys("LoadingState")]
    public static RuntimeValue LoadingState(Interpreter self, RuntimeValue _) => self.SnapshotLoading();

    /// <summary>把模块加载状态还原成快照那一份</summary>
    [Sys("RestoreLoading")]
    public static RuntimeValue RestoreLoadingFrom(Interpreter self, RuntimeValue snap) => self.RestoreLoading(snap);

    /// <summary>把一个异常渲染成 **CLI 那份报告** —— 位置、源码那一行、插入符、调用栈
    /// (见 `Runtime/ErrorReport.cs`)。Ravel 层自己 `string e` 只拿得到**消息那一句**,
    /// 而"错在第几行第几列"那些是**引擎内部状态**,库够不着。
    ///
    /// 位置从哪儿来:引擎交给 handler 的那个 C# 异常(见 `Interpreter.Handed`,
    /// handler 跑的时候它就摆在那儿)。所以这条是**在 handler 体里**用的 ——
    /// 写 REPL / 测试框架那种"自己接住错误再展示"的东西(`lib/repl.rav` 就是)。
    /// 拿得到就渲染整份报告;拿不到(在 handler 外面调、或者嵌套的第二次)
    /// 就退回那句消息,不报错 —— 少给点信息总比什么都不给好。
    ///
    /// **但得核对一下手上那份是不是"这一份"。** `Handed` 是**引擎刚交出去的那个错**,
    /// 问题在于它**没有一个可靠的地方可以清**:handler 可能用续延跳走(库里的 `try` 就是),
    /// 收不到"调用返回"那一刻,所以它一直挂着,直到下一次引擎错误把它顶掉。
    ///
    /// 于是**用户自己 `throw`** 的错进来时,上面挂的还是**上一次**引擎错误 —— 照渲染就会
    /// 报出别人的位置和调用栈来(实测:`1/0` 之后紧跟一个 `throw`,报告里是除法那道栈,
    /// 自己那句话反而不见了)。那种错压根没走引擎这条报错路,`Handed` 本来就不该认它。
    ///
    /// 判据现成:引擎交给 Ravel 的那一份是 `<see cref="BuiltinClasses.NewException"/>`
    /// 照着 `ex.Message` 造的,所以**消息逐字相同**;对不上一律退回那句消息。
    ///
    /// 平时不必碰它:没人接的异常,顶层自己会这么渲染。</summary>
    [Sys("FormatError")]
    public static RuntimeValue FormatError(Interpreter self, RuntimeValue e)
    {
        var msg = BuiltinClasses.ExceptionMessage(e);
        return new StringVal(self.Handed is { } ex && ex.Message == msg
            ? ErrorReport.Format(ex)
            : msg ?? Show(e));
    }
}
