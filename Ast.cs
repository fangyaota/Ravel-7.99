namespace Ravel;

/// <summary>可自定义的运算符符号。类体里直接用符号定义(`+ := f` 定义、`+ = f` 覆盖),
/// 符号本身就是实例里的成员名,所以 `a.+` 能取到它。集合与 RuntimeType.Operators 注册的内置运算符一致。</summary>
public static class OperatorSymbols
{
    public static readonly HashSet<string> All =
        ["+", "-", "*", "/", "%", "==", "!=", "<", ">", "<=", ">=", "&", "|", "^"];

    public static bool IsSymbol(string name) => All.Contains(name);
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

public record VarDefinition(string Name, string? TypeAnnotation, Expression Value, List<string>? Attrs = null, bool Named = false) : Statement
{
    public bool HasAttr(string a) => Attrs?.Contains(a) ?? false;
    /// <summary>构造器:靠名字识别(类体里写 `init := () => {...}`),不再用 init 修饰符</summary>
    public bool IsInit => Name == "init";
    /// <summary>运算符:靠名字识别——类体里直接写符号(`+ := f` / `+ = f`)</summary>
    public bool IsOperator => OperatorSymbols.IsSymbol(Name);
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
public record BlockExpr(List<Statement> Statements) : Expression
{
    /// <summary>这个块来自哪个源文件(主文件 / using 的模块 / eval 的片段)。
    /// 节点本身只有行列,文件名记在块上——求值器报错和拼调用栈时沿帧链取最近的一个。</summary>
    public string? Source { get; init; }
}

// --- 辅助 ---
public record Parameter(string Name, string TypeName);

// --- 程序根 ---
public record Program(List<Statement> Statements) : AstNode
{
    /// <summary>入口源文件路径(报错时显示用)</summary>
    public string? Source { get; init; }
}
