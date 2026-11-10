namespace Ravel.Runtime;

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
