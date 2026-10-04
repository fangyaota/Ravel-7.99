namespace Ravel;

public enum TokenType
{
    // 关键字 — Ravel 没有关键字，while/if 等是内置函数

    // 字面量
    Number,
    String,
    /// <summary>`'a'` —— 一个**字符**(UTF-16 码元,和 `string.Length` 一个口径)。
    /// 转义和字符串那套一样,外加 `\'`。</summary>
    Char,

    // 标识符
    Identifier,

    // 括号
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    LeftBrace,
    RightBrace,

    // 运算符 / 标点
    Colon,         // :
    ColonEqual,    // :=
    ColonColon,    // ::
    ColonColonEqual, // ::=
    Arrow,         // =>
    Equal,         // =
    PipeLeft,      // <|
    PipeInto,      // |> 把左边交给右边:右边是 `.成员` 就挂上去,否则 `a |> f` ≡ `f a`
    BindArrow,     // :<  do 块里的取值绑定(`x :< m`;只在 do 里认)

    // 比较
    EqualEqual,    // ==
    NotEqual,      // !=
    Less,          // <
    Greater,       // >
    LessEqual,     // <=
    GreaterEqual,  // >=
    Subtype,       // <:  A 是不是 B 的子类型(两边都是**类型**)
    Supertype,     // :>  A 是不是 B 的父类型(同上,方向反过来)

    // 逻辑
    AndAnd,        // &&
    OrOr,          // ||
    Pipe,          // |
    And,           // &
    Caret,         // ^
    Bang,          // !

    // 算术
    Plus,          // +
    Minus,         // -
    PlusPlus,      // ++  自增**语句**的糖,不是运算符(见 Parser.Statements 的 ParseIncDec)
    MinusMinus,    // --  自减同上
    Star,          // *
    Slash,         // /
    Percent,       // %
    StarStar,      // **   乘方(右结合、比一元还紧 —— 见 Parser 里的 ParsePower)

    // 移位与循环移位(见 BuiltinClasses.Operators 里那段说明)
    ShiftLeft,     // <<   左移(位型:高位丢出去就没了)
    ShiftRight,    // >>   算术右移(保符号)
    RotateLeft,    // <<<  循环左移(32 位转圈)
    RotateRight,   // >>>  循环右移

    // 复合赋值
    PlusEqual,     // +=
    MinusEqual,    // -=
    StarEqual,     // *=
    StarStarEqual, // **=
    SlashEqual,    // /=
    PercentEqual,  // %=

    Comma,         // ,  (暂时保留，报错用)
    Dot,           // .
    DotDot,        // .. —— 区间 `[1..3]` / `(3..5)` 的分隔符
    QuestionDot,   // ?. —— 空值穿透的成员访问(`a?.b c`:空就短路)

    // 空值合并(语法糖,见 Parser.Expressions 那三条)
    Coalesce,      // ??  a 空就用 b
    CoalesceEqual, // ??= 空才写

    // 特殊
    Newline,
    EndOfFile,
}
