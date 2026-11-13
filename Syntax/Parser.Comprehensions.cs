namespace Ravel;

//  推导式 —— `[x for i in xs if p]` / `(x for …)` / `{x for …}` / `{k -> v for …}` / 裸 `x for …`
//
//  `for` / `in` / `if` 不是关键字:词法器只发 Identifier,这里按"上下文里那个词"认。
//  所以 `for` 只能出现在**表达式收尾之后**;解析表达式时若碰到它,一律当边界
//  (见 Parser.Postfix.TryPostfixArg)—— 否则 `x for i in xs` 里 `for` 会被当成
//  `x` 的并列实参,报出「类型 'X' 没有方法 'for'」这种鬼话。
public partial class Parser
{
    private bool IsWord(string w) => Peek().Type == TokenType.Identifier && Peek().Lexeme == w;

    private bool IsCompBoundary() => IsWord("for") || IsWord("if");

    private void ConsumeWord(string w, string message)
    {
        if (!IsWord(w)) throw ParseError(message);
        _pos++;
    }

    // 包裹语法里先探一眼:这一层有没有 `for`?有才按推导式解。
    // 判据只看本层(括号/方括号/花括号里的 `for` 不算)。
    private bool ForAhead()
    {
        var depth = 0;
        for (var i = _pos; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t.Type)
            {
                case TokenType.LeftParen or TokenType.LeftBracket or TokenType.LeftBrace:
                    depth++;
                    break;
                case TokenType.RightParen or TokenType.RightBracket or TokenType.RightBrace:
                    if (depth == 0) return false;   // 这一层收尾了,`for` 不在里面
                    depth--;
                    break;
                case TokenType.Identifier when depth == 0 && t.Lexeme == "for":
                    return true;
                case TokenType.EndOfFile:
                    return false;
            }
        }

        return false;
    }

    // 从已经解析出来的值往后接 `for 名字 in 源 (if 条件)*` 与后续子句。
    private Comprehension ParseComprehension(Expression value, Expression? dictValue, CompKind kind)
    {
        var at = Peek();
        var clauses = new List<CompClause>();

        while (true)
        {
            if (IsWord("for"))
            {
                _pos++;
                var name = Peek();
                Consume(TokenType.Identifier, "`for` 后面要跟循环变量名");
                ConsumeWord("in", "循环变量后面要跟 `in`");
                clauses.Add(new CompFor(name.Lexeme, ParseCompPart())
                    { Line = name.Line, Column = name.Column });
                continue;
            }

            if (IsWord("if"))
            {
                _pos++;
                clauses.Add(new CompIf(ParseCompPart()));
                continue;
            }

            break;
        }

        if (clauses.Count == 0 || clauses[0] is not CompFor)
            throw ParseError("推导式得先写子句:`for 名字 in 序列`");

        return new Comprehension(value, dictValue, clauses, kind) { Line = at.Line, Column = at.Column };
    }

    // 子句里的表达式(序列 / 条件)。收尾停在 `for`、`if` 上 ——
    // 但**允许**并列实参,所以 `xs.Where ((x) => { … })` 这种源写得动。
    private Expression ParseCompPart() => ParseCompHead();

    // 包裹语法里那个"值":它后面的 `for` 归外层(`[x for …]` 的子句不属于 `x`)。
    // 所以这期间关掉 `ParseExpression` 末尾的裸推导式后缀 —— 否则 `[i for i in xs]`
    // 会先被值这条式子吞成一个 Generator 推导式,外层再找子句就只剩 `]`。
    private int _compSuffixOff;

    private Expression ParseCompHead()
    {
        _compSuffixOff++;
        try { return ParseExpression(Postfix.Full); }
        finally { _compSuffixOff--; }
    }

}
