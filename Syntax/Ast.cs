namespace Ravel;

using Ravel.Runtime;

public static class OperatorSymbols
{
    public static readonly HashSet<string> All =
        ["+", "-", "*", "/", "%", "**", "==", "!=", "===", "!==", "<", ">", "<=", ">=", "&", "|", "^",
         "<<", ">>", "<<<", ">>>", "->", "is", "isnot", "<:", ":>"];

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

    // 脱糖造出来的那条(`(x: int) => …` 体里把 `x` 绑到实参上) —— 印签名时跳过。
    public bool Synthetic { get; init; }

    // `#ignoreTypes true` 之下：注解还在（`default T` 还要它），但不发检查
    public bool IgnoreType { get; init; }
}
public record Assignment(string Name, Expression Value) : Statement;

public record SlotAssign(Expression Path, Expression Value, bool Define = false) : Statement;
// `Synthetic` = 脱糖造出来的,不是用户写的(`(x: int) => …` 体最前头那条注解检查)
// ——**求值时照跑**,只是印出来(AST 转储 / 函数签名)时跳过,
// 不然 `print f` 会印出 `!(x : int) (…)` 那一坨。
public record ExpressionStatement(Expression Expr) : Statement
{
    public bool Synthetic { get; init; }
}

// 开关语句(`#ignore-types true` / `#more-control-flow true`)——不产出代码,
// 但**得原样印回去**:源码转储里那一行就是它(`Text` 带着 `#`),印成 `()` 会把开关丢了。
public record DirectiveStatement(string Text) : Statement;

public record BindStatement(string Name, Expression Monad) : Statement;

public record DoExpr(List<Statement> Statements) : Expression;

public record Destructure(List<string> Attrs, Pattern Pattern, Expression Value) : Statement
{
    public bool HasAttrs => Attrs.Count > 0;

    // `[a b] = e`(没有类型注解的那种):里面的名字用**赋值**落,不是定义。
    public bool Assign { get; init; }
}

public abstract record Pattern : AstNode
{
    public Expression? Type { get; init; }

    // `#ignoreTypes true` 之下：注解还在（印得出来），但不发检查
    public bool IgnoreType { get; init; }

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

// 这一格是个**位置**(`l[2]` / `a.b`)—— 只有赋值(`=`)时才成立,定义(`:=`)只给名字。
public record PlacePattern(Expression Place) : Pattern;

public record ListPattern(List<Pattern> Parts) : Pattern;

public record MemberPattern(List<NamePattern> Names) : Pattern;

public record WholePattern(Pattern Inner) : Pattern;

// --- 表达式 ---
public abstract record Expression : AstNode
{
    // 这个式子外面套着**显式括号**。链式比较只串裸的比邻运算符:
    // `(1 < 2) == true` 里那个 `<` 是"括号里的一整句",不再跟外面的 `==` 串。
    public bool Parenthesized { get; init; }
}

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

// 插值字符串:`f"共 {n} 件"`(老的 `"${n}"` 也走这一个节点)。
// **这里只把原料摆好** —— 折成 `"" + "共 " + string(n) + " 件"` 是 `Lowering` 的事,
// 解析器不替它做(`f"…"` 是语法糖,糖纸就撕在 lowering 那一层)。
public record FStringExpr(List<FStringPart> Parts) : Expression;

// 一段:要么是字面量(`Interp` 为空),要么是 `{…}` 里那个表达式。字面量的 `Text`
// 已经是**解码后**的文本(转义、`{{` 都在词法器里落定)。
public record FStringPart(string Text, Expression? Interp) : AstNode;

public record IdentifierExpr(string Name) : Expression;
public record CallExpr(Expression Function, Expression Argument) : Expression
{
    public bool BareUnitArg { get; init; }
}
public record MemberAccess(Expression Object, string Member) : Expression;

// `xs[i]` —— 读:`xs.At i`;写:`xs[i] = v`(见 Lowering);切片:`xs[a..b]`
public record IndexExpr(Expression Object, Expression Index) : Expression;
public record BinaryExpr(Expression Left, string Op, Expression Right) : Expression;

// `a < b < c` —— 链式比较:中间的每个操作数都**只求一次**,比较按 `&&` 串起来
public record ChainCompare(List<Expression> Operands, List<string> Ops) : Expression;

// `a if c else b` —— 词形三元。没有 `? :` 那种写法,因为 `:` 早就是类型判定了。
// 糖本身留着不改树,脱糖见 `Lowering.Conditional`:与手写的 `c { a } { b }` 同一个形状
// (一个 Bool 带两个**裸块**),所以分支里 `:=` 落在外层作用域 —— 和手写块一模一样。
public record ConditionalExpr(Expression Condition, Expression WhenTrue, Expression WhenFalse) : Expression;
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

// `s[]` / `s{}` / `s{->}`:紧贴的空容器 = "转成那种容器"(去糖成 `s.ToList ()` 之类,见 Lowering)。
// 判据在 Parser.Postfix 的 ParseAdjacentCallAndConvert,这里只记"转成什么"。
public enum ConvertKind
{
    List,   // s[]
    Set,    // s{}
    Dict,   // s{->}
}

public record ContainerConvert(Expression Object, ConvertKind Kind) : Expression;

// 推导式 —— `[x for i in xs if p]` 这类。语法上是**表达式后缀**(见 Parser.Comprehensions),
// 所以糖本身只把"容器 + 值 + 子句"记下来,拼 `|> .Where/.Map/.Bind` 是 Lowering 的活。
public enum CompKind
{
    List,       // [ … ]
    Set,        // { … }
    Dict,       // { k -> v … }
    Generator,  // 裸形式 / ( … ):惰性,不落容器
}

public abstract record CompClause : AstNode;

// 循环变量位就是**参数位**:名字、解构、`: 类型`、`|> 守卫`、字面量全收(见 Lowering.Lam)。
public record CompFor(Pattern Pattern, Expression Source) : CompClause;

// `if` 只对**紧挨着它前面那个 `for`** 生效(见 Lowering.CompSeq 的注释)
public record CompIf(Expression Cond) : CompClause;

public record Comprehension(
    Expression Value,
    Expression? DictValue,
    List<CompClause> Clauses,
    CompKind Kind) : Expression;

public record SlotExpr(Expression Path) : Expression;

public record LiteralExpr(RuntimeValue Value) : Expression;
public record HoleExpr(int Index) : Expression;
public record BlockExpr(List<Statement> Statements) : Expression
{
    public string? Source { get; init; }

    // 「这一块是柯里化的中间层」= **最后一句**是交给下一层的东西(lambda)。
    // 前面可以挂着注解检查(`(x: int y) => …` 的 `x` 那条跑完再把 `y` 那支交出去),
    // 所以不再要求"只有一句"。
    public bool Curried => Statements is [.., ExpressionStatement { Expr: LambdaExpr }];
}

// --- 辅助 ---
public record Parameter(string Name, Expression Type, Pattern? Pattern = null)
{
    public List<string>? Attrs { get; init; }

    // `#ignoreTypes true` 之下：注解只当摆设，体里不为它发检查
    public bool IgnoreType { get; init; }
}

// --- 程序根 ---
public record Program(List<Statement> Statements) : AstNode
{
    public string? Source { get; init; }
}
