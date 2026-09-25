namespace Ravel.Repl;

using Spectre.Console;

using System.Text;

/// <summary>REPL 编辑区的行渲染:行号 + 按词法 token 上色的源码,光标处画下划线块。
/// 纯函数——只依赖传入的行文本和光标列,不碰 REPL 状态,便于单独验证。</summary>
internal static class ReplView
{
    /// <summary>渲染一行;cursorX 非空表示「光标就在这一行」,值为光标列(可等于行长=行尾)</summary>
    public static string RenderRow(int lineNumber, string text, int? cursorX)
    {
        var sb = new StringBuilder();
        sb.Append($"[blue]{lineNumber.ToString().PadLeft(3)}|[/] ");

        List<Token> tokens;
        try
        {
            tokens = new Lexer(text).Tokenize();
        }
        catch (Exception)
        {
            // 输入中遇到未识别字符,词法未完成:不高亮,原样显示
            sb.Append(text.EscapeMarkup());
            if (cursorX == text.Length)
                sb.Append("[underline red] [/]");
            sb.AppendLine();
            return sb.ToString();
        }

        int pos = 0;
        foreach (var tok in tokens)
        {
            if (tok.Type == TokenType.EndOfFile || tok.Type == TokenType.Newline)
                continue;

            int start = tok.Column - 1;
            int end = start + tok.Lexeme.Length;
            if (tok.Type == TokenType.String)
                end += 2;
            if (end > text.Length)
                end = text.Length;

            if (pos < start)
                sb.Append(text[pos..start].EscapeMarkup());

            string color = tok.Type switch
            {
                TokenType.String => "yellow",
                TokenType.Number => "lime",
                TokenType.Identifier => "cyan",
                _ => "bold"
            };
            sb.Append($"[{color}]");

            if (cursorX is { } cx && start <= cx && cx < end)
            {
                // 光标落在本 token 内:把那一格单独包成下划线
                sb.Append(text[start..cx].EscapeMarkup());
                sb.Append("[underline red]");
                sb.Append(text[cx].ToString().EscapeMarkup());
                sb.Append("[/]");
                sb.Append(text[(cx + 1)..end].EscapeMarkup());
            }
            else
            {
                sb.Append(text[start..end].EscapeMarkup());
            }

            sb.Append("[/]");
            pos = end;
        }

        if (pos < text.Length)
            sb.Append(text[pos..].EscapeMarkup());

        if (cursorX == text.Length)
            sb.Append("[underline red] [/]");

        sb.AppendLine();
        return sb.ToString();
    }
}
