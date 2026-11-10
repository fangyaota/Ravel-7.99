namespace Ravel;

using Ravel.Runtime;

public static class OperatorSymbols
{
    public static readonly HashSet<string> All =
        ["+", "-", "*", "/", "%", "**", "==", "!=", "<", ">", "<=", ">=", "&", "|", "^",
         "<<", ">>", "<<<", ">>>", "is", "isnot", "<:", ":>"];

    public static bool IsSymbol(string name) => All.Contains(name);

    public static readonly HashSet<string> AssignOps =
        ["=", ":=", "+=", "-=", "*=", "**=", "/=", "%="];
}

// ============================================================
//  抽象语法树 — 纯语法结构，不带类型信息
// ============================================================

public abstract record AstNode
{
    public int Line { get; init; }
    public int Column { get; init; }
}

// --- 语句 ---
public abstract record Statement : AstNode;

public record VarDefinition(string Name, Expression? TypeAnnotation, Expression Value, List<string>? Attrs = null, bool Named = false) : Statement
{
    public bool IsOperator => OperatorSymbols.IsSymbol(Name);

    public bool Preset { get; init; }
}
public record Assignment(string Name, Expression Value) : Statement;

public record SlotAssign(Expression Path, Expression Value, bool Define = false) : Statement;
public record ExpressionStatement(Expression Expr) : Statement;

public record BindStatement(string Name, Expression Monad) : Statement;

public record DoExpr(List<Statement> Statements) : Expression;

public record Destructure(List<string> Attrs, Pattern Pattern, Expression Value) : Statement
{
    public bool HasAttrs => Attrs.Count > 0;
}

public abstract record Pattern : AstNode
{
    public Expression? Type { get; init; }

    public Expression? Guard { get; init; }

    public Expression? When { get; init; }

    public List<string>? Attrs { get; init; }
}

public record LiteralPattern(Expression Value, Expression Domain, string Text) : Pattern;

public record DictPatEntry(Expression Key, Pattern Sub) : AstNode;

public record DictPattern(List<DictPatEntry> Entries) : Pattern;

public record NamePattern(string Name) : Pattern
{
    public Pattern? Sub { get; init; }
    public string? Member { get; init; }
}

public record SkipPattern : Pattern;

public record RestPattern(string Name) : Pattern;

public record ListPattern(List<Pattern> Parts) : Pattern;

public record MemberPattern(List<NamePattern> Names) : Pattern;

public record WholePattern(Pattern Inner) : Pattern;

// --- 表达式 ---
public abstract record Expression : AstNode;

public record NumberLiteral(string Lexeme, bool IsFloat = false) : Expression
{
    public char Suffix => char.IsLetter(Lexeme[^1]) ? Lexeme[^1] : '\0';

    internal string Digits => Suffix == '\0' ? Lexeme : Lexeme[..^1];

    internal RuntimeValue? Parsed;
}

public record StringLiteral(string Value) : Expression
{
    internal StringVal? Packed;
}
public record CharLiteral(char Value) : Expression;
public record IdentifierExpr(string Name) : Expression;
public record CallExpr(Expression Function, Expression Argument) : Expression
{
    public bool BareUnitArg { get; init; }
}
public record MemberAccess(Expression Object, string Member) : Expression;
public record BinaryExpr(Expression Left, string Op, Expression Right) : Expression;
public record UnaryExpr(string Op, Expression Operand) : Expression;
public record LambdaExpr(Parameter Param, BlockExpr Body) : Expression
{
    public bool Sugar { get; init; }
}
public record PipeExpr(Expression Left, Expression Right) : Expression;
public record ListLiteral(List<Expression> Elements) : Expression;
public record RangeExpr(Expression? Lo, Expression? Hi, bool StartClosed, bool EndClosed) : Expression;
public record SetLiteral(List<Expression> Elements) : Expression;
public record DictEntry(Expression Key, Expression Value);
public record DictLiteral(List<DictEntry> Entries) : Expression;
public record VoidLiteral : Expression;

public record SlotExpr(Expression Path) : Expression;

public record LiteralExpr(RuntimeValue Value) : Expression;
public record HoleExpr(int Index) : Expression;
public record BlockExpr(List<Statement> Statements) : Expression
{
    public string? Source { get; init; }

    public bool Curried => Statements is [ExpressionStatement { Expr: LambdaExpr }];
}

// --- 辅助 ---
public record Parameter(string Name, Expression Type, Pattern? Pattern = null)
{
    public List<string>? Attrs { get; init; }
}

// --- 程序根 ---
public record Program(List<Statement> Statements) : AstNode
{
    public string? Source { get; init; }
}
