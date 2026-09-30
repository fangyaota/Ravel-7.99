namespace Ravel.Runtime;

/// <summary>变量与字段的修饰符。
///
/// 它们在解析时是 token 文本、运行时装进 <see cref="Variable"/> 的 attrs 集合、还会经
/// `x.Attrs ()` 原样暴露给 Ravel 层——所以底层仍是字符串。但 C# 这边从前到处写
/// `HasAttr("readonly")` 这种字面量对暗号:把 readOnly 拼错一个字母不会报错,
/// 只会让那条只读检查静默消失。收成常量之后编译器管拼写,要改名也只改一处。
///
/// **这里的每一个都必须有地方读它** —— `override` / `new` 从前也在这张表里,而全库
/// 没有一处读它们:写上去等于没写,只是一句看着像承诺的注释(已删,连修饰符一起)。
/// `public` 是唯一的例外,它是**默认**行为,记下来只为念着顺。</summary>
public static class Attr
{
    public const string Readonly = "readonly";
    public const string Public = "public";
    public const string Private = "private";
    public const string Protected = "protected";
    public const string Outdated = "outdated";
    public const string Unreadable = "unreadable";
    public const string By = "by";
    public const string Core = "core";

    /// <summary>**实例可见** —— 带它的成员才沿类链兜底兜得到（<see cref="MemberView.LookupInClassChain"/>）。
    ///
    /// 从前那一半的判据是"值是个 `FunctionVal`"这么一个猜法:类对象的表里既有方法也有数据,
    /// 靠"是不是函数"来分,于是 **用户在类上挂的任何东西都会漏给所有实例**:
    ///     C.func := (x: int) => x + 1     # 从前 `c.func` 也看得到(挂在类上的都算"方法")
    /// 现在反过来:**没带这个标记的东西实例一律看不到**,标记是唯一的凭据。
    ///
    /// 引擎给自己挂的成员都带它(内置方法、类运算符、序列方法)——
    /// 它们本来就是"这一类的东西身上的方法"。用户写它要 `unsafe`
    /// (见 `Interpreter.StepVarDef`:这是内部修饰符,不是给业务代码用的开关)。</summary>
    public const string ForInstance = "forInstance";

    /// <summary>全部合法修饰符,解析器靠它认出修饰符位置的 token</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Readonly, Public, Private, Protected, Outdated, Unreadable, By, Core, ForInstance,
    };
}
