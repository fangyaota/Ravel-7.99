namespace Ravel;

using System.Collections.Concurrent;
using System.Text;

internal static class TempNames
{
    private static readonly ConcurrentDictionary<string, int> Seq = new();

    public static string Next(string prefix, string? source)
    {
        var tag = Tag(source);
        return prefix + tag + "_" + Seq.AddOrUpdate(tag, 0, static (_, n) => n + 1);
    }

    private static string Tag(string? source)
    {
        var hash = 2166136261u;                                   // FNV-1a 的偏移基
        foreach (var b in Encoding.UTF8.GetBytes((source ?? "").Replace('\\', '/')))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return (hash & 0xFFFFFF).ToString("x6");
    }
}
