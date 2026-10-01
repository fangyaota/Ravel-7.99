namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

/// <summary>`System` 的反射与作用域那几条。</summary>
internal static class SysReflection
{
    [Sys("TypeOf")]
    public static RuntimeValue TypeOf(RuntimeValue a) => a.Type;

    /// <summary>接口实现:`use` 登记进**当前作用域**(随作用域在/不在),`impl` 登记进**全局作用域**
    /// (每个作用域链都到全局,于是处处生效)。两个共用一套登记与查找,
    /// 见 `BuiltinClasses.Interfaces.cs`。</summary>
    [Sys("Use")]
    public static RuntimeValue UseTrait(Interpreter self, RuntimeValue a)
        => BuiltinClasses.Use(self, a, self.CurrentScope, "use");

    [Sys("Impl")]
    public static RuntimeValue ImplTrait(Interpreter self, RuntimeValue a)
        => BuiltinClasses.Use(self, a, self.GlobalScope, "impl");

    [Sys("CurrentScope")]
    public static RuntimeValue CurrentScopeValue(Interpreter self, RuntimeValue _) => new ScopeVal(self.CurrentScope);

    /// <summary>标记**当前**作用域:core 检查沿作用域链往上找标记,所以函数返回后标记自然失效</summary>
    [Sys("Unsafe")]
    public static RuntimeValue MarkUnsafe(Interpreter self, RuntimeValue _)
    {
        self.UnsafeScopes.Add(self.CurrentScope);
        return VoidVal.Instance;
    }
}
