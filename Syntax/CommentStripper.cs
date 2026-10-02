namespace Ravel;

using System.Text;

/// <summary>把源码里的**注释剥掉**,别的字符一个不动。发布产物里的标准库用它
/// (见 `Cli/Strip.cs` 与 `Ravel.csproj` 的 `StripLibComments`):那些注释是写给
/// 改这门语言的人看的,不是写给跑它的人看的。
///
/// **为什么不用正则**:`#` 只在"普通代码里"是注释 —— 字符串(`"a # b"`)、原始字符串
/// (`"""…"""`,一个字符都不动)、字符字面量(`'#'`)里的都不是。而这一套分法**词法器**
/// 已经分好了,所以这儿直接问它要区间(见 <see cref="Lexer.Comments"/>),
/// 不另写一遍扫描器:`lib/repl.rav` 里那张 ASCII 大图就是原始字符串,正则一过就碎了。
///
/// **换行留着** —— 整行注释变成一行空行。行号于是和原文**一一对应**:发布产物里报错
/// 指的行,还是仓库里那一行(这是"留着换行"换来的东西,不是偷懒)。代价是行尾可能留
/// 几个空格(注释前面那点缩进),无害:`Lexer` 不看它,谁都不看。
///
/// **剥完自验**:再词一遍,和原文的 token 序列**逐个比对**(种类/文本/行/列/跨度;
/// 连着几枚换行先折成一枚 —— 整行注释留下的空行会把它们并起来,那是"换行怎么分组"
/// 而不是内容变了,见 `Fold`)。全等才交回 —— 那就意味着"注释之外一个字符没动"。
/// 不比就写文件,是赌自己没写错;这一步要动的可是四十几个库文件,赌不起。</summary>
public static class CommentStripper
{
    /// <summary>交回剥掉注释的源码;里面没有注释就原样交回(同一个串)。
    ///
    /// 自验不过就抛 —— 那是**这个类自己的 bug**(词法器没理由对同一份源码给出两套 token),
    /// 调用方(见 `Cli/Strip.cs`)会把那个文件原样留下、不再写盘。</summary>
    public static string Strip(string source)
    {
        var lexer = new Lexer(source);
        var before = lexer.Tokenize();          // 这一趟顺带把注释区间记下来
        if (lexer.Comments.Count == 0) return source;

        var sb = new StringBuilder(source);
        // **从后往前删**:删前面那一段会让后面那些下标失效
        for (var i = lexer.Comments.Count - 1; i >= 0; i--)
            sb.Remove(lexer.Comments[i].Start, lexer.Comments[i].Length);

        var stripped = sb.ToString();
        var was = Fold(before);
        var now = Fold(new Lexer(stripped).Tokenize());
        for (var i = 0; i < Math.Max(was.Count, now.Count); i++)
        {
            if (i < was.Count && i < now.Count && Same(was[i], now[i])) continue;
            var left = i < was.Count ? was[i].ToString() : "<没了>";
            var right = i < now.Count ? now[i].ToString() : "<多出来>";
            throw new InvalidOperationException(
                $"剥掉注释之后第 {i} 个 token 对不上 —— 原文 {left} / 剥后 {right}");
        }

        return stripped;
    }

    /// <summary>把**连着几枚换行折成一枚** —— 只为了后面好比。
    ///
    /// 为什么非折不可:整行注释剥完会留下一串**空行**,而词法器把连着几个换行**合成一枚**
    /// (`Lexer` 那段:`while (source[_pos] == '\n')`)。于是"三行注释"原来给三枚换行,
    /// 剥完给一枚 —— 那差的是**换行怎么分组**,不是内容变了(而且那一枚的位置属性
    /// 本来也没人用:解析器只拿它当"语句到这儿断")。折完两边就一一对应了。</summary>
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

    /// <summary>两个 token 是不是**一模一样**:种类、文本、行、列、跨度,一样不落。
    ///
    /// 行列也一起比是划算的 —— 换行既然留着(见上),注释前后的**代码** token 连**列**
    /// 都不该变(注释总是到行尾,它后面不可能还有代码)。于是"相等"这句话比"语义没变"
    /// 还硬一点:报错位置也一个没挪。
    ///
    /// 换行那一枚是例外:折过之后它代表的是"这一段有换行",位置属性随分组走(见 `Fold`),
    /// 只比它是换行就够了。</summary>
    private static bool Same(Token a, Token b)
        => a.Type == b.Type
           && (a.Type == TokenType.Newline
               || (a.Lexeme == b.Lexeme && a.Line == b.Line && a.Column == b.Column && a.Length == b.Length));
}
