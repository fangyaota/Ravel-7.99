namespace Ravel;

using Ravel.Runtime;

/// <summary>可自定义的运算符符号。类体里直接用符号定义(`+ := f` 定义、`+ = f` 覆盖),
/// 符号本身就是实例里的成员名,所以 `a.+` 能取到它。集合与 BuiltinClasses.Operators 注册的内置运算符一致。
///
/// `is` / `isnot` 是**词形运算符**(类型判定):它们不是标点,所以解析器在
/// **运算符位置**按词判定(见 `Parser.IsWordOperator`)—— 别处照样能当普通标识符用,
/// 于是 `1.is`(等右操作数)和 `is.int`(等左操作数)两种节形式和 `a.+` / `+.2` 完全对称。
///
/// `<:` / `:>` 是**类型之间**的关系(A 是不是 B 的子类型 / 父类型),两边都得是类型对象 ——
/// 和 `is`("值是不是这个类型")分清楚,那一对才收值。</summary>
public static class OperatorSymbols
{
    public static readonly HashSet<string> All =
        ["+", "-", "*", "/", "%", "==", "!=", "<", ">", "<=", ">=", "&", "|", "^", "is", "isnot", "<:", ":>"];

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
    /// <summary>运算符:靠名字识别——类体里直接写符号(`+ := f` / `+ = f`)</summary>
    public bool IsOperator => OperatorSymbols.IsSymbol(Name);

    /// <summary>**引擎自己造的类体**(`PresetCtor`,见 `BuiltinClasses`)里那条定义。
    /// 它和用户写的 `:=` 不同:预设的那一串是"每一层覆盖上一层" ——
    /// `Object` 的默认构造、`Type` 的造类、`Interface` 的造接口、`BaseInterface` 的造实现,
    /// 一个盖一个,所以它们走**覆盖**(`DefineOrReplace`)而不是"同名即错"。</summary>
    public bool Preset { get; init; }
}
/// <summary>赋值 `x = v`</summary>
public record Assignment(string Name, Expression Value) : Statement;

/// <summary>`by a = X` / `by a.x = X` —— **换掉槽里的那份 property**:不走旧 setter,
/// 也不查类型约束(`by a: int = …` 那个约束管的是"写进属性的值",而这里换的是属性本身)。
///
/// `Define` 是 `by a.x := X` 那种(`:=` 定义):**在那个对象上把槽建出来**(成员还不存在时用)。
/// 和语言里别处一个规矩:`:=` 定义、`=` 赋值 —— 只是这儿定义的是"一个属性槽"。
/// 不带 `Define` 时目标必须已经是 by 属性,不然 `by obj.nope = …` 会悄悄多出个成员。
///
/// `Path` 和 <see cref="SlotExpr"/> 一模一样:变量名,或 `对象.成员`。</summary>
public record SlotAssign(Expression Path, Expression Value, bool Define = false) : Statement;
public record ExpressionStatement(Expression Expr) : Statement;

/// <summary>`名字 =&lt; 表达式`:do 块里的"从这个 Monad 里取值"。
///
/// **只在 `do { … }` 里认**(解析器按 do 的深度放行),而且它不活到求值期 ——
/// `ParseDo` 把整个块折成一串 `Bind` 之后就没它的事了。单独执行它没有语义:
/// 值取出来给谁、后面那些语句跑不跑,全看它在链上的位置。
/// (所以 `AstPrinter` 里没有它的分支:打印函数时看到的是**脱糖后**的 Bind 链。)</summary>
public record BindStatement(string Name, Expression Monad) : Statement;

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
/// <summary>字典的一个条目。**键也是表达式**(从前的"标识符即字符串"那条糖已经去掉):
/// `{"a": 1}` / `{1: "x"}` / `{k: v}` —— 键求出来得是**值类型**(数 / 字符串),
/// 不然当场报错(见 `Interpreter.StepDict`)。</summary>
public record DictEntry(Expression Key, Expression Value);
public record DictLiteral(List<DictEntry> Entries) : Expression;
public record VoidLiteral : Expression;

/// <summary>`by a` / `by a.x` —— **取槽里的那份 property 本身**(不过 getter)。
///
/// 和 `by a = X`(换槽,见 <see cref="Assignment.By"/>)对称的那一半:写那边不过 setter,
/// 这边不过 getter。拿到手的是那个 `property` 值,可以当普通值传出去、也可以
/// `p.Get ()` / `p.Set v` 自己调。
///
/// `Path` 要么是个名字(变量槽),要么是 `对象.成员`(那边得先求出对象)。</summary>
public record SlotExpr(Expression Path) : Expression;

/// <summary>直接求值成一个 C# 侧造好的值。给内置类的**预设类体**用 —— 那些成员
/// (类型转换器、`type` 的默认建类函数)是 C# 函数,写不出 Ravel 源码来。
/// 造类体走 BuiltinClasses.PresetCtor。</summary>
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
