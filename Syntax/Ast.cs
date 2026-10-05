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
        ["+", "-", "*", "/", "%", "**", "==", "!=", "<", ">", "<=", ">=", "&", "|", "^",
         "<<", ">>", "<<<", ">>>", "is", "isnot", "<:", ":>"];

    public static bool IsSymbol(string name) => All.Contains(name);

    /// <summary>**赋值那一类**(`a.b = v` / `a.b := v` / `+=` / …)。
    ///
    /// 它们在语法上就是 `BinaryExpr`(见 `Parser.Expressions.ParseAssignment`),所以
    /// **认的形状和别的二元运算一模一样**;区别在求值:`WriteVariable` 那条把赋进去的值
    /// 交回来(`x := 0` 给 `()`、`x = 0` 给 `0`、`by` 属性给新值 —— 三个答案已经统一成一个)。
    ///
    /// 表摆在这儿是要**只有一个出处**:`Interpreter.WarnIfForgotCall` 判"这一句的值被丢掉了"
    /// 时要跳过它们 —— 赋值的事已经做了,丢的只是回显,不是"忘了调用"。
    /// (改 `ParseAssignment` 里那串 `Match` 的话,这儿也得跟着改。)</summary>
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

/// <summary>`名字 :&lt; 表达式`:do 块里的"从这个 Monad 里取值"。
///
/// **只在 `do { … }` 里认**(解析器按 do 的深度放行),而且它不活到求值期 ——
/// `Lowering` 把整个块折成一串 `Bind` 之后就没它的事了。单独执行它没有语义:
/// 值取出来给谁、后面那些语句跑不跑,全看它在链上的位置。
/// (所以 `AstPrinter` 里没有它的分支:打印函数时看到的是**脱糖后**的 Bind 链。)</summary>
public record BindStatement(string Name, Expression Monad) : Statement;

/// <summary>`do { … }` —— 一串"从 Monad 里取值"的语句(`名字 :&lt; 表达式`)。
///
/// **它活不过 `Lowering`**:那一趟把整个块折成 `Bind` 链
/// (`m1.Bind ((x: object) => { m2.Bind (…) })`),所以求值器、`AstPrinter` 都见不到它
/// —— 和 <see cref="BindStatement"/> 一个性质,只是那个要更早一点(它在同一趟里被吃掉)。
///
/// 为什么不照老样子让**解析器当场折**:那一段(从最后一条往前,一条条包 lambda)
/// 造的是**意思**,不是语法 —— 和 `or` / `and` 是同一个病。解析器只管把这个块读出来,
/// 折法归 `Syntax/Lowering.cs`。</summary>
public record DoExpr(List<Statement> Statements) : Expression;

/// <summary>一条**解构定义**:`[x _ z ..rest] : T = e` / `{x y z} : U = e`。
///
/// **它活不过 `Lowering`** —— 那一趟按 <see cref="Pattern"/> 拆成一串普通的 `:=`
/// (见 `Lowering.Destructure`)。所以求值器和 `AstPrinter` 都见不到它
/// (和 <see cref="DoExpr"/> 一个待遇)。
///
/// `Attrs` 是前面那圈修饰符(`private` / `readonly` …),**每个拆出来的绑定都带一份**
/// —— `private [x y] : T = e` 拆出来的是两个私有成员。
/// 类型写在**模式自己身上**(见 <see cref="Pattern.Type"/>):`[x y] : T = e` 里那个 `:` 挂的
/// 就是最外那格 —— 说的还是"右边那个值"。</summary>
public record Destructure(List<string> Attrs, Pattern Pattern, Expression Value) : Statement
{
    /// <summary>有没有写修饰符(拆的中间量要不要跟着带一份)。</summary>
    public bool HasAttrs => Attrs.Count > 0;
}

