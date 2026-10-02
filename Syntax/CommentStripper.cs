namespace Ravel;

using System.Text;

/// <summary>把源码里的**注释剥掉、把空行收干净**。发布产物里的标准库用它
/// (见 `Cli/Strip.cs` 与 `Ravel.csproj` 的 `StripLibComments`):那些注释是写给
/// 改这门语言的人看的,不是写给跑它的人看的。
///
/// 两步,都**逐行**做 —— 没有哪一行会和另一行并成一行:
///
///   ① **删注释**:连同它前面同一行的那点空白一起删(`x := 1  # 说明` → `x := 1`),
///      换行留着,所以整行注释会留下一行空行。
///   ② **收空行**:连着两行以上的空行只留一行。整块注释(比如文件头上那几十行)
///      于是收成一行,而不是留下一片空场。
///
/// 行号因此**会往前挪**(空行被收掉了),但**列一个都没动**:两步都不在同一行里搬东西,
/// 除了删注释本身。自验(见下)就按这个口径比。
///
/// **为什么不用正则**:`#` 只在"普通代码里"是注释 —— 字符串(`"a # b"`)、原始字符串
/// (`"""…"""`,一个字符都不动)、字符字面量(`'#'`)里的都不是。而这一套分法**词法器**
/// 已经分好了,所以这儿直接问它要区间(见 <see cref="Lexer.Comments"/>),
/// 不另写一遍扫描器:`lib/repl.rav` 里那张 ASCII 大图就是原始字符串,正则一过就碎了。
/// 同理,**收空行也得绕开字符串里面** —— 多行字符串当中有几行全是空白是常事
/// (那张大图里就有空行),收了就把字符串的值改了。所以按 token 的跨度把那些行标出来,
/// 一个都不碰。`tests/286_repl.rav` 钉着那张大图的行数,收坏了当场红。
///
/// **剥完自验**:再词一遍,和原文的 token 序列**逐个比对**(种类/文本/**列**/跨度;
/// 连着几枚换行先折成一枚 —— 空行被收掉,换行的分组本来就会变,见 `Fold`)。
/// 全等才交回 —— 那就意味着"注释之外一个字符没动、一行代码没挪位"。不比就写文件,
/// 是赌自己没写错;这一步要动的可是四十几个库文件,赌不起。</summary>
public static class CommentStripper
{
    /// <summary>交回剥干净、收利落的源码;本来就没有注释、也没有多余空行就原样交回。
    ///
    /// 自验不过就抛 —— 那是**这个类自己的 bug**(词法器没理由对同一份源码给出两套 token),
    /// 调用方(见 `Cli/Strip.cs`)会把那个文件原样留下、不再写盘。</summary>
    public static string Strip(string source)
    {
        var lexer = new Lexer(source);
        var before = lexer.Tokenize();

        // 多行 token(多行字符串)盖住的行 —— 收空行时整行跳过,里面的空白一个字不动。
        // **注释不用管**:它们马上就被删掉了,而且注释只有一行。
        var guarded = GuardedLines(source, before);

        var stripped = CollapseBlankLines(DropComments(source, lexer.Comments), guarded);

        var was = Fold(before);
        var now = Fold(new Lexer(stripped).Tokenize());
        for (var i = 0; i < Math.Max(was.Count, now.Count); i++)
        {
            if (i < was.Count && i < now.Count && Same(was[i], now[i])) continue;
            var left = i < was.Count ? was[i].ToString() : "<没了>";
            var right = i < now.Count ? now[i].ToString() : "<多出来>";
            throw new InvalidOperationException(
                $"剥完之后第 {i} 个 token 对不上 —— 原文 {left} / 剥后 {right}");
        }

        return stripped;
    }

    /// <summary>① 把注释区间从源码里删掉(换行留着),顺带**把它前面同一行那点空白也吃掉**
    /// —— `x := 1  # 说明` 该变成 `x := 1`,而不是 `x := 1  `。
    ///
    /// 为什么吃得放心:注释只在**代码态**出现,所以它前面那段空白必定是"上一个 token 与它
    /// 之间"的空隙,不属于任何 token(不会啃到字符串里面去)。跨行不啃 —— 停在换行处。
    ///
    /// **从后往前**删:删前面那一段会让后面那些下标失效。</summary>
    private static string DropComments(string source, List<(int Start, int Length)> comments)
    {
        if (comments.Count == 0) return source;

        var sb = new StringBuilder(source);
        for (var i = comments.Count - 1; i >= 0; i--)
        {
            var (start, length) = comments[i];
            while (start > 0 && sb[start - 1] is ' ' or '\t') start--;
            sb.Remove(start, comments[i].Start + length - start);
        }

        return sb.ToString();
    }

