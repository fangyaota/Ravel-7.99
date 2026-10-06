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

    /// <summary>`seal 某个类` —— **密封**它:此后不能再被继承;密封**接口**则是不能再被实现。
    ///
    /// **交回那个类自己**(可串:`C := seal (class { … })`)。幂等 —— 再 `seal` 一次不报错。
    ///
    /// 挡住的是两件事(`BuiltinClasses.SealedUp` 那条判据,两处都问它):
    ///
    /// - **建类 / 建接口**(`class 那个密封的类 { … }`)—— 落在 `Install` 那一句上,
    ///   那儿是**唯一**的建类入口;
    /// - **造实现**(`impl (那个密封的接口 某个类 { … })`)—— 落在 `StepImplMake` 上。
    ///
    /// 判据是"自己**或任一祖先**密封了",不是只看直接那一层:密封说的是**这条继承链到此为止**
    /// (`C <: B <: A`,A 密封之后 C 一样进不来);接口同理 —— `IEnumerable <: IMonad`,
    /// `IMonad` 密封之后实现 `IEnumerable` 照样是在实现它。
    ///
    /// **密封的是"能不能再往下长",不是"能不能用"**:密封的类照常实例化、照常当类型注解;
    /// 密封的接口照常 `x : I`、照常出现在 `Fields ()` / 类型树里。</summary>
    [Sys("Seal")]
    public static RuntimeValue Seal(RuntimeValue a)
        => a is ObjectVal o
            ? SetSealed(o)
            : throw new RuntimeException($"seal 要一个类（或接口），得到 {a.Type}", ErrorKind.Type);

    private static RuntimeValue SetSealed(ObjectVal o)
    {
        o.IsSealed = true;
        return o;
    }
}
