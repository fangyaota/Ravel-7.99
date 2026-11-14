namespace Ravel.Runtime;

using System.Reflection;
using System.Text;

// 内置报错的文案,放在可读的 TOML 里:`i18n/zh.toml`(中性语言,唯一真源)、`i18n/en.toml`(可选)。
//
//     [parse]
//     # 词法器
//     unexpected_char = "未预期的字符 '{0}'"
//
// 规则就三条:`[节.子节]` 分节;`键 = "文案"`;`#` 到行尾是注释。`{0}` `{1}` 是占位符,`\n` 表示换行。
// `zh.toml` 同时**编进程序集一份**当兜底 —— 发布目录里删了文件也不会满屏 key。
//
// 切语言:`RAVEL_LANG=en`(或命令行 `--lang en`)→ 读 `<可执行文件目录>/i18n/en.toml`;
// 某一条没翻就回退中文。默认永远是中文,金标用例不随机器区域设置变。
public static class Messages
{
    private const string Neutral = "zh";

    private static readonly Dictionary<string, string> Zh = Load(Embedded(Neutral) ?? "");
    private static Dictionary<string, string> Over = Zh;

    /// 当前语言;`zh` 表示"用编进来的中文原文"。
    public static string Lang { get; private set; } = Neutral;

    public static string T(string key, params object?[] args)
    {
        var text = Over.TryGetValue(key, out var found) ? found
            : Zh.TryGetValue(key, out var fallback) ? fallback
            : key;
        if (args.Length == 0) return text;
        try { return string.Format(text, args); }
        catch (FormatException) { return text; }   // 译文里写坏了占位符:宁可露出原文
    }

    /// 切语言;找不到 `<exe>/i18n/<code>.toml` 就留在原来的语言。
    public static bool Use(string code)
    {
        if (code == Neutral) { Over = Zh; Lang = Neutral; return true; }

        var file = Path.Combine(AppContext.BaseDirectory, "i18n", code + ".toml");
        if (!File.Exists(file)) return false;

        var table = new Dictionary<string, string>(Zh, StringComparer.Ordinal);
        foreach (var (k, v) in Load(File.ReadAllText(file, Encoding.UTF8))) table[k] = v;
        Over = table;
        Lang = code;
        return true;
    }

    public static void FromEnvironment()
    {
        var code = Environment.GetEnvironmentVariable("RAVEL_LANG");
        if (!string.IsNullOrWhiteSpace(code)) Use(code.Trim());
    }

    private static string? Embedded(string code)
    {
        var asm = typeof(Messages).Assembly;
        var name = $"Ravel.i18n.{code}.toml";
        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// 极小的 TOML 子集:节头 + `键 = "值"` + 注释。不认多行字符串/数组 —— 文案不需要。
    private static Dictionary<string, string> Load(string text)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        var section = "";

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            if (line[0] == '[')
            {
                var close = line.IndexOf(']');
                if (close > 0) section = line[1..close].Trim();
                continue;
            }

            var eq = IndexOfOutsideQuotes(line, '=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var quote = line.IndexOf('"', eq + 1);
            if (quote < 0) continue;
            var value = ReadString(line, quote);
            table[section.Length == 0 ? key : section + "." + key] = value;
        }

        return table;
    }

    private static int IndexOfOutsideQuotes(string s, char c)
    {
        var inQuote = false;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && inQuote) { i++; continue; }
            if (s[i] == '"') inQuote = !inQuote;
            else if (s[i] == c && !inQuote) return i;
        }
        return -1;
    }

    private static string ReadString(string s, int quote)
    {
        var sb = new StringBuilder();
        for (var i = quote + 1; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '"') break;
            if (ch != '\\') { sb.Append(ch); continue; }
            if (++i >= s.Length) break;
            sb.Append(s[i] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\', var other => other });
        }
        return sb.ToString();
    }
}
