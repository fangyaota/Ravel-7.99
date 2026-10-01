namespace Ravel;

using System.Linq;
using Ravel.Runtime;

/// <summary>递归下降解析器,按优先级链逐层收窄(见 ParseExpression 起的各层)。
/// `_` 占位符的消糖是独立的一趟 AST 改写,在 Parser.Holes.cs。</summary>
public partial class Parser(List<Token> tokens, string? source = null)
{
    private int _pos;

    private int _holeCount;

    /// <summary>`?.` / `??` / `??=` 脱糖时那几枚闭包的参数名(`_nil{n}`)。
    /// 全进程单调递增,不归零 —— 名字只要不撞就行(`_holeCount` 每条语句归零是因为
    /// 它代表"第几个洞"这个语义,这个纯粹是"取个没人用的名字")。</summary>
    private int _nilCount;

    /// <summary>当前括号/块的嵌套深度(见 Nested)</summary>
    private int _depth;
    private const int MaxDepth = 400;

    // ========================================
    //  入口
    // ========================================

    /// <summary>词法 + 语法一步到位(调用方不必重复 new Lexer/new Parser 两行)。
    /// fileName 会挂到块上,求值器报错时用它指出是哪个文件。</summary>
    public static Program ParseSource(string source, string? fileName = null)
        => new Parser(new Lexer(source, fileName).Tokenize(), fileName).Parse();

    /// <summary>同上,但结果作为块(模块体 / eval 代码片段用)。
    /// 位置给 1:1 而不是留 0——帧链里 Line==0 的块会被当成「没有位置」跳过,
    /// 模块体于是不会出现在调用栈里。顶层的根块(RunStack)也是这么标 1:1 的。</summary>
    public static BlockExpr ParseBlock(string source, string? fileName = null)
        => new(ParseSource(source, fileName).Statements) { Line = 1, Column = 1, Source = fileName };

    /// <summary>把一段**源码片段**当**表达式**解析 —— 插值字符串用(`"你好 ${name}"` 里那段
    /// `name` 就是)。片段来自字符串字面量内部,所以它的 token 位置是相对的:按它在原文件里的
    /// 行列挪回去,报错才指得对地方(片段跨行时列只修第一行,够用了)。</summary>
    internal static Expression ParseExpressionSnippet(string src, string? file, int line, int col)
    {
        var toks = new Lexer(src, file).Tokenize();
        for (var i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            toks[i] = new Token(t.Type, t.Lexeme, t.Line + line - 1,
                t.Line == 1 ? t.Column + col - 1 : t.Column, t.Length) { Parts = t.Parts };
        }

        return new Parser(toks, file).ParseExpression();
    }

    public Program Parse()
    {
        var statements = new List<Statement>();

        while (!IsAtEnd())
        {
            if (Match(TokenType.Newline))
                continue;
            statements.Add(ParseStatement());
        }

        return new Program(statements) { Source = source };
    }

    // ========================================
    //  辅助
    // ========================================

    private bool StartsPrimary()
    {
        if (IsAtEnd()) return false;
        return Peek().Type is TokenType.Number or TokenType.String or TokenType.Char or TokenType.Identifier
            or TokenType.LeftParen or TokenType.LeftBracket
            or TokenType.LeftBrace;
    }

    /// <summary>把 token 说成人话,给报错用。
    /// `Token.ToString()` 是 `EndOfFile() at 1:4` 那种调试格式(类型名 + 行列),
    /// 直接插进用户消息里就成了「需要表达式，但得到 EndOfFile() at 1:4」——
    /// 行列号 ErrorReport 会另外画,这里只该说「看到的是什么」。</summary>
    private static string Describe(Token t) => t.Type switch
    {
        TokenType.EndOfFile => "表达式结束",
        TokenType.Newline => "换行",
        TokenType.Identifier => $"标识符 '{t.Lexeme}'",
        TokenType.Number or TokenType.String => $"字面量 {t.Lexeme}",
        _ => $"'{t.Lexeme}'",
    };

    private Token Peek() => tokens[_pos];
    private Token Previous() => tokens[_pos - 1];
    private bool IsAtEnd() => _pos >= tokens.Count || tokens[_pos].Type == TokenType.EndOfFile;

    private bool Check(TokenType type) => !IsAtEnd() && Peek().Type == type;

    /// <summary>往前数第 off 个 token 的类型(0 = 当前)。**越界当文件结束** ——
    /// 于是所有前瞻在末尾自动为假,调用点不必各自判越界。</summary>
    private TokenType TypeAt(int off) => _pos + off < tokens.Count ? tokens[_pos + off].Type : TokenType.EndOfFile;

    private TokenType NextType() => TypeAt(1);

    private bool CheckNext(TokenType type) => NextType() == type;

    private bool Match(TokenType type)
    {
        if (Check(type))
        {
            _pos++;
            return true;
        }

        return false;
    }

    private Token Consume(TokenType type, string errorMessage)
    {
        if (Check(type)) return tokens[_pos++];
        throw ParseError(errorMessage);
    }

    private void SkipNewlines()
    {
        while (Match(TokenType.Newline))
        {
        }
    }

    /// <summary>位置不再写进消息里——ErrorReport 会画 `--> file:line:col` 和插入符,
    /// 重复写一遍只是噪音。`附近` 也由插入符接管(读不到源文件的 eval 片段除外)。</summary>
    private SyntaxException ParseError(string message)
    {
        var token = IsAtEnd() ? tokens[^1] : Peek();
        return new SyntaxException(message, new SourceSpot(source, token.Line, token.Column));
    }
}
