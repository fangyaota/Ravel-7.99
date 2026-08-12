namespace Ravel;

public enum TokenType
{
    // 关键字 — Ravel 没有关键字，while/if 等是内置函数

    // 字面量
    Number,
    String,

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

    // 比较
    EqualEqual,    // ==
    NotEqual,      // !=
    Less,          // <
    Greater,       // >
    LessEqual,     // <=
    GreaterEqual,  // >=

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
    Star,          // *
    Slash,         // /
    Percent,       // %

    // 复合赋值
    PlusEqual,     // +=
    MinusEqual,    // -=
    StarEqual,     // *=
    SlashEqual,    // /=
    PercentEqual,  // %=

    Comma,         // ,  (暂时保留，报错用)
    Dot,           // .

    // 特殊
    Newline,
    EndOfFile,
}
