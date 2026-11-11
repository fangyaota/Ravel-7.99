namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class FunctionMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(FunctionMembers), ObjectMembers.Set);

    private static FunctionVal AsFunction(RuntimeValue s, string what)
        => s as FunctionVal ?? throw new RuntimeException($"{what} 只对函数有意义，{s.Type} 不行", ErrorKind.Type);

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

    [ClassMethod("Scope")]
    private static RuntimeValue Scope(RuntimeValue self)
        => new ScopeVal(AsFunction(self, "Scope").CaptureScope ?? new Scope());

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
        if (arg is not FunctionVal p) throw new RuntimeException("prepend 需要一个函数参数", ErrorKind.Argument);
        return AsFunction(self, "Prepend").Prepend(p);
    }

    [ClassMethod("Append")]
    private static RuntimeValue Append(RuntimeValue self, RuntimeValue arg)
    {
        if (arg is not FunctionVal p) throw new RuntimeException("append 需要一个函数参数", ErrorKind.Argument);
        return AsFunction(self, "Append").Append(p);
    }
}
