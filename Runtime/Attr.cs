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

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Readonly, Public, Private, Protected, Internal, Outdated, Unreadable, By, Core,
    };
}
