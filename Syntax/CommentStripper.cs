namespace Ravel;

using System.Text;
using Ravel.Runtime;

public static class CommentStripper
{
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
                Messages.T("parse.strip.token_mismatch", i, left, right));
        }

        return stripped;
    }

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

    private static List<int> LineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < source.Length; i++)
            if (source[i] == '\n') starts.Add(i + 1);
        return starts;
    }

    private static int LineOf(List<int> starts, int offset)
    {
        for (var i = starts.Count - 1; i >= 0; i--)
            if (starts[i] <= offset) return i;
        return 0;
    }

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

    private static bool Same(Token a, Token b)
        => a.Type == b.Type
           && (a.Type == TokenType.Newline
               || (a.Lexeme == b.Lexeme && a.Column == b.Column && a.Length == b.Length));
}
