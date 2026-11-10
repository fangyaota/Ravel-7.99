namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

internal static class SysReflection
{
    [Sys("TypeOf")]
    public static RuntimeValue TypeOf(RuntimeValue a) => a.Type;

    [Sys("Use")]
    public static RuntimeValue UseTrait(Interpreter self, RuntimeValue a)
        => BuiltinClasses.Use(self, a, self.CurrentScope, "use");

    [Sys("Impl")]
    public static RuntimeValue ImplTrait(Interpreter self, RuntimeValue a)
        => BuiltinClasses.Use(self, a, self.GlobalScope, "impl");

    [Sys("CurrentScope")]
    public static RuntimeValue CurrentScopeValue(Interpreter self, RuntimeValue _) => new ScopeVal(self.CurrentScope);

    [Sys("Unsafe")]
    public static RuntimeValue MarkUnsafe(Interpreter self, RuntimeValue _)
    {
        self.UnsafeScopes.Add(self.CurrentScope);
        return VoidVal.Instance;
    }

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
