namespace Ravel.Runtime;

using System.Reflection;
using System.Runtime.ExceptionServices;

/// <summary>把一个 C# 类登记成 Ravel 里的一个**内置类**(类型树上的一个节点)。
///
/// 用法和内置函数那套 `[Sys]` 一个路子:类上挂 `[BuiltinClass("Stack")]`,
/// 方法上挂 `[ClassMethod("Push")]`,构造器挂 `[ClassCtor]` —— 扫一遍自己捡。
/// **加一个结构 = 新写一个类文件**,不用回去改 `BuiltinClasses` 里那几张大表。
///
/// 那一族类都是 `static`,而且**不持有 `ClassVal`**:建出来的类对象统一在
/// `BuiltinClasses.ClassOf ("Stack")` 那张名字表里(`List` / `Set` / `Dict` 那些老兄弟也一样,
/// 见 `New`)。值那边就写 `StackVal.Type => ClassOf ("Stack")` —— 一次字典查,
/// 犯不着为每个结构再挂一个静态字段(挂了就又多一处"加新结构要记得改"的地方)。</summary>
[AttributeUsage(AttributeTargets.Class)]
internal sealed class BuiltinClassAttribute(string name) : Attribute
{
    /// <summary>Ravel 里那个类名(`Stack` / `Queue`…)</summary>
    public string Name { get; } = name;

    /// <summary>父类叫什么。默认 `object`;名字在**已经建出来的那些**里找
    /// (所以父类得早点扫到 —— 见 `Scan` 里那句按名字排)。</summary>
    public string Parent { get; init; } = "Object";
}

/// <summary>内置类的一个实例方法,名字就是 Ravel 里那个成员名。
///
/// 签名:**第一个参数是接收者**(`RuntimeValue self`),后面 0~2 个 `RuntimeValue` ——
/// 多参自动柯里化(和 `[Sys]` 那边一样,交给 `FunctionVal`)。</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ClassMethodAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>这个类的构造器 —— `Stack ()` / `Heap [3 1 2]` 走的就是它。
/// 签名 `static RuntimeValue New(RuntimeValue arg)`(不给实参时收到的是 `()`)。</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ClassCtorAttribute : Attribute;

/// <summary>`[BuiltinClass]` 那一族的**扫描与装配**:建类对象、登记方法、接进类型树。
///
/// 扫的是**整个程序集**(和 `[Sys]` 那趟一样,不列名单),扫完**按名字排** ——
/// 反射给的先后没有保证,而 `Subtypes ()` / 类型树打印都按登记先后列,
/// 不排的话同一份源码在不同运行时上打出来的顺序都可能不一样。</summary>
internal static class ClassRegistry
{
    /// <summary>扫出来建好的那些类(**按名字排**,和登记进 `AllTypes` 一个顺序)。
    /// `SysModule.Fill` 拿它把 `System.Stack` 这些名字摆进模块。</summary>
    private static readonly List<ClassVal> Built = [];

    public static IReadOnlyList<ClassVal> Classes => Built;

    /// <summary>按名字建出所有内置类(挂父类、登记方法、进 `AllTypes`)。
    /// 建树那一步(见 `BuiltinClasses` 的静态构造器)在最后调它一次。</summary>
    public static void Install(List<ClassVal> allTypes)
    {
        foreach (var (attr, host) in Scan())
        {
            var cls = BuiltinClasses.New(attr.Name);
            BuiltinClasses.Link(cls, Parent(attr.Parent), BuiltinClasses.Type);

            foreach (var m in host.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.GetCustomAttribute<ClassMethodAttribute>() is { } method)
                    BuiltinClasses.EngineMember(cls, method.Name, Bind(cls, m, method.Name));
                else if (m.GetCustomAttribute<ClassCtorAttribute>() is not null)
                    BuiltinClasses.SetCtor(cls, m);
            }

            allTypes.Add(cls);
            Built.Add(cls);
        }
    }

    /// <summary>父类:名字在**已有的类**里找(`ClassOf` 那张表;扫在前面的也在里面)。
    /// 找不到当场报 —— 拼错一个名字不该静默挂到 `object` 上。</summary>
    private static ClassVal Parent(string name)
        => BuiltinClasses.ClassOf(name, "内置类的父类");

    private static List<(BuiltinClassAttribute Attr, Type Host)> Scan()
    {
        var found = new List<(BuiltinClassAttribute, Type)>();
        foreach (var t in typeof(Interpreter).Assembly.GetTypes())
            if (t.GetCustomAttribute<BuiltinClassAttribute>() is { } attr)
                found.Add((attr, t));

        found.Sort((a, b) => string.CompareOrdinal(a.Item1.Name, b.Item1.Name));
        return found;
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
    private static BuiltinMethodVal Bind(ClassVal cls, MethodInfo m, string name)
    {
        var ps = m.GetParameters();
        if (ps.Length == 0 || ps[0].ParameterType != typeof(RuntimeValue) || ps.Length > 3)
            throw new InvalidOperationException($"内置类方法 {cls.DisplayName}.{name} 的签名不对:要收 (self, 0~2 个实参)");

        return new BuiltinMethodVal(ps.Length switch
        {
            1 => (s, _) => Call(m, [s]),
            2 => (s, a) => Call(m, [s, a]),
            _ => (s, a) => FunctionVal.From(b => Call(m, [s, a, b])),
        })
        { Name = name };
    }
}
