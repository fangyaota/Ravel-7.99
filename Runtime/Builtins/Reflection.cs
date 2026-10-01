namespace Ravel.Runtime;

/// <summary>`System` 的反射与作用域那几条。</summary>
public partial class Interpreter
{
    [Sys("TypeOf")]
    private static RuntimeValue TypeOf(RuntimeValue a) => a.Type;

    /// <summary>接口实现:`use` 登记进**当前作用域**(随作用域在/不在),`impl` 登记进**全局作用域**
    /// (每个作用域链都到全局,于是处处生效)。两个共用一套登记与查找,
    /// 见 `BuiltinClasses.Interfaces.cs`。</summary>
    [Sys("Use")]
    private RuntimeValue UseTrait(RuntimeValue a) => BuiltinClasses.Use(this, a, CurrentScope, "use");

    [Sys("Impl")]
    private RuntimeValue ImplTrait(RuntimeValue a) => BuiltinClasses.Use(this, a, _global, "impl");

    [Sys("CurrentScope")]
    private RuntimeValue CurrentScopeValue(RuntimeValue _) => new ScopeVal(CurrentScope);

    /// <summary>标记**当前**作用域:core 检查沿作用域链往上找标记,所以函数返回后标记自然失效</summary>
    [Sys("Unsafe")]
    private RuntimeValue MarkUnsafe(RuntimeValue _)
    {
        UnsafeScopes.Add(CurrentScope);
        return VoidVal.Instance;
    }
}