/// <summary>解构模式的**一格**。纯语法,没有求值语义 —— 活在 `Destructure` 和
/// 带模式的参数(<see cref="Parameter.Pattern"/>)里。
///
/// `Type` 是**这一格自己**的类型要求(可省):`[x: int y: string]` / `{n: int}`,
/// 连嵌套的那几格也能写(`[[a b]: list c]`)。它和 `x : int = v` 是**同一个判据** ——
/// 绑的时候当场验,对不上就是 `TypeError`,所以 `|` 的交替接得住(和形状检查一条路)。
///
/// **一整条一个注解是它顶层的特例**:`[x y] : list = e` 里那个 `:` 挂在最外那格模式上,
/// 说的还是"右边那个值得是 list"。不再有"另存一处"的说法。</summary>
public abstract record Pattern : AstNode
{
    public Expression? Type { get; init; }

    /// <summary>这一格自己的**条件**(可省):`[u == 1 v]` / `a == 1 = e`。
    ///
    /// 和参数表里那个守卫**同一套**:表达式里**最左边那个标识符**就是这一格的名字,
    /// 值绑给它之后才验这条 —— `u == 1` 说的是"绑出来的 `u` 要等于 1"。不成 → **拒收**,
    /// 和形状检查一条路(`|` 的交替接得住)。
    ///
    /// 名字本身也认(`a |> IsPrime`),只要那个标识符是**最左**的就行(`1 == a` 取到的是 `a`)。</summary>
    public Expression? Guard { get; init; }

    /// <summary>这一格**整体**的条件:`[u == 1 v] |> LongerThanThree` —— `|>` 后面那个函数
    /// 拿**整块**当实参喂进去,不成立就拒收。
    ///
    /// 和 <see cref="Guard"/> 的分别只在于**东西怎么到手**:
    /// - `Guard` 是"**用名字写**"的(`u == 1`),名字取表达式里最左那个标识符;
    /// - `When` 是"**把整块喂出去**"的(`|> LongerThanThree`)—— 复合的那几格
    ///   (列表/对象)自己没绑过名字,而"整块"在调用点现成有,不必另起一个。
    ///
    /// `|>` 贴的是**刚结束的那一格**:`[u |> IsPrime]` 里喂的是 `u` 那个元素,
    /// `[u == 1 v] |> LongerThanThree` 里喂的是整个列表。</summary>
    public Expression? When { get; init; }
}

/// <summary>**字面量那一格**:`[1 v]` / `["ok" v]` / `[() x]` / `[true x]` ——
/// 要求这一格**等于**那个字面量。它不绑名字(没名字可写),所以名字式条件和整体那个 `|>`
/// 在这儿都用不上,它自己就是全部。
///
/// 降糖成**两道**,不是一道:`==` 是**按左操作数分派**的,右边跨了族它是**抛**
/// (`1 == true` → 「运算符 '==' 不支持 Bool 操作数」),而"抛"会穿掉 `|` 的交替
/// (那台机器只接拒收)。所以先用 <see cref="Domain"/> 过一道 `:` —— `:` 对不上只回
/// false,不抛 —— 过了才敢比。<see cref="Text"/> 是写出来的原文(`1` / `"a"` / `()`),
/// 给报错用。
///
/// 数字那一族的判据是 `INumber` 而不是 `int`:`1 == 1.0` 本来就为真,所以 `[1 v]` 收
/// `1.0` —— 判据得和 `==` 的**域**一样宽,不然同一个写法在模式里和在表达式里两个意思。</summary>
public record LiteralPattern(Expression Value, Expression Domain, string Text) : Pattern;

/// <summary>`{键 -> 模式}` 里的**一项**。
///
/// 键是一个**词**(名字 / 串 / 数 / `()` / `-1`)接 `.成员` 链 —— 而且它是**求值的**:
/// `k -> v` 说的是"键**等于 `k` 那个值**"的那一项,不是"叫 k 的那一项"
/// (后者是老写法 `{x y}`,那条按**成员名**走)。就差一个 `->`,所以判据也只看它。</summary>
public record DictPatEntry(Expression Key, Pattern Sub) : AstNode;

