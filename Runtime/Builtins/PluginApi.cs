namespace Ravel.Runtime;

using System.Reflection;

// ============================================================
//  插件那一套:**公开**的口子
// ============================================================
//
// 引擎自己用的那些(`[Sys]` / `[BuiltinClass]` / `BuiltinClasses` 里的助手)全是 `internal`
// —— 同一个程序集里随便用,可**外部的 dll 看不见**。所以这一格把"写扩展需要的东西"
// 重新公开一遍:特性 + 几件本来在 `BuiltinClasses` / `Structure` 里的小工具。
//
// 规则和内置那边**一模一样**(同一套签名、同一个柯里化、同一条"比大小走 Ravel 的 `<`"),
// 只是换了个 `public` 的门。

/// <summary>这个类里的东西进哪个模块。模块名就是 Ravel 里那个名字:
/// `using "x.dll"` 之后 `模块名.成员` 就能用;模块不存在就**建一个**。
///
/// **`""` 这个特例**:进**全局作用域**(不建模块,直接叫名字)。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RavelModuleAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>模块里的一个**函数**。签名和内置那边一样:
/// `static RuntimeValue F(RuntimeValue a)`(1~3 个),要用引擎就把它当第一个参数收
/// (`Interpreter self`)—— 用不着的那几条就是**纯函数**。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RavelFnAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>一个 **Ravel 类**(类型树上的节点)。里面的方法挂 <see cref="ClassMethodAttribute"/>、
/// 构造器挂 <see cref="ClassCtorAttribute"/> —— 和引擎里那些内置类写法完全一样。
///
/// 旁边再挂一个 <see cref="RavelModuleAttribute"/> 的话,这个**类对象**也进那个模块
/// (`模块.类名 ()`);不挂就只是类型(在树里、`is` 认,但得自己去拿类对象)。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RavelClassAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>父类叫什么(默认 `object`;名字在已有的类里找,包括别的插件带来的)</summary>
    public string Parent { get; init; } = "Object";
}

/// <summary>插件作者要用的几件小工具 —— 引擎里都有(而且**同一套脾气**),只是那些是 `internal`。
///
/// 一件都不新造:`Compare` 走 Ravel 的 `<`(比不了当场报「比不了 X 与 Y」,
/// 不会让 .NET 的比较器在里面炸)、`Elements` 走 `foreach` 那个口径、`Key` 走 `dict` 那个规矩。</summary>
public static class PluginKit
{
    /// <summary>照 Ravel 的 `<` 比大小:小于 -1、大于 1、比不了(或者一样)0 ——
    /// 注意"比不了"和"相等"在这儿都是 0,**要比大小就先确保它们能比**。</summary>
    public static int Compare(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.Less(a, b) ? -1 : BuiltinClasses.Less(b, a) ? 1 : 0;

    /// <summary>一个容器里的元素(按枚举顺序)。list / set / dict 和数据结构那一族都吃。</summary>
    public static List<RuntimeValue> Elements(RuntimeValue container, string what)
        => BuiltinClasses.ElementsOf(container, what);

    /// <summary>键规范:得是**值类型**(数 / 字符串),和 `dict` 一个规矩。</summary>
    public static RuntimeValue Key(RuntimeValue k, string what) => BuiltinClasses.KeyArg(k, what);

    /// <summary>按名字拿一个类对象(`Stack` / `List`…)。自己的类在 `New ("名字")` 里建,
    /// 建完就能按名字找到。</summary>
    public static ClassVal ClassOf(string name, string what = "类型") => BuiltinClasses.ClassOf(name, what);

    /// <summary>建一个类对象(还没挂父类 —— 挂父类那一步由 `[RavelClass]` 的装配做)。
    /// 插件里自己的值类型要拿"我属于哪个类"就调它。</summary>
    public static ClassVal New(string name) => BuiltinClasses.New(name);

    /// <summary>打印一个容器时的**深度护栏**:自己包含自己的容器(`d.Set "self" d`)会无限递归,
    /// 走到第 N 层就收住(`...`)。插件的值类型写 `ToString ()` 时套一层就行。</summary>
    public static string Guard(Func<string> show) => ShowDepth.Guard(show);

    /// <summary>比不了 / 空集合这类**说人话**的错误(报出来的就是 Ravel 层的类型)。</summary>
    public static RuntimeException Fail(string message, ErrorKind kind = ErrorKind.Value)
        => new(message, kind);
}

/// <summary>`[RavelModule]` / `[RavelFn]` / `[RavelClass]` 那一族的扫描与装配。
///
/// 和内置那两趟(`SysRegistry` / `ClassRegistry`)是**同一套规矩**,只是扫的对象换成了
/// "`using` 进来的那个 dll":扫出来的函数进模块、类进类型树(挂了模块名的再顺手进模块)。</summary>
internal static class PluginLoader
{
    /// <summary>把这个程序集里的东西装进来。`modules` 是解释器那张"名字 → 模块"的表
    /// (和 `System` 一个地方),不存在就现建一个。</summary>
    public static void Load(Interpreter self, Assembly asm, Scope global, Dictionary<string, ModuleVal> modules)
    {
        foreach (var type in asm.GetTypes())
        {
            var module = type.GetCustomAttribute<RavelModuleAttribute>()?.Name;
            var cls = type.GetCustomAttribute<RavelClassAttribute>();

            if (cls is not null) InstallClass(self, type, cls, module, global, modules);

            if (module is null) continue;
            foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.GetCustomAttribute<RavelFnAttribute>() is { } fn)
                    ScopeOf(module, global, modules).Define(fn.Name, BuiltinClasses.Function, ClassRegistry.Fn(self, m));
        }
    }

