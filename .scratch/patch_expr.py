# -*- coding: utf-8 -*-
import io

p = 'Syntax/Parser.Expressions.cs'
s = io.open(p, encoding='utf-8-sig').read()
if not s.startswith('\ufeff'):
    s = '\ufeff' + s

BUMP_HELPER = (
    "    /// <summary>链上又挂了一节(运算符 / 一元 / 成员访问 / 乘方 / 空值合并)。\n"
    "    /// 数字超了就是 <see cref=\"MaxExprDepth\"/> —— 再往下走,打印器和脱糖那几趟递归\n"
    "    /// 会先把 C# 栈用光。</summary>\n"
    "    private void BumpExprDepth()\n"
    "    {\n"
    "        if (++_exprDepth > MaxExprDepth)\n"
    "            throw ParseError($\"表达式嵌套太深（超过 {MaxExprDepth} 层运算符/调用链）\");\n"
    "    }\n\n"
    "    /// <summary>主表达式后面那一串**并列的调用/实参**(`f a b` / `|>`)。</summary>"
)

reps = [
    ("    private Expression ParseClimb(bool allowCall, int minBp)\n"
     "    {\n"
     "        using var _climb = ExprDepth.Enter(this);\n"
     "        var left = ParsePower(allowCall);",
     "    private Expression ParseClimb(bool allowCall, int minBp)\n"
     "    {\n"
     "        using var _climb = ExprDepth.Begin(this);\n"
     "        var left = ParsePower(allowCall);"),

    ("            _pos++;\n"
     "            var op = t.Lexeme;\n"
     "            var right = ParseClimb(allowCall, rbp);",
     "            _pos++;\n"
     "            BumpExprDepth();                        // 链上又挂了一节,树也跟着长一层\n"
     "            var op = t.Lexeme;\n"
     "            var right = ParseClimb(allowCall, rbp);"),

    ("    private Expression ParsePower(bool allowCall = true)\n"
     "    {\n"
     "        using var _pow = ExprDepth.Enter(this);\n"
     "        var left = ParseCall(allowCall);\n"
     "\n"
     "        if (Match(TokenType.StarStar))\n"
     "        {\n"
     "            var op = Previous().Lexeme;",
     "    private Expression ParsePower(bool allowCall = true)\n"
     "    {\n"
     "        var left = ParseCall(allowCall);\n"
     "\n"
     "        if (Match(TokenType.StarStar))\n"
     "        {\n"
     "            BumpExprDepth();                        // `**` 右结合:右边那个就是下一层\n"
     "            var op = Previous().Lexeme;"),

    ("    private Expression ParseCall(bool allowCall = true)\n"
     "    {\n"
     "        using var _call = ExprDepth.Enter(this);\n"
     "\n",
     "    private Expression ParseCall(bool allowCall = true)\n"
     "    {\n"),

    ("        if (Match(TokenType.Bang) || Match(TokenType.Minus))\n"
     "        {\n"
     "            var op = Previous();\n"
     "            return new UnaryExpr(op.Lexeme, ParsePower(allowCall)) { Line = op.Line, Column = op.Column };\n"
     "        }",
     "        if (Match(TokenType.Bang) || Match(TokenType.Minus))\n"
     "        {\n"
     "            BumpExprDepth();                        // `! ! !x` / `- - -1`:一元也是一层递归\n"
     "            var op = Previous();\n"
     "            return new UnaryExpr(op.Lexeme, ParsePower(allowCall)) { Line = op.Line, Column = op.Column };\n"
     "        }"),

    ("        var op = Previous();\n"
     "        var right = ParseNullCoalesce(allowCall);          // 右结合:`a ?? b ?? c` 是 `a ?? (b ?? c)`",
     "        BumpExprDepth();                            // 右结合:`a ?? b ?? c` 是 `a ?? (b ?? c)`,每层一个栈帧\n"
     "        var op = Previous();\n"
     "        var right = ParseNullCoalesce(allowCall);          // 右结合:`a ?? b ?? c` 是 `a ?? (b ?? c)`"),

    ("    private Expression GuardedChain(Expression receiver, bool allowCall, Token q)\n"
     "    {\n"
     "        using var _guard = ExprDepth.Enter(this);\n",
     "    private Expression GuardedChain(Expression receiver, bool allowCall, Token q)\n"
     "    {\n"),

    ("                var member = ParseMemberName();\n"
     "                e = new MemberAccess(e, member) { Line = e.Line, Column = e.Column };\n"
     "                continue;\n"
     "            }",
     "                BumpExprDepth();                    // `a.b.c…`:成员访问也是一层接一层\n"
     "                var member = ParseMemberName();\n"
     "                e = new MemberAccess(e, member) { Line = e.Line, Column = e.Column };\n"
     "                continue;\n"
     "            }"),

    ("                if (guarded)\n"
     "                {\n"
     "                    var m = ParseMemberName();",
     "                if (guarded)\n"
     "                {\n"
     "                    BumpExprDepth();\n"
     "                    var m = ParseMemberName();"),

    ("    private Expression ParsePostfixRest(Expression expr, bool allowCall, bool guarded = false,",
     BUMP_HELPER.replace("    /// <summary>主表达式后面那一串**并列的调用/实参**(`f a b` / `|>`)。</summary>",
                        "    private Expression ParsePostfixRest(Expression expr, bool allowCall, bool guarded = false,")),
]

for a, b in reps:
    if s.count(a) != 1:
        raise SystemExit('MISS %d: %r' % (s.count(a), a[:70]))
    s = s.replace(a, b)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