/// <summary>**字典模式**:`{"a" -> v  () -> u  k -> 112}` —— **按键**取,不按位置。
///
/// 和 <see cref="MemberPattern"/>(老写法 `{x y}`:按**成员名**、走 `Fields ()` + 成员访问)
/// 是两回事 —— 这边走 `Has` / `Get`。所以一个 `{…}` 里不许混着写(混了报一句说清的)。
///
/// 和列表那条一个次序:先说清"这一份得是 `dict`",再逐项查键在不在 —— 不在算**拒收**
/// (形状对不上),不是引擎硬错。`Get` 缺键是会抛的,所以查在**前**。</summary>
public record DictPattern(List<DictPatEntry> Entries) : Pattern;

/// <summary>`x` —— 绑这个名字(名字就取模式里写的那个,这一轮不做重命名)。</summary>
public record NamePattern(string Name) : Pattern;

/// <summary>`_` —— **跳过**这一格。不绑东西,但游标**照走**(跳过 ≠ 不取)。
///
/// 和别处的 `_`(占位符洞,`HoleExpr`)**不是一回事**:那个要交给 `Parser.Holes` 收成
/// lambda 参数,这个在解析模式时就被吃掉,一辈子到不了 `HasHoles`。</summary>
public record SkipPattern : Pattern;

/// <summary>`..rest` —— 剩下的全给它(一个 `IEnumerable`)。**只能写最后一项**
/// (它把后面都吃了,写在中间没有意义)。</summary>
public record RestPattern(string Name) : Pattern;

/// <summary>`[p0 p1 …]` —— **按位置**取:挨个 `MoveNext` + `Current`。</summary>
public record ListPattern(List<Pattern> Parts) : Pattern;

/// <summary>`{x y z}` —— 从**对象**上取同名成员(`x := obj.x`)。每一项用 `NamePattern`
/// 装(这样每一项能各自带 `: 类型`,和列表模式那边对称)。</summary>
public record MemberPattern(List<NamePattern> Names) : Pattern;

// --- 表达式 ---
public abstract record Expression : AstNode;

/// <summary>数字文字。存**原始文本**(连后缀)而不是 double:double 只有 15~17 位有效数字,
/// 大整数转一手就丢精度(12345678901234567890 → 12345678901234567000),再转回来已经不是原来那个数。
/// 原始文本还让 `AstPrinter` 把字面量原样打印回去(`2n` 打印出来还是 `2n`)。</summary>
public record NumberLiteral(string Lexeme, bool IsFloat = false) : Expression
{
    /// <summary>后缀那一个字母:`2n` / `3i` / `2.5f`。没写就是 `'\0'`。
    ///
    /// 词法那一步已经认过只可能是这三个(`Lexer.ReadNumber`),所以这儿只看末尾是不是字母。
    /// 后缀说的是**类型**,不是形状:`2n` 是大整数、`2i` 是普通整数(装不下 int 当场报错)、
    /// `2f` 是浮点。所以 **`2.0` 是 float,`2` 是 int** —— 有小数的形状说了算,不是值。</summary>
    public char Suffix => char.IsLetter(Lexeme[^1]) ? Lexeme[^1] : '\0';

    /// <summary>纯数字那一段(后缀去掉)—— 真正拿去 `Parse` 的文本</summary>
    internal string Digits => Suffix == '\0' ? Lexeme : Lexeme[..^1];

    /// <summary>解析出来的值。字面量是**纯**的(文本 → 值,不看环境),所以算一次存下来 ——
    /// 循环体里的 `1000000` 从前每轮都要 `int.Parse` / `BigInteger.Parse` 一遍。
    /// 私有字段不进 record 的相等与打印。</summary>
    internal RuntimeValue? Parsed;
}

