namespace Ravel.Runtime;

/// <summary>引擎报的错**属于哪一族** —— `ErrorKind` → 内置类的那张表,外加"造一个出来"。
///
/// 报错的是引擎(<see cref="RuntimeException"/> / <see cref="TypeMismatchException"/>),
/// 分类也该引擎说了算 —— 这 11 个类因此在 **C# 侧**建(`BuiltinClasses` 静态构造器那两趟),
/// 而不是在 `lib/exceptions.rav` 里:库里再定义一遍就成两处各管一半(引擎认不得库里的名字,
/// 库又得猜引擎什么时候报什么)。
///
/// 用户看到的永远是 **Ravel 的对象**:能 `e.Message`、能按类型接、`typeof e` 是那个类。
/// 引擎冒泡到错误钩子那儿时把消息和族一起交给 <see cref="NewException"/>。
///
/// | 类别 | 什么时候报 |
/// |---|---|
/// | `TypeError` | 类型不对:运算符不吃这个操作数、赋值类型不符、转换不了、构造不了 |
/// | `NameError` | 名字找不到:未定义的变量 |
/// | `AttributeError` | 成员找不到:对象没有这个字段 / 类型没有这个方法 |
/// | `IndexError` | 下标越界 |
/// | `KeyError` | 键不存在 |
/// | `ZeroDivisionError` | 除零 |
/// | `AssertionError` | `assert` 不成立 |
/// | `ArgumentError` | 实参的形状不对(要代码块、要字符串、要类型对象…) |
/// | `ValueError` | 值本身不对:数值超范围、解析不动、JSON 转不了 |
/// | `IoError` | 文件/目录/命令那批(`Fs` 那一族) |
/// | `RegexError` | 正则:模式写错、匹配超时 |
/// | `Error` | 还没归类的 —— 落基类 `Exception`(分类可以慢慢补,不必一次到位) |
///
/// 题外:它们也进 `AllTypes`(`Subtypes ()` 看得见),层级就是
/// `Exception → TypeError / NameError / …` —— 所以 `try { … } (e: Exception) => …`
/// 照旧接得住全部,`(e: TypeError) => …` 只接那一族。</summary>
internal static partial class BuiltinClasses
{
    /// <summary>这一族对应哪个类(没归类的落基类 `Exception`)。</summary>
    internal static ClassVal ErrorClass(ErrorKind kind) => kind switch
    {
        ErrorKind.Type => TypeError,
        ErrorKind.Name => NameError,
        ErrorKind.Attribute => AttributeError,
        ErrorKind.Index => IndexError,
        ErrorKind.Key => KeyError,
        ErrorKind.ZeroDivision => ZeroDivisionError,
        ErrorKind.Assert => AssertionError,
        ErrorKind.Argument => ArgumentError,
        ErrorKind.Value => ValueError,
        ErrorKind.Io => IoError,
        ErrorKind.Regex => RegexError,
        _ => Exception,
    };

    /// <summary>引擎自己造一个异常实例(Ravel 侧报错要交给错误钩子时用)。
    /// 不走构造器 —— 那样要推帧;这里直接建对象、把 `Message` 放进去,和 `StepClassInit` 一个形状。
    ///
    /// 类体那半:`ClassBody` 为空的内置类(理论上没有,错误这一族都装了)给一个孤立 scope,
    /// 照样建得出来。</summary>
    internal static ObjectVal NewException(string message, ErrorKind kind = ErrorKind.Error)
    {
        var type = ErrorClass(kind);
        var scope = new Scope(type.ClassBody?.CaptureScope);
        var obj = new ObjectVal(type, scope);
        scope.Define(ObjectVal.ThisMember, type, obj);
        scope.Define(MessageMember, String, new StringVal(message));
        return obj;
    }
}
