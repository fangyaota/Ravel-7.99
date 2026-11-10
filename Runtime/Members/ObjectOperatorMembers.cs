namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`Object` 这个类给它的实例准备什么** —— 运算符那一份(通用方法在
/// `ObjectMembers` 里,`AttachMembers` 里 `Object.Members` 接的是**这一份**,它的父
/// 就是 `ObjectMembers`,所以通用方法跟着来,不必再 `Combine` 一遍)。
///
/// 这一份放的是**每个值都够得着**的那几个:`:`(类型判定)、`==` / `!=`(按值或按身份),
/// 以及类型之间的 `&lt;:` / `:>`。它们挂在 `Object` 上是因为运算符查的是 `MemberScope`
/// (自己那层 + 沿 `parent` 往上),而任何值往上都到 `Object`。
///
/// **`: ` 不是 `OperatorSymbols.All` 里的符号** —— 它是标点,`a <> b` 那种用户写法根本
/// 不存在,所以这条路用 `[ClassMethod(":")]`(和从前的 `DefineMethod` 同一个落点),
/// 不是 `[ClassOp]`(`MemberSet.Scan` 装的时候就会把不认的符号打回去)。
///
/// 声明顺序就是 `Fields ()` 报的顺序(`MemberSet.Scan` 按声明收名字),所以这里
/// **保持原来注册的顺序**:`:` → `==` → `!=` → `&lt;:` → `:>`。</summary>
internal static class ObjectOperatorMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(ObjectOperatorMembers), ObjectMembers.Set);

    /// <summary>类型判定 `x: T`。**消息里那个名字就是这一条** —— `BindOperator` 在求值器
    /// 那条特判**之前**就查表了,表里没有就当场报「运算符 ':' 不支持 X 操作数」;
    /// 所以这一格必须存在,哪怕真正算的是 `Interpreter.Binary` 那一支(它还要看接口实现
    /// `HasTrait`)。
    ///
    /// **不给自定义**:`OperatorSymbols.All` 里没有它,`ParseOperatorDefinition` 也拦了一道。
    ///
    /// 判据是类型树上的 `IsAssignableTo`(帮手 `BuiltinClasses.IsA` 还在,那条特判用的就是它):
    ///   `1: int`     Integer &lt;: Integer          ✓
    ///   `1: real`   Integer 与 Real 是兄弟       ✗
    ///   `1: object`  Integer &lt;: IValue &lt;: Object ✓
    ///   `int: type`  类对象是 type 的实例           ✓
    ///   `default: int`  Every(底类型)特判           ✓
    /// 右边必须是个类型对象,否则报 Ravel 错误(不是 `InvalidCastException`)。
    ///
    /// **从前这里挂着 `is` / `isnot` 两个成员**(那时它们是词形运算符,于是 `1.is` /
    /// `is.int` 两种节形式和 `a.+` 完全对称)。`:` 是标点,取成员那条路就没了 ——
    /// 要当函数用写节:`:.int`。</summary>
    [ClassMethod(":")]
    private static RuntimeValue TypeTest(RuntimeValue self, RuntimeValue type)
        => new BoolVal(BuiltinClasses.IsA(self, type, ":"));

    /// <summary>`a == b`:对象按**身份**比(`SameValue`)—— 类对象也是对象,所以"类型之间"
    /// 和"实例之间"用的**同一个实现**,只是各自的表不同。钩在 `Object` 上而不是 `Type` 上:
    /// 挂在 `Type` 上只有**类对象**够得着(它从元类起往上走会经过 `Type`),普通实例、属性、
    /// `()` 这些就都摸不到了。
    ///
    /// `SameValue` 里**两边都不能硬转 `ObjectVal`** —— 这条是沿 `parent` 往上兜底找来的,
    /// 于是 `()`(Void)、`default`(Every)、异常值这些**原子值**也走得到它:硬转就抛 C# 的
    /// `InvalidCastException`,漏到顶层把程序打掉(`print (() == ())` 从前就是这么死的)。</summary>
    [ClassOp("==")]
    private static RuntimeValue Equal(RuntimeValue a, RuntimeValue b)
        => new BoolVal(BuiltinClasses.SameValue(a, b, "=="));

    /// <summary>`a != b` —— 同一个 `SameValue`,取反。</summary>
    [ClassOp("!=")]
    private static RuntimeValue NotEqual(RuntimeValue a, RuntimeValue b)
        => new BoolVal(!BuiltinClasses.SameValue(a, b, "!="));

    /// <summary>类型**之间**:`A &lt;: B`(A 是不是 B 的子类型)。
    /// 和 `is` 分工清楚:`is` 收**值**("这个值是不是这个类型"),这一对两边都必须是
    /// **类型对象**。也挂在 `Object` 上,是为了报错能说人话(`1 &lt;: int` 得到的是
    /// 「左边得是个类型」,而不是「类型 Integer 不支持运算符」)。接口那一半("这个类在当前
    /// 作用域里算不算那个接口")要问作用域,在求值器里补 —— 这里只给名义答案。</summary>
    [ClassOp("<:")]
    private static RuntimeValue SubtypeOf(RuntimeValue a, RuntimeValue b)
    {
        var (x, y) = BuiltinClasses.AsTypes(a, b, "<:");
        return new BoolVal(x.IsAssignableTo(y));
    }

    /// <summary>`A :> B`(A 是不是 B 的父类型)—— 同一个判据,方向反过来。
    /// 两边同样都得是类型对象。</summary>
    [ClassOp(":>")]
    private static RuntimeValue SupertypeOf(RuntimeValue a, RuntimeValue b)
    {
        var (x, y) = BuiltinClasses.AsTypes(a, b, ":>");
        return new BoolVal(y.IsAssignableTo(x));
    }
}
