namespace Ravel.Repl;

using System.Text.Json;

/// <summary>REPL 编辑缓冲的持久化(多页文本)。读写失败一律静默——
/// 会话文件坏掉不该让 REPL 起不来。</summary>
internal static class ReplSession
{
    private const string SessionPath = "repl_session.json";

    /// <summary>读回上次的页;文件缺失/损坏/内容为空 → 返回一个空白页</summary>
    public static List<List<string>> Load()
    {
        List<List<string>>? pages = null;
        try
        {
            if (File.Exists(SessionPath))
                pages = JsonSerializer.Deserialize<List<List<string>>>(File.ReadAllText(SessionPath));
        }
        catch
        {
            pages = null;
        }

        if (pages == null || pages.Count == 0) return [new()];
        return pages;
    }

    public static void Save(List<List<string>> pages)
    {
        try
        {
            File.WriteAllText(SessionPath, JsonSerializer.Serialize(pages));
        }
        catch
        {
            // 忽略保存失败
        }
    }
}
