namespace Ravel.Runtime;

/// <summary>容器 ToString 的递归护栏。列表/集合/字典会展开元素,所以自引用
/// (`a := []; a.Add a; print a`) 会无限展开——不是抛异常,是直接爆 C# 栈,
/// 而 StackOverflowException 捕获不了,进程就没了。ToString 改不了签名,
/// 于是用线程静态计数器:超过 <see cref="Max"/> 层就收成 "..."。
/// 求值器是单线程的,计数用 try/finally 保证复位。</summary>
internal static class ShowDepth
{
    private const int Max = 3;

    [ThreadStatic] private static int _depth;

    public static string Guard(Func<string> render)
    {
        if (_depth >= Max) return "...";

        _depth++;
        try
        {
            return render();
        }
        finally
        {
            _depth--;
        }
    }
}
