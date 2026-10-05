namespace Ravel;

using System.Collections.Concurrent;
using System.Text;

/// <summary>脱糖造出来的那些名字 —— `__d…`(解构的中间量)/ `__g…`(游标)/ `__p…`(模式参数),
/// 以及 `_and…` / `_or…` / `_nil…` / `_y…` 那一族(lambda 参数)。
///
/// 形状是 **`前缀 + 文件标签 + "_" + 该文件的第几个`**,比如 `__d3f9c2_0`。种类那个字母留着:
/// 翻脱糖之后的源码时,"这是解构的中间量"还是"这是枚举游标"一眼看得出来。
///
/// **为什么要带文件标签。** 这些名字是拿 `:=` 定义在**调用方那个作用域**里的 ——
/// `using` 进来的模块和主文件共享顶层作用域,`eval` 也一样,所以两个文件里的临时量真会撞上
/// (`tests/lang/318_destructure` / `tests/module/320_destructure_scope` 钉着这条)。
/// 从前靠一个**全进程单调递推**的序号躲开:不撞是做到了,代价是**一个文件印出来是什么样,
/// 取决于你之前解析过谁** —— 同一个文件,先解析一串别的再解析它,临时量就从 `__d0` 变成
/// `__d37`。`ravel ast --source` 单文件的输出因此不可复现。改成"文件名掺进去"之后,
/// 名字只取决于 **(文件, 该文件的第几个)**,两次跑一模一样。
///
/// **标签是按文件名算的确定性哈希**(FNV-1a 取低 24 位)。两条讲究:
/// * **不能用 `string.GetHashCode ()`** —— .NET 给每个进程一个随机种子,同一份源码两次跑
///   算出来的名字不一样,那就白改了;
/// * 路径**先把 `\` 归一成 `/`** —— `lib\repl.rav` 和 `lib/repl.rav` 说的是同一个文件,
///   标签得一样,不然两种调用方式印出来的东西对不上。
///
/// 24 位:两百来个文件撞上的概率在千分之一量级;真撞了是**当场报「这个名字已经存在」**
/// (响的,不是静默串味),那时候把长度加上去就是。</summary>
internal static class TempNames
{
    /// <summary>每个**文件标签**一个计数器。同一份源码解析两遍会接着往下数(于是不会自己撞
    /// 自己),不同文件各数各的(于是不受"之前解析过谁"影响)。</summary>
    private static readonly ConcurrentDictionary<string, int> Seq = new();

    /// <summary>下一个名字,比如 `__d3f9c2_7`。`source` 是这块代码来自哪个文件
    /// (取不到就一律算作同一个"无源"标签 —— 那种片段本来就该共用一个计数器)。</summary>
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
