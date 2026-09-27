namespace Ravel;

using Ravel.Runtime;

/// <summary>可自定义的运算符符号。类体里直接用符号定义(`+ := f` 定义、`+ = f` 覆盖),
/// 符号本身就是实例里的成员名,所以 `a.+` 能取到它。集合与 BuiltinClasses.Operators 注册的内置运算符一致。
///
/// `is` / `isnot` 是**词形运算符**(类型判定):它们不是标点,所以解析器在
/// **运算符位置**按词判定(见 `Parser.IsWordOperator`)—— 别处照样能当普通标识符用,
/// 于是 `1.is`(等右操作数)和 `is.int`(等左操作数)两种节形式和 `a.+` / `+.2` 完全对称。</summary>
public static class OperatorSymbols
{
    public static readonly HashSet<string> All =
        ["+", "-", "*", "/", "%", "==", "!=", "<", ">", "<=", ">=", "&", "|", "^", "is", "isnot"];

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

public record VarDefinition(string Name, Expression? TypeAnnotation, Expression Value, List<string>? Attrs = null, bool Named = false) : Statement
{
    public bool HasAttr(string a) => Attrs?.Contains(a) ?? false;
    /// <summary>构造器:靠名字识别(类体里写 `init := () => {...}`),不再用 init 修饰符</summary>
    public bool IsInit => Name == "init";
    /// <summary>运算符:靠名字识别——类体里直接写符号(`+ := f` / `+ = f`)</summary>
    public bool IsOperator => OperatorSymbols.IsSymbol(Name);
    public bool IsReadonly => HasAttr(Attr.Readonly);
    public bool IsOverride => HasAttr(Attr.Override);
    public bool IsNew => HasAttr(Attr.New);
}
public record Assignment(string Name, Expression Value) : Statement;
public record ExpressionStatement(Expression Expr) : Statement;

// --- 表达式 ---
public abstract record Expression : AstNode;

/// <summary>数字文字。存**原始文本**而不是 double:double 只有 15~17 位有效数字,
/// 大整数转一手就丢精度(12345678901234567890 → 12345678901234567000),再转回来已经不是原来那个数。</summary>
public record NumberLiteral(string Lexeme, bool IsFloat = false) : Expression;
public record StringLiteral(string Value) : Expression;
public record IdentifierExpr(string Name) : Expression;
/// <summary>调用:`函数 参数`。**参数只有一个** —— 多参靠柯里化,`f a b` 解析成 `(f a) b`
/// (见 Parser.Expressions 的 juxtaposition)。从前这里存的是个 List,而它恒有一个元素:
/// 每个读点都得写 `Arguments[0]`,还得提防"要是空了/多了呢"。
/// 注解里的多参 lambda(`(a: int b: int) => …`)同样只是"体里再套一层 lambda"的糖。</summary>
public record CallExpr(Expression Function, Expression Argument) : Expression;
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

/// <summary>直接求值成一个 C# 侧造好的值。给内置类的**预设类体**用 —— 那些成员
/// (类型转换器、`type` 的默认建类函数)是 C# 函数,写不出 Ravel 源码来。
/// 造类体走 BuiltinClasses.PresetBody。</summary>
public record LiteralExpr(RuntimeValue Value) : Expression;
public record HoleExpr(int Index) : Expression;
public record BlockExpr(List<Statement> Statements) : Expression
{
    /// <summary>这个块来自哪个源文件(主文件 / using 的模块 / eval 的片段)。
    /// 节点本身只有行列,文件名记在块上——求值器报错和拼调用栈时沿帧链取最近的一个。</summary>
    public string? Source { get; init; }
}

// --- 辅助 ---
/// <summary>参数。类型是**表达式**(常见是一个名字,也可以是括号里的表达式),
/// 求值在那个 lambda 被创建时做 —— 见 <see cref="VarDefinition.TypeAnnotation"/>。</summary>
public record Parameter(string Name, Expression Type);

// --- 程序根 ---
public record Program(List<Statement> Statements) : AstNode
{
    /// <summary>入口源文件路径(报错时显示用)</summary>
    public string? Source { get; init; }
}
