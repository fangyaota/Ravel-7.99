namespace Ravel;

using System.Linq;
using Ravel.Runtime;

/// <summary>递归下降解析器,按优先级链逐层收窄(见 ParseExpression 起的各层)。
/// `_` 占位符的消糖是独立的一趟 AST 改写,在 Parser.Holes.cs。</summary>
/// <summary>`moreControlFlow` —— `--more-control-flow`(命令行)或文件头那条
/// `#program --more-control-flow=true` 开的那个开关。**默认为关**,关着的时候
/// `return` / `break` / `continue` 就是三个普通名字,和从前一模一样。</summary>
public partial class Parser(List<Token> tokens, string? source = null, bool moreControlFlow = false)
{
    private int _pos;

    private int _holeCount;

    /// <summary>`?.` / `??` / `??=` 脱糖时那几枚闭包的参数名(`_nil{n}`)。
    /// 全进程单调递增,不归零 —— 名字只要不撞就行(`_holeCount` 每条语句归零是因为
    /// 它代表"第几个洞"这个语义,这个纯粹是"取个没人用的名字")。</summary>
    private int _nilCount;

    /// <summary>当前括号/块的嵌套深度(见 Nested)</summary>
    private int _depth;
    /// <summary>嵌套上限。**这条线是护栏,不是能力** —— 递归下降靠 C# 调用栈,而
    /// StackOverflow **捕获不了**:真撞上去,进程直接死,连个语法错误都看不到。
    ///
    /// 数字要**按最坏情况**留:解析器不只在顶层跑,它还会在解释器深处被调用
    /// ( `eval` 里、Ravel 的异常 handler 里)。那儿的 C# 栈已经不浅,余量得按那儿算。
    /// 从前是 400 —— 那是**空栈上量出来的**(注释里那句"500 层没事、1000 层爆栈"),
    /// 一进深栈就不够用了:`class` 那批用例跑完再跑 `diag/152`(里面有一条 1000 层括号)
    /// 必崩,而单独跑那条没事。200 是压着最坏情况取的。</summary>
    private const int MaxDepth = 200;

    // ========================================
    //  入口
    // ========================================

    /// <summary>词法 + 语法一步到位(调用方不必重复 new Lexer/new Parser 两行)。
    /// fileName 会挂到块上,求值器报错时用它指出是哪个文件。</summary>
    public static Program ParseSource(string source, string? fileName = null)
        => ParseSource(source, fileName, DeclaresMoreControlFlow(source));

    /// <summary>同上,但开关由调用方说了算(命令行那个 `--more-control-flow` 走这条)。</summary>
    public static Program ParseSource(string source, string? fileName, bool moreControlFlow)
        => new Parser(new Lexer(source, fileName).Tokenize(), fileName, moreControlFlow).Parse();

    /// <summary>读文件头那条 `#program …`。`#` 是**注释**,词法器根本不看它 —— 所以在
    /// **读源码**这一层扫,只认**代码之前**的那些行(`#` 注释和空行可以夹在中间)。
    ///
    ///     #program --more-control-flow=true
    ///     #program --more-control-flow          (光写名字就是 true)
    ///
    /// 和命令行那个开关是**或**的关系:任一边开了就开。</summary>
    public static bool DeclaresMoreControlFlow(string source)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] != '#') return false;                 // 见到代码了,后面不认
            if (line != "#program" && !line.StartsWith("#program ")) continue;
            foreach (var part in line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (part is "--more-control-flow" or "--more-control-flow=true") return true;
        }
        return false;
    }

    /// <summary>同上,但结果作为块(模块体 / eval 代码片段用)。
    /// 位置给 1:1 而不是留 0——帧链里 Line==0 的块会被当成「没有位置」跳过,
    /// 模块体于是不会出现在调用栈里。顶层的根块(RunStack)也是这么标 1:1 的。</summary>
    public static BlockExpr ParseBlock(string source, string? fileName = null, bool moreControlFlow = false)
        => new(ParseSource(source, fileName, moreControlFlow).Statements) { Line = 1, Column = 1, Source = fileName };

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

        // 片段也要过脱糖那趟:插值里写 `(${a or b})` 一样得折成谓词
        return Lowering.Apply(new Parser(toks, file).ParseExpression());
    }

    /// <summary>把整份源码读成一棵树。
    ///
    /// **这是唯一的出口** —— `ParseSource` / `ParseBlock`(模块体、`eval`)都走它,
    /// 所以脱糖那趟(<see cref="Lowering"/>)挂在这儿就够了,单独调用点不必各自记得。</summary>
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

    /// <summary>往前数第 off 个 token 能不能**起一个操作数**(实参循环和"运算符后面跟什么"都用它)。</summary>
    private bool StartsPrimaryAt(int off)
        => TypeAt(off) is TokenType.Number or TokenType.String or TokenType.Char or TokenType.Identifier
            or TokenType.LeftParen or TokenType.LeftBracket
            or TokenType.LeftBrace;

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
