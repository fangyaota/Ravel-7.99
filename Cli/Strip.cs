namespace Ravel;

using System.Text;
using Ravel.Runtime;

/// <summary>`ravel strip <文件或目录>` —— 把 `.rav` 里的注释剥掉,**就地**改。
///
/// 发布产物用它:`Ravel.csproj` 的 `StripLibComments` 在 publish 之后把 `out/lib/` 过一遍,
/// 于是发出去的标准库不带注释(为什么不用正则、换行为什么留着,见 `CommentStripper`)。
/// **平时 `build` 拷进 `bin/` 的那份不动** —— 调试时要读那些注释。
///
/// 就地改是只给构建用的:每个文件都先过 `CommentStripper` 的自验(比对 token),
/// 不过关就当场报错、那个文件**原样留着**。目录是**递归**收 `*.rav`。
///
/// 交回 false = 有文件没过(调用方把退出码拨 1)。</summary>
internal static class Strip
{
    public static bool Run(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            Console.WriteLine("用法: ravel strip <文件或目录>…");
            return false;
        }

        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path)) files.AddRange(Directory.GetFiles(path, "*.rav", SearchOption.AllDirectories));
            else if (File.Exists(path)) files.Add(path);
            else
            {
                Console.WriteLine($"找不到: {path}");
                return false;
            }
        }

        long removed = 0;
        foreach (var file in files)
        {
            // BOM 和换行都按原样还回去:这是往别人的文件里写字(.NET 默认写 UTF-8 **不带** BOM,
            // 而仓库里有一部分 `.rav` 是带 BOM 的),读进来什么编码、写出去还是什么编码。
            var raw = File.ReadAllBytes(file);
            var hasBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            var source = File.ReadAllText(file);

            string stripped;
            try
            {
                stripped = CommentStripper.Strip(source);
            }
            catch (Exception ex) when (ex is InvalidOperationException or SyntaxException)
            {
                // 语法错也可能是"这个文件本来就写坏了" —— 两种都报出来,别把人往 strip 上引
                Console.WriteLine($"剥不了 {file}: {ex.Message}");
                return false;
            }

            if (stripped == source) continue;
            File.WriteAllText(file, stripped, new UTF8Encoding(hasBom));
            removed += source.Length - stripped.Length;
        }

        Console.WriteLine($"剥过 {files.Count} 个文件,去掉 {removed} 个字符的注释");
        return true;
    }
}