    /// <summary>建一个插件带来的类:建类对象、挂父类、登记方法/构造器、进 `AllTypes`,
    /// 挂了模块名的话再把类对象摆进那个模块。</summary>
    private static void InstallClass(Interpreter self, Type type, RavelClassAttribute cls, string? module,
                                     Scope global, Dictionary<string, ModuleVal> modules)
    {
        // **幂等**:同一个 dll 被不同的解释器各 `using` 一次(每个用例一个解释器),
        // 类对象是**进程级**的(和内置类一个待遇),不能再建一个 —— 那会往 AllTypes 里灌一堆重复的。
        if (Known(cls.Name)) return;

        var klass = BuiltinClasses.New(cls.Name);
        BuiltinClasses.Link(klass, BuiltinClasses.ClassOf(cls.Parent, $"类 {cls.Name} 的父类"), BuiltinClasses.Type);
        BuiltinClasses.AddType(klass);

        foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.GetCustomAttribute<ClassMethodAttribute>() is { } method)
                BuiltinClasses.EngineMember(klass, method.Name, ClassRegistry.Bind(klass, m, method.Name));
            else if (m.GetCustomAttribute<ClassCtorAttribute>() is not null)
                BuiltinClasses.SetCtor(klass, m);
        }

        if (module is not null)
            ScopeOf(module, global, modules).Define(cls.Name, BuiltinClasses.Type, klass);
    }

    /// <summary>这个名字的类已经有了吗(内置的、或者上一个解释器装进来的)</summary>
    private static bool Known(string name)
        => BuiltinClasses.AllTypes.Any(t => t.DisplayName == name);

    /// <summary>模块名 → 那个模块的作用域。没见过的名字现建一个(和 `ravel "X"` 建模块一个做法)。
    /// 名字是 `""` 就是**全局作用域**(不建模块)。</summary>
    private static Scope ScopeOf(string name, Scope global, Dictionary<string, ModuleVal> modules)
    {
        if (name.Length == 0) return global;
        if (modules.TryGetValue(name, out var mv)) return mv.Scope;

        var mt = BuiltinClasses.NewModuleClass(name, BuiltinClasses.Ravel);
        mv = new ModuleVal(mt, new Scope(global));
        modules[name] = mv;
        global.Define(name, mt, mv);
        return mv.Scope;
    }
}
