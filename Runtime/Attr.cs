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

    // **只在本层可见**:内层(块、lambda 体)沿词法链找过来时挡住它。
    // 别的修饰符管的都是"谁能读写这个成员",只有它管"词法链上的下一层能不能看见"。
    public const string Local = "local";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Readonly, Public, Private, Protected, Internal, Outdated, Unreadable, By, Core, Local,
    };
}
