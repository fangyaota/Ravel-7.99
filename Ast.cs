namespace Ravel;

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

public record VarDefinition(string Name, string? TypeAnnotation, Expression Value, List<string>? Attrs = null, bool Named = false) : Statement
{
    public bool HasAttr(string a) => Attrs?.Contains(a) ?? false;
    public bool IsInit => HasAttr("init");
    public bool IsOperator => Attrs?.Any(a => a.StartsWith("operator")) ?? false;
    public string? OperatorName => Attrs?.FirstOrDefault(a => a.StartsWith("operator"));
    public bool IsReadonly => HasAttr("readonly");
    public bool IsOverride => HasAttr("override");
    public bool IsNew => HasAttr("new");
}
public record Assignment(string Name, Expression Value) : Statement;
public record ExpressionStatement(Expression Expr) : Statement;

// --- 表达式 ---
public abstract record Expression : AstNode;

public record NumberLiteral(double Value, bool IsFloat = false) : Expression;
public record StringLiteral(string Value) : Expression;
public record IdentifierExpr(string Name) : Expression;
public record CallExpr(Expression Function, List<Expression> Arguments) : Expression;
public record MemberAccess(Expression Object, string Member) : Expression;
public record BinaryExpr(Expression Left, string Op, Expression Right) : Expression;
public record UnaryExpr(string Op, Expression Operand) : Expression;
public record LambdaExpr(Parameter Param, BlockExpr Body) : Expression;
public record PipeExpr(Expression Left, Expression Right) : Expression;
public record ListLiteral(List<Expression> Elements) : Expression;
public record SetLiteral(List<Expression> Elements) : Expression;
public record DictEntry(string Key, Expression Value);
public record DictLiteral(List<DictEntry> Entries) : Expression;
public record VoidLiteral : Expression;
public record HoleExpr(int Index) : Expression;
public record BlockExpr(List<Statement> Statements) : Expression;

// --- 辅助 ---
public record Parameter(string Name, string TypeName);

// --- 程序根 ---
public record Program(List<Statement> Statements) : AstNode;
