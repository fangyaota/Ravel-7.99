namespace Ravel;

/// <summary>一个词法单元。
///
/// `Lexeme` 是**给人看的文本**(标识符、符号本身、数字原文;字符串是**解码后**的内容 ——
/// `"a\nb"` 的 Lexeme 里是一个真换行)。要切源码的人不能拿它的长度当跨度:字符串带上引号
/// 和转义之后长度对不上(REPL 的高亮就踩过这个 —— 它拿 `Lexeme.Length + 2` 当字符串的
/// 跨度,遇到转义就划错地方)。所以源码跨度单独记在 <see cref="Length"/> 上。</summary>
public class Token(TokenType type, string lexeme, int line, int column, int length)
{
    public TokenType Type { get; } = type;
    public string Lexeme { get; } = lexeme;
    public int Line { get; } = line;
    public int Column { get; } = column;

    /// <summary>这个单元在源码里占几个字符(字符串含两边的引号)。行/列之外再给个长度,
    /// 是为了让"划出这个单元的源码区间"的调用方(REPL 高亮)不必反推。</summary>
    public int Length { get; } = length;

    /// <summary>**插值字符串**的片段(普通字符串是 null):字面量文本与"要当表达式解析的源码"
    /// 交替出现。解析器拿它把 `"你好 ${name}"` 拼成 `"" + "你好 " + string (name)` ——
    /// 所以词法只负责**切开**,不求值。</summary>
    public IReadOnlyList<StringPart>? Parts { get; init; }

    public override string ToString() => $"{Type}({Lexeme}) at {Line}:{Column}";
}

/// <summary>插值字符串里的一段。<see cref="IsExpr"/> 为真时 <see cref="Text"/> 是一段**源码**
/// (要另起一次词法+语法),行/列是它在原文件里的位置 —— 报错要指对地方。</summary>
public record StringPart(string Text, bool IsExpr, int Line, int Column);