    /// <summary>② 连着两行以上的空行只留一行。**空白行也算空行**(整行注释剥完留下的
    /// 缩进就是一片空格),`guarded` 里标着的行不看。
    ///
    /// 逐行来:每行带着自己的换行符一起收着,拼回去一个字不多不少 —— 于是**行内**的东西
    /// (列位置、行尾、`\r\n` 还是 `\n`)原样不动,变的只有"哪几行空行还在"。</summary>
    private static string CollapseBlankLines(string source, bool[] guarded)
    {
        var lines = SplitLines(source);
        var kept = new List<string>(lines.Count);
        var blanks = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsBlank(lines[i]) && (i >= guarded.Length || !guarded[i]))
            {
                if (++blanks > 1) continue;      // 连着第二行及以后的空行:丢掉
            }
            else
            {
                blanks = 0;
            }

            kept.Add(lines[i]);
        }

        return string.Concat(kept);
    }

    /// <summary>哪些行**在多行 token 里面** —— 收空行时不许碰。
    ///
    /// 只看多行的:单行的 token 那行本来就有代码,`IsBlank` 自己会挡住。
    /// 位置是拿行首偏移表算的(`行列 + 跨度` → 首尾偏移),因为 token 只给行列和跨度,
    /// 给不出绝对下标。</summary>
    private static bool[] GuardedLines(string source, List<Token> tokens)
    {
        var starts = LineStarts(source);
        var guarded = new bool[starts.Count];
        foreach (var t in tokens)
        {
            // **换行那枚要跳过**:它的跨度是"几个换行",而行列指的是**那一段的末尾** ——
            // 按行列+跨度去推首尾偏移会推到后面几行上去,于是白白挡住一片空行。
            if (t.Type == TokenType.Newline || t.Length <= 1) continue;
            var from = starts[t.Line - 1] + t.Column - 1;
            var last = LineOf(starts, Math.Min(from + t.Length - 1, source.Length - 1));
            if (last == t.Line - 1) continue;                        // 单行 token,不用管
            for (var i = t.Line - 1; i <= last; i++) guarded[i] = true;
        }

        return guarded;
    }

    /// <summary>每行第一个字符的绝对偏移(第 0 项是 0)</summary>
    private static List<int> LineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') starts.Add(i + 1);
        return starts;
    }

    /// <summary>这个偏移落在第几行(0 起)</summary>
    private static int LineOf(List<int> starts, int offset)
    {
        for (var i = starts.Count - 1; i >= 0; i--)
            if (starts[i] <= offset) return i;
        return 0;
    }

    /// <summary>按行拆,**每行带着自己那个换行符**(最后一行可能没有)——
    /// 拼回去才是原样,不会被规范化成 `\n`。</summary>
    private static List<string> SplitLines(string source)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n')
            {
                lines.Add(source[start..(i + 1)]);
                start = i + 1;
            }

        if (start < source.Length) lines.Add(source[start..]);
        return lines;
    }

    private static bool IsBlank(string line) => line.Trim().Length == 0;

    /// <summary>把**连着几枚换行折成一枚** —— 只为了后面好比。
    ///
    /// 为什么非折不可:空行收掉之后,原本被空行隔开的换行会**连成一片**,而词法器把连着
    /// 几个换行**合成一枚**(`Lexer` 那段:`while (source[_pos] == '\n')`)。那差的是
    /// **换行怎么分组**,不是内容变了(那一枚的位置属性也没人用:解析器只拿它当
    /// "语句到这儿断")。折完两边就一一对应了。</summary>
    private static List<Token> Fold(List<Token> tokens)
    {
        var folded = new List<Token>();
        foreach (var t in tokens)
        {
            if (t.Type == TokenType.Newline && folded.Count > 0 && folded[^1].Type == TokenType.Newline)
                continue;
            folded.Add(t);
        }

        return folded;
    }

    /// <summary>两个 token 是不是**一样**:种类、文本、**列**、跨度。
    ///
    /// **行不比** —— 空行收掉之后行号本来就往前挪,那是这一步要的效果(列不挪:两步都不在
    /// 同一行里搬东西)。
    ///
    /// 换行那一枚是例外:折过之后它代表的是"这一段有换行",只比它是换行就够了。</summary>
    private static bool Same(Token a, Token b)
        => a.Type == b.Type
           && (a.Type == TokenType.Newline
               || (a.Lexeme == b.Lexeme && a.Column == b.Column && a.Length == b.Length));
}
