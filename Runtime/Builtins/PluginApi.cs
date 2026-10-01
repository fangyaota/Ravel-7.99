namespace Ravel.Runtime;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

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

/// <summary>模块里的一个**常量**(挂在 `static readonly` **字段**上,值就是那个 `RuntimeValue`)。
///
/// 「函数还是常量」在引擎里原来是两处(`[Sys]` 收函数、`SysModule` 那三张表摆数据),
/// 理由是**数据一眼看全比撒在各处好读**。外置之后没有"一眼看全"的余地了,
/// 但这条区别还在:`Math.Pi` 是个**值**、不是一枚函数 —— 拿 `[RavelFn]` 登记
/// 就会让 `Math.Pi` 变成"要写 `Math.Pi ()` 才拿到数",那是换了个 API,不是搬了个家。
/// 所以常量单开一条,类型槽用**值自己的类型**(和 `System.True` 那些一个写法)。</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class RavelConstAttribute(string name) : Attribute
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

    // ── 值本身 ──
    //
    // 官方扩展(`Ravel.Extensions/`)要把原来那批 `System.*` 原语搬出去,而它们当时用的是
    // `Interpreter.As<T>` / `BuiltinClasses.IntArg` / `SysKit.BytesOf` —— **全是 `internal`**。
    // 下面这一段就是把那几件重新公开一遍:一件都不新造,规矩和引擎里那条**同一个**
    // (`BytesOf` 报错时照样说清是第几个不是字节,`Guarded` 兜的还是那几类异常)。

    /// <summary>`()` —— 空值。原生函数"什么都不交回"时给这个,别给 null。</summary>
    public static RuntimeValue Void => VoidVal.Instance;

    /// <summary>顶类型 `Any`。造原生闭包当参数类型时用(见 <see cref="Closure"/>)</summary>
    public static ClassVal Any => BuiltinClasses.Any;

    /// <summary>造一枚**原生闭包**:形状像 lambda,体是 C#。`NumberSource` 那种"交回一枚
    /// 取数的函数"就靠它 —— `Parameter` 是它将来收的那个参数叫什么。</summary>
    public static FunctionVal Closure(string parameter, Func<RuntimeValue, RuntimeValue> body)
        => new NativeClosure(parameter, BuiltinClasses.Any, (_, v) => body(v));

    /// <summary>参数收束成**字符串**;不是就报 Ravel 错误(消息里带 Ravel 的类型名)</summary>
    public static StringVal Text(RuntimeValue v, string what) => Interpreter.As<StringVal>(v, what);

    /// <summary>参数收束成**字典**(`{}`)</summary>
    public static DictVal Dict(RuntimeValue v, string what) => Interpreter.As<DictVal>(v, what);

    /// <summary>参数收束成**表**(`[]`)</summary>
    public static ListVal List(RuntimeValue v, string what) => Interpreter.As<ListVal>(v, what);

    /// <summary>参数收束成 **int**(不是数、或者是个装不下的 bigint,都会说人话)</summary>
    public static int Int(RuntimeValue v, string what) => BuiltinClasses.IntArg(v, what);

    /// <summary>数值参数收成 `double` 来算 —— **五种数值类型全吃**(int / float / bigint /
    /// fraction / bigfraction),和 `<` 那批运算符同一个口径(都是
    /// `BuiltinClasses.TryAsDouble`)。不这么做的话 `sin 1` 和 `1 &lt; 2` 就成了两套说法。
    /// 非数值报 `what` 归属的错误 —— 传函数名进来(`Num (a, "Sin")`)。</summary>
    public static double Num(RuntimeValue v, string what)
        => BuiltinClasses.TryAsDouble(v, out var d)
            ? d
            : throw Fail($"{what} 需要数值参数，得到 {v.Type}", ErrorKind.Type);

    /// <summary>字节表(list,0..255)→ `byte[]`。元素不是字节就当场说清是**第几个**不对</summary>
    public static byte[] Bytes(RuntimeValue v, string what) => SysKit.BytesOf(v, what);

    /// <summary>`byte[]` → 字节表(list)</summary>
    public static ListVal BytesList(byte[] bytes) => SysKit.BytesList(bytes);

    /// <summary>算出来的整数**收窄**:装得下 `int` 就给 `int`,否则给 `bigint`。
    /// (照 Ravel 的口径来,别自己 `(int)` 硬转 —— 那会静默回绕。)</summary>
    public static RuntimeValue Narrow(long n, string what) => BuiltinClasses.Narrow(n, what);

    // ── 路径与取值 ──

    /// <summary>路径收束:要一个字符串。(扩展里要收**磁盘条目**就当文件写,这一条只管字符串。)
    ///
    /// 叫 `PathOf` 不叫 `Path`:`using static` 进来的**方法**会把同名的**类型**盖住 ——
    /// 叫 `Path` 的话扩展里 `Path.GetFileName (…)` 当场编不过(实测撞过)</summary>
    public static string PathOf(RuntimeValue v, string what) => SysKit.PathOf(v, what);

    /// <summary>值 → 字符串(Ravel 语义,和 `print` 一个口径)</summary>
    public static string Show(RuntimeValue v) => Interpreter.Show(v);

    /// <summary>"这个文件得在" —— 不在(或者是个目录)就报 `IoError`,消息里带路径</summary>
    public static void NeedFile(string p, string what) => SysKit.NeedFile(p, what);

    /// <summary>"这个文件的上一级目录得在" —— 写文件前问一句,比让 .NET 抛个英文的好</summary>
    public static void NeedParentDir(string p, string what) => SysKit.NeedParentDir(p, what);

    /// <summary>从一个**参数表**(dict)里取一个键;没给(或者给的是 `()`)就是 null。
    /// 收一个 dict、缺的走默认那几条原语全靠它。</summary>
    public static RuntimeValue? Opt(DictVal d, string key) => SysKit.Opt(d, key);

    public static string OptText(DictVal d, string key, string dflt) => SysKit.OptText(d, key, dflt);
    public static int OptInt(DictVal d, string key, int dflt) => SysKit.OptInt(d, key, dflt);
    public static bool OptBool(DictVal d, string key, bool dflt) => SysKit.OptBool(d, key, dflt);

    /// <summary>兜底:把一类 C# 异常(IO / 权限 / 参数 / 溢出 / 平台不支持…)翻成 Ravel 的
    /// `IoError`。**别让 C# 异常漏到顶层**是这批原语一条老规矩 —— 漏出去会绕过 Ravel 的
    /// `try` 把程序打掉。要兜别的异常族(比如 `SqliteException`)就自己写一个,别硬塞进来。</summary>
    public static RuntimeValue Guarded(string what, Func<RuntimeValue> body) => SysKit.Fs(what, body);
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
        HookDependencies(asm);

        var found = new List<(string Module, string Name, MethodInfo? Method, RuntimeValue? Value)>();

        foreach (var type in asm.GetTypes())
        {
            var module = type.GetCustomAttribute<RavelModuleAttribute>()?.Name;
            var cls = type.GetCustomAttribute<RavelClassAttribute>();

            if (cls is not null) InstallClass(self, type, cls, module, global, modules);
            if (module is null) continue;

            foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.GetCustomAttribute<RavelFnAttribute>() is { } fn)
                    found.Add((module, fn.Name, m, null));

            // 常量挂在字段上:值就是那个 `RuntimeValue`(见 RavelConstAttribute)
            foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (f.GetCustomAttribute<RavelConstAttribute>() is { } c)
                    found.Add((module, c.Name, null, ReadField(f)));
        }

        // **按名字排**(先模块后名字)—— 和内置那趟(`SysRegistry.Scan`)同一条理由:
        // 反射给的先后取决定元数据顺序,**没有保证**,而成员表是**按登记先后**列的
        // (`Native.Fields ()` 打的就是它)。不排的话同一份 dll 换个运行时打出来顺序可能不一样,
        // 而那是没人会想到去查的坑。所以先收齐、排好、再登记。
        found.Sort((a, b) => string.CompareOrdinal(a.Module, b.Module) is var m && m != 0
            ? m
            : string.CompareOrdinal(a.Name, b.Name));

        foreach (var (module, name, m, value) in found)
            // **`DefineOrReplace` 不是 `Define`**:装进来的是"这个模块该有的东西",
            // 而模块可能是**用户早就自己建过**的(`ravel "Math"` 建一个、之后才
            // `using "math.rav"`)。`Define` 撞名就报「已经定义过」,那是 `:=` 的规矩,
            // 不是"装库"的规矩 —— 装库这一刻该是**以库为准**。
            // (它只挡 `readonly` 那些:用户自己标了只读的东西,照样不许悄悄换掉。)
            ScopeOf(module, global, modules)
                .DefineOrReplace(name, value?.Type ?? BuiltinClasses.Function, m is null ? value! : ClassRegistry.Fn(self, m));
    }

    /// <summary>读一个 `[RavelConst]` 字段。**静态字段的初始化时机是它自己那边的事** ——
    /// 读到 null 只可能是"字段不是 `static readonly RuntimeValue` 那个形状",当场说清楚。</summary>
    private static RuntimeValue ReadField(FieldInfo f)
        => f.GetValue(null) as RuntimeValue
           ?? throw new InvalidOperationException(
               $"常量 {f.DeclaringType?.Name}.{f.Name} 要是 static readonly RuntimeValue（现在 {f.FieldType.Name}）");

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
            ScopeOf(module, global, modules).DefineOrReplace(cls.Name, BuiltinClasses.Type, klass);
    }

    /// <summary>这个名字的类已经有了吗(内置的、或者上一个解释器装进来的)</summary>
    private static bool Known(string name)
        => BuiltinClasses.AllTypes.Any(t => t.DisplayName == name);

    /// <summary>插件**自己带的本机库**得找得到 —— 现在只有一个例子:扩展拖来的
    /// `SQLitePCLRaw.lib.e_sqlite3`,也就是 `runtimes/<rid>/native/e_sqlite3.dll`。
    ///
    /// **托管**那几件(`Microsoft.Data.Sqlite.dll` / `SQLitePCLRaw.*.dll`)不用管:
    /// `LoadFrom` 会从插件所在的目录解出来。本机的走另一条路 —— 默认探测看的是**主程序**的
    /// `deps.json`,而插件带的包不在那里面。解不出来就是一句 `TypeInitializationException`
    /// 套着 `DllNotFoundException`,连"少的是哪个文件"都不说(所以报错那条也顺手改了,
    /// 见 Program.cs 的 `Inner`)。
    ///
    /// 试过的另一条路:`AssemblyDependencyResolver`(读插件那份 `.deps.json`)。**它对
    /// 不带 RID 的库 deps.json 认不出 `runtimeTargets`** —— 挂上之后 `e_sqlite3` 还是
    /// 返回 null(实测)。所以改成自己按**当前 RID** 探两处,都在插件旁边:
    /// 平铺那一层,和 NuGet 的老规矩 `runtimes/<rid>/native/`。
    ///
    /// 只在**默认解析都失败之后**才轮到这儿,所以 `ravel.dll` 这类共用件从来不会
    /// 走到这条路来,也不会被解成插件目录里的一份。</summary>
    private static void HookDependencies(Assembly asm)
    {
        Dirs.Add(Path.GetDirectoryName(asm.Location)!);
        if (_hooked) return;                                  // 口子只挂一次
        _hooked = true;

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            foreach (var dir in Dirs)
            {
                if (FindNative(dir, name) is not { } p) continue;
                return NativeLibrary.Load(p);
            }

            return IntPtr.Zero;                               // 找不到就交回默认那套去报错
        };
    }

    /// <summary>插件目录下按当前 RID 找一本机库:先平铺,再 `runtimes/&lt;rid&gt;/native/`。
    /// 后缀三种都试 —— RID 已经说了是哪个平台,不必再问一遍 `OperatingSystem.IsWindows`。</summary>
    private static string? FindNative(string dir, string name)
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        foreach (var suffix in new[] { ".dll", ".so", ".dylib" })
        {
            var flat = Path.Combine(dir, name + suffix);
            if (File.Exists(flat)) return flat;

            var underRuntimes = Path.Combine(dir, "runtimes", rid, "native", name + suffix);
            if (File.Exists(underRuntimes)) return underRuntimes;
        }

        return null;
    }

    /// <summary>装进来那些插件各自的目录,外加"口子挂没挂"</summary>
    private static readonly List<string> Dirs = [];
    private static bool _hooked;

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