public record StringLiteral(string Value) : Expression
{
    /// <summary>包成值的那一个 —— 和数字同理:字符串值不可变、按值比,同一个字面量
    /// 每次求值交回**同一个** `StringVal` 就行,不必每次新建。</summary>
    internal StringVal? Packed;
}
/// <summary>`'a'` —— 一个字符(UTF-16 码元)。字面量本身不区分转义写法,词法已经解码好了。</summary>
public record CharLiteral(char Value) : Expression;
public record IdentifierExpr(string Name) : Expression;
/// <summary>调用:`函数 参数`。**参数只有一个** —— 多参靠柯里化,`f a b` 解析成 `(f a) b`
/// (见 Parser.Expressions 的 juxtaposition)。从前这里存的是个 List,而它恒有一个元素:
/// 每个读点都得写 `Arguments[0]`,还得提防"要是空了/多了呢"。
/// 注解里的多参 lambda(`(a: int b: int) => …`)同样只是"体里再套一层 lambda"的糖。</summary>
public record CallExpr(Expression Function, Expression Argument) : Expression;
public record MemberAccess(Expression Object, string Member) : Expression;
public record BinaryExpr(Expression Left, string Op, Expression Right) : Expression;
public record UnaryExpr(string Op, Expression Operand) : Expression;
public record LambdaExpr(Parameter Param, BlockExpr Body) : Expression
{
    /// <summary>这枚 lambda 是**语法糖**生成的(不是用户写在那儿的)。
    ///
    /// 只为一件事:`_` 占位符消糖那趟(`Parser.Holes`)平时把 lambda 当**闭包边界**
    /// —— 里面的 `_` 归内层,不往外收。而糖生成的那些(lambda 只是"把这段表达式挪个地方",
    /// 并不引入自己的 `_` 作用域)**不是**边界:`_ ?? 1` 里那个 `_` 还是外层语句的洞,
    /// 不收的话它会一路带到求值器报「无法求值的节点类型: HoleExpr」。
    ///
    /// 它们的体**一律**是"一条表达式语句"(见 `Parser.Expressions` 那三条糖的构造),
    /// 所以消糖那趟只认那一种形状。</summary>
    public bool Sugar { get; init; }
}
public record PipeExpr(Expression Left, Expression Right) : Expression;
public record ListLiteral(List<Expression> Elements) : Expression;
/// <summary>一个**区间**:`[1..3]`(全闭)/ `(3..5)`(全开)/ `[1..5)` / `(1..5]`。
///
/// 那一对括号各带**一半的意思**:`[` `]` 含那一端、`(` `)` 不含 —— 所以四个组合都认,
/// 收尾时也**两种右括号都收**(`[1..5)` 这种混着写是合法的)。
///
/// 只在括号里认:**裸的 `a..b` 不成立**(没必要,还多一层歧义)。求值见
/// `Interpreter.StepRange`,造出来的是 `Runtime/Values/RangeVal.cs` 那个值。</summary>
public record RangeExpr(Expression Lo, Expression Hi, bool StartClosed, bool EndClosed) : Expression;
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

    /// <summary>这是个**柯里化函数**的体 —— 体就是"再交一个 lambda"那一句。</summary>
    /// <remarks>多参 lambda(`(a b c) => { … }`)在语法层就是这么消的糖(见 Parser.Atoms 的
    /// 多参数消糖),所以用户写的多参函数**一律**是柯里化的:给一个实参,它交回内层那个。
    /// 手写的 `(x) => { (y) => { … } }` 形状一样,一视同仁。
    ///
    /// 谁在用:`CallInto` 拿它给这次调用交回的那个函数打上 `FunctionVal.IsPartial`
    /// (半成品 —— 还等着下一批实参),那又喂给 `Interpreter.WarnIfForgotCall`。</remarks>
    public bool Curried => Statements is [ExpressionStatement { Expr: LambdaExpr }];
}

// --- 辅助 ---
/// <summary>参数。类型是**表达式**(常见是一个名字,也可以是括号里的表达式),
/// 求值在那个 lambda 被创建时做 —— 见 <see cref="VarDefinition.TypeAnnotation"/>。
///
/// **模式参数**(`([x y]) => …`)走 <see cref="Pattern"/>:`Name` 是个**合成名**
/// (`__p{n}`,不是用户写的),真正给用户用的名字由模式在体的开头绑出来。
/// 模式参数**不能带注解** —— 形状本身就是它对实参的要求。</summary>
public record Parameter(string Name, Expression Type, Pattern? Pattern = null);

// --- 程序根 ---
public record Program(List<Statement> Statements) : AstNode
{
    /// <summary>入口源文件路径(报错时显示用)</summary>
    public string? Source { get; init; }
}
