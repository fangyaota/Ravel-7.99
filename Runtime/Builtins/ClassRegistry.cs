namespace Ravel.Runtime;

using System.Reflection;
using System.Runtime.ExceptionServices;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ClassMethodAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ClassPropertyAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public bool ReadOnly { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ClassCtorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ClassOpAttribute(string op) : Attribute
{
    public string Op { get; } = op;

    // **这一格连遮蔽都不许**(见 `Attr.NoShadow`):`:=` 和 `=` 都不收,也不许类体新开一格盖住它。
    // 内建语义只有一种、覆盖了没有第二种解释可讲的才标:目前是 `:` / `===` / `!==` / `->`。
    public bool ReadOnly { get; set; }
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class ClassValueAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

internal static class ClassRegistry
{
    internal static FunctionVal Fn(Interpreter? self, MethodInfo m)
    {
        var ps = m.GetParameters();
        var takesSelf = ps.Length > 0 && ps[0].ParameterType == typeof(Interpreter);
        var arity = ps.Length - (takesSelf ? 1 : 0);
        if (arity is < 1 or > 3 || ps.Skip(takesSelf ? 1 : 0).Any(p => p.ParameterType != typeof(RuntimeValue)))
            throw new InvalidOperationException(Messages.T("runtime.class.native_function_bad_signature", m.Name));

        if (takesSelf && self is null)
            throw new InvalidOperationException(Messages.T("runtime.class.interpreter_required", m.Name));

        if (takesSelf)
        {
            var me = self!;
            return arity switch
            {
                1 => FunctionVal.Direct(a => Call(m, [me, a])),
                2 => FunctionVal.Direct((a, b) => Call(m, [me, a, b])),
                _ => FunctionVal.Direct((a, b, c) => Call(m, [me, a, b, c])),
            };
        }

        return arity switch
        {
            1 => FunctionVal.Direct(m.CreateDelegate<Func<RuntimeValue, RuntimeValue>>()),
            2 => FunctionVal.Direct(m.CreateDelegate<Func<RuntimeValue, RuntimeValue, RuntimeValue>>()),
            _ => FunctionVal.Direct(m.CreateDelegate<Func<RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue>>()),
        };
    }

    internal static RuntimeValue Call(MethodInfo m, object?[] args)
    {
        try
        {
            return (RuntimeValue)m.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;   // 到不了
        }
    }

    internal static PropertyVal PropertyOf(Interpreter? self, MethodInfo m)
    {
        var acc = Fn(self, m);
        return new PropertyVal(acc, acc);
    }

    internal static BuiltinMethodVal Bind(ClassVal cls, MethodInfo m, string name)
    {
        var ps = m.GetParameters();
        var takesInterp = ps.Length > 0 && ps[0].ParameterType == typeof(Interpreter);
        if (takesInterp) ps = ps[1..];
        if (ps.Length == 0 || ps[0].ParameterType != typeof(RuntimeValue) || ps.Length > 3)
            throw new InvalidOperationException(Messages.T("runtime.class.method_bad_signature", cls.DisplayName, name));

        // **要解释器的成员**:签名第一个参数写 `Interpreter`,体里就能问 `HasTrait`、`SawScopedUse`
        // 这些只有解释器才知道的事(`:` 判"值是不是某个接口"就靠它)。
        // 普通那条路(`Impl`)拿不到解释器,所以只留一句"走错了"的兜底 —— 所有运算符调用点
        // 都走 `BuiltinMethodVal.Invoke(interp, …)`,不会落到这儿。
        if (takesInterp)
        {
            Func<Interpreter, RuntimeValue, RuntimeValue, RuntimeValue> withInterp = ps.Length switch
            {
                1 => (Interpreter i, RuntimeValue s, RuntimeValue a) => a is VoidVal
                    ? Call(m, [i, s])
                    : throw new RuntimeException(
                        $"类方法 {cls.DisplayName}.{name} 不收实参(它只有 self)—— 多给了 {a.Type}",
                        ErrorKind.Argument),
                2 => (Interpreter i, RuntimeValue s, RuntimeValue a) => Call(m, [i, s, a]),
                _ => (Interpreter i, RuntimeValue s, RuntimeValue a)
                    => FunctionVal.From(b => Call(m, [i, s, a, b])),
            };

            // **不传解释器那条路**(运算符被当值调:`x.: int`):借当前解释器。
            return new BuiltinMethodVal((s, a) => withInterp(
                    Interpreter.Current ?? throw new RuntimeException(
                        $"'{name}' 要在解释器里调用", ErrorKind.Error), s, a))
            {
                Name = name,
                WithInterp = withInterp,
            };
        }

        return new BuiltinMethodVal(ps.Length switch
        {
            // **只收 `self` 的那个:第二个槽摆着(形状是死的,两槽同步委托),但只认 `()`。**
            //
            // 从前写成 `(s, _) => …` —— 多给的实参**悄悄丢掉**。而 lambda 那边
            // `f 1 2` 是报「值 1 不是函数」的(柯里化:多给的成为"把结果再调一次")——
            // 于是这是**引擎里独一份的静默**。踩得很正:生成的适配层里 `AppendLine`
            // 排位第一的是 0 参数那个,`sb.AppendLine "尾巴"` 只加了个换行、实参没了,
            // `--warn` 也不喊(实测)。
            //
            // **"没给"和"多给"本来就分得开**:没给时那个槽收到的是 `()`。
            1 => (s, a) => a is VoidVal
                ? Call(m, [s])
                : throw new RuntimeException(
                    $"类方法 {cls.DisplayName}.{name} 不收实参(它只有 self)—— 多给了 {a.Type}",
                    ErrorKind.Argument),
            2 => (s, a) => Call(m, [s, a]),
            _ => (s, a) => FunctionVal.From(b => Call(m, [s, a, b])),
        })
        { Name = name };
    }
}
