namespace Ravel.Runtime;

using System.Reflection;
using System.Runtime.ExceptionServices;

/// <summary>内置类的一个实例方法,名字就是 Ravel 里那个成员名。
///
/// 签名:**第一个参数是接收者**(`RuntimeValue self`),后面 0~2 个 `RuntimeValue` ——
/// 多参自动柯里化(和 `[Sys]` 那边一样,交给 `FunctionVal`)。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClassMethodAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>这个类的构造器 —— `Stack ()` / `Heap [3 1 2]` 走的就是它。
/// 签名 `static RuntimeValue New(RuntimeValue arg)`(不给实参时收到的是 `()`)。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClassCtorAttribute : Attribute;

/// <summary>这个类的一个**运算符** —— `a + b` / `a == b` 走的就是它。
///
/// 签名和 <see cref="ClassMethodAttribute"/> **同一个形状**:`(RuntimeValue a, RuntimeValue b)`,
/// 两个操作数都递进来。区别在**认不认接收者**:普通方法第一个参数是"谁在调",
/// 运算符没有"谁" —— 两边地位一样(`Interpreter.BindOperator` 查的也是
/// `left.MemberScope`,查到了直接 `Impl(left, right)`,不绑 self)。
///
/// 名字得在 `OperatorSymbols.All` 里(`+` `-` `*` `/` `%` `**` `==` `!=` `<` `>` `<=` `>=`
/// `&` `|` `^` `<<` `>>` `<<<` `>>>` …)—— **装的时候就查**,
/// 写错了当场报,不留到运行期(`a <> b` 那种根本解析不出来)。
///
/// `:` / `!` / `&&` / `||` **不在**那一档:它们归求值器特判,类型这一层插不了手。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClassOpAttribute(string op) : Attribute
{
    public string Op { get; } = op;
}

/// <summary>类的**绑定**那一格:把 `[ClassMethod]` / `[ClassCtor]` 的方法变成 Ravel 认的东西
/// (实例成员 / 预设类体),外加两件反射的杂活。
///
/// 从前这里还有"扫整个程序集建内置类"那一趟(`[BuiltinClass]`)—— 数据结构搬进插件之后,
/// 主项目里已经没有用那条路声明出来的类了,那一趟就删了。**装配现在只有一条路**:
/// `PluginLoader` 在 `using "x.dll"` 时跑(见 `PluginApi.cs`)。</summary>
internal static class ClassRegistry
{
    /// <summary>把一个"模块里的函数"方法(`[Sys]` / `[RavelFn]` 那些)绑成函数值:
    /// 1~3 个 `RuntimeValue`,**开头可以是 `Interpreter`**(要用引擎的那种)。
    /// 内置那趟和插件那趟共用这一条 —— 规矩只有一处。</summary>
    internal static FunctionVal Fn(Interpreter? self, MethodInfo m)
    {
        var ps = m.GetParameters();
        var takesSelf = ps.Length > 0 && ps[0].ParameterType == typeof(Interpreter);
        var arity = ps.Length - (takesSelf ? 1 : 0);
        if (arity is < 1 or > 3 || ps.Skip(takesSelf ? 1 : 0).Any(p => p.ParameterType != typeof(RuntimeValue)))
            throw new InvalidOperationException($"函数 {m.Name} 的签名不对:要收 1~3 个 RuntimeValue（开头可以是 Interpreter）");

        if (takesSelf && self is null)
            throw new InvalidOperationException($"函数 {m.Name} 要一个 Interpreter,但这一趟没有");

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

    /// <summary>调那个方法。**要把反射那层壳剥掉**:`MethodInfo.Invoke` 会把方法里抛的
    /// 异常包成 `TargetInvocationException` —— 那玩意儿不是 `RuntimeException`,
    /// 求值器接不住、Ravel 层更接不住,一路打成「解释器内部错误」。
    /// (`ExceptionDispatchInfo` 是为了把栈保住。)</summary>
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

    /// <summary>把一个 `[ClassMethod]` 方法包成实例成员:第一个参数是接收者,
    /// 后面 0~2 个实参(多参柯里化,和 `[Sys]` 那套一个走法)。</summary>
    internal static BuiltinMethodVal Bind(ClassVal cls, MethodInfo m, string name)
    {
        var ps = m.GetParameters();
        if (ps.Length == 0 || ps[0].ParameterType != typeof(RuntimeValue) || ps.Length > 3)
            throw new InvalidOperationException($"类方法 {cls.DisplayName}.{name} 的签名不对:要收 (self, 0~2 个实参)");

        return new BuiltinMethodVal(ps.Length switch
        {
            1 => (s, _) => Call(m, [s]),
            2 => (s, a) => Call(m, [s, a]),
            _ => (s, a) => FunctionVal.From(b => Call(m, [s, a, b])),
        })
        { Name = name };
    }
}
