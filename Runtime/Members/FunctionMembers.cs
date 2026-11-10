namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`Function` 这个类给它的实例准备什么。**
///
/// `function` 是**所有能调的东西**的那一层:`lambda` 是它,`bool` / `block` /
/// `continuation` 也是它(Ravel 类树里 `Bool : Function`),`type` 更是 `Type : Function`
/// —— 所以 `TypeMembers` 的父也是这一份,而不是 `Object`(见 `TypeMembers`)。
///
/// 由此有三个地方要当心,代码里那两处 `AsFunction` 就是为此:
/// - 类对象**沿链**查到这一层(`type <: function`),所以 `Fields ()` 会把这几条
///   列成类的可用方法;真调起来接收者是 `ObjectVal` 不是 `FunctionVal` —— 硬转会抛
///   `InvalidCastException`。`AsFunction` 负责让它在 Ravel 里报成一句人话。
/// - `body` / `scope` 的**类型是恒定**的(`Block` / `Scope`),没有的给空块/空作用域,
///   不拿 `()` 顶替 —— 于是 `(f.body) 1` 这种写法永远成立,不必先判一下。
/// - `Name` 落在 `ObjectVal.Name`(成员表)上,函数和类对象都有,所以那一条**不转**。
///
/// 从前这一批在 `RegisterFunctionMethods` 里。</summary>
internal static class FunctionMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(FunctionMembers), ObjectMembers.Set);

    /// <summary>取接收者当函数用。**不能硬转 `(FunctionVal)s`**:类对象往上查到 `Function` 那层
    /// (type <: function),所以 `Fields ()` 会把 Function 的方法列成类的可用方法,
    /// 而类对象是 `ObjectVal` —— 硬转就抛 InvalidCastException 漏到顶层。
    /// 换句话说,这是 BoolVal 那个「类型说有、值却接不住」的**反向**同款。</summary>
    private static FunctionVal AsFunction(RuntimeValue s, string what)
        => s as FunctionVal ?? throw new RuntimeException($"{what} 只对函数有意义，{s.Type} 不行", ErrorKind.Type);

    /// <summary>Name 落在成员表里(ObjectVal.Name),函数和类对象都有,不用转。
    /// 给字符串就是**改名**,不给就是**读名**(两种读法同一个名字,和 C# 的属性一样)。</summary>
    [ClassMethod("Name")]
    private static RuntimeValue Name(RuntimeValue self, RuntimeValue arg)
    {
        var f = (ObjectVal)self;
        if (arg is StringVal sv)
        {
            f.Name = sv.Value;
            return VoidVal.Instance;
        }

        return new StringVal(f.Name ?? "");
    }

    /// <summary>捕获作用域是**函数独有**的:对象没有捕获作用域,它那半边叫 MemberScope。
    /// 没有捕获作用域的值(类对象不是闭包)给一个**空 Scope** —— **类型恒定是 Scope**,
    /// 不拿 `()` 顶替,也不报错:`x.scope ()` 拿到的东西该始终能当作用域用。</summary>
    [ClassMethod("Scope")]
    private static RuntimeValue Scope(RuntimeValue self)
        => new ScopeVal(AsFunction(self, "Scope").CaptureScope ?? new Scope());

    /// <summary>body:这个函数/类的**体**。同样类型恒定是 Block —— 没有体的(内置方法、
    /// 原生闭包、没类体的内建类)给一个**空块**,而不是 `()`:`f.body` 始终能当块用
    /// (`(f.body) 1` 就是跑一遍它)。</summary>
    [ClassMethod("Body")]
    private static RuntimeValue Body(RuntimeValue self) => self switch
    {
        LambdaVal lam => new BlockVal(lam.Block, lam.CaptureScope),
        ClassVal c => c.ClassBody ?? BuiltinClasses.EmptyBody,
        BlockVal b => b,
        _ => BuiltinClasses.EmptyBody,
    };

    // 换作用域 / prepend / append 只有真函数能做(类对象的作用域是它的实例作用域,不能换)

    [ClassMethod("SetScope")]
    private static RuntimeValue SetScope(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not ScopeVal sv) throw new RuntimeException("setScope 需要 Scope 参数", ErrorKind.Argument);
        AsFunction(self, "SetScope").CaptureScope = sv.Inner;
        return VoidVal.Instance;
    }

    [ClassMethod("Prepend")]
    private static RuntimeValue Prepend(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not BlockVal p) throw new RuntimeException("prepend 需要代码块参数", ErrorKind.Argument);
        return AsFunction(self, "Prepend").Prepend(p);
    }

    [ClassMethod("Append")]
    private static RuntimeValue Append(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not BlockVal p) throw new RuntimeException("append 需要代码块参数", ErrorKind.Argument);
        return AsFunction(self, "Append").Append(p);
    }
}
