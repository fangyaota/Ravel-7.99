namespace Ravel.Runtime;

public static class Attr
{
    public const string Readonly = "readonly";
    public const string Public = "public";
    public const string Private = "private";
    public const string Protected = "protected";
    public const string Internal = "internal";
    public const string Outdated = "outdated";
    public const string Unreadable = "unreadable";
    public const string By = "by";
    public const string Core = "core";

    // **这一格连"遮蔽"都不许**:成员装出来一律带 `Readonly`,但那只挡赋值 —— 类体里
    // `+ := …` 是新开一格盖住继承来的成员,那是用户定义自己运算符的正经路子,不能挡。
    // 少数内建成员(`:` / `===` / `!==` / `->`)连"遮掉"都不许:装的时候多带这一个记号,
    // `Scope.Define` 在**成员链**(继承)上撞见它才报错,词法外层的遮蔽照旧放行。
    // 它不出现在 `All` 里 —— 用户写不出来,只能由引擎挂(`[ClassOp(…, ReadOnly = true)]`)。
    public const string NoShadow = "no-shadow";

    // **只在本层可见**:内层(块、lambda 体)沿词法链找过来时挡住它。
    // 别的修饰符管的都是"谁能读写这个成员",只有它管"词法链上的下一层能不能看见"。
    public const string Local = "local";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Readonly, Public, Private, Protected, Internal, Outdated, Unreadable, By, Core, Local,
    };
}
