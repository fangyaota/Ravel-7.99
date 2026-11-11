namespace Ravel;

using System.Linq;
using Ravel.Runtime;

public partial class Parser(List<Token> tokens, string? source = null, bool moreControlFlow = false)
{
    private int _pos;

    private int _holeCount;

    private int _nilCount;

    private int _depth;
    private const int MaxDepth = 200;

    private int _exprDepth;

    // `#ignoreTypes true` 之后、`#ignoreTypes false` 之前:所有类型注解都只当摆设,
    // lowering 不为它们发检查（变量定义、参数定义、模式匹配都算）。
    private bool _ignoreTypes;

    private const int MaxExprDepth = 400;

    private readonly struct ExprDepth : IDisposable
    {
        private readonly Parser _parser;
        private readonly int _start;

        private ExprDepth(Parser parser, int start)
        {
            _parser = parser;
            _start = start;
        }

        public static ExprDepth Begin(Parser parser) => new(parser, parser._exprDepth);

        public void Dispose() => _parser._exprDepth = _start;
    }

    // ========================================
    //  入口
    // ========================================

    public static Program ParseSource(string source, string? fileName = null)
        => ParseSource(source, fileName, DeclaresMoreControlFlow(source));

    public static Program ParseSource(string source, string? fileName, bool moreControlFlow)
        => new Parser(new Lexer(source, fileName).Tokenize(), fileName, moreControlFlow).Parse();

    public static bool DeclaresMoreControlFlow(string source) => Declares(source, "--more-control-flow");

    private void TakeDirective()
    {
        _ignoreTypes = Peek().Lexeme == "true";
        _pos++;
    }

    public static bool DeclaresWarn(string source) => Declares(source, "--warn");

    private static bool Declares(string source, string name)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] != '#') return false;                 // 见到代码了,后面不认
            if (line != "#program" && !line.StartsWith("#program ")) continue;
            foreach (var part in line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (part == name || part == name + "=true") return true;
        }
        return false;
    }

    public static BlockExpr ParseBlock(string source, string? fileName = null, bool moreControlFlow = false)
        => new(ParseSource(source, fileName, moreControlFlow).Statements) { Line = 1, Column = 1, Source = fileName };

    internal static Expression ParseExpressionSnippet(string src, string? file, int line, int col)
    {
        var toks = new Lexer(src, file).Tokenize();
        for (var i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            toks[i] = new Token(t.Type, t.Lexeme, t.Line + line - 1,
                t.Line == 1 ? t.Column + col - 1 : t.Column, t.Length) { Parts = t.Parts };
        }

        // 片段也要过脱糖那趟:插值里写 `(${a or b})` 一样得折成谓词。
        // 文件名传进去 —— 折出来的块得带上它,报错和调用栈要沿帧链找。
        return Lowering.Apply(new Parser(toks, file).ParseExpression(), file);
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

        return Lowering.Apply(new Program(statements) { Source = source });
    }

    // ========================================
    //  辅助
    // ========================================

    private bool StartsPrimary() => StartsPrimaryAt(0);

    private bool StartsPrimaryAt(int off)
        => TypeAt(off) is TokenType.Number or TokenType.String or TokenType.Char or TokenType.Identifier
            or TokenType.LeftParen or TokenType.LeftBracket
            or TokenType.LeftBrace
            or TokenType.Tilde;      // `~x` 是个操作数 —— 于是 `f ~5` 能把块当实参传

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

    private SyntaxException ParseError(string message)
    {
        var token = IsAtEnd() ? tokens[^1] : Peek();
        return new SyntaxException(message, new SourceSpot(source, token.Line, token.Column));
    }
}
