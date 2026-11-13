namespace Ravel.Runtime;

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Ravel.Runtime.Values;

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

[AttributeUsage(AttributeTargets.Class)]
public sealed class RavelModuleAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RavelFnAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class RavelConstAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class RavelPropertyAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public bool ReadOnly { get; set; }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class RavelClassAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string Parent { get; init; } = "Object";

    public string? Implements { get; init; }
}

public static class PluginKit
{
    public static int Compare(RuntimeValue a, RuntimeValue b)
        => BuiltinClasses.Less(a, b) ? -1 : BuiltinClasses.Less(b, a) ? 1 : 0;

    public static List<RuntimeValue> Elements(RuntimeValue container, string what)
        => BuiltinClasses.ElementsOf(container, what);

    public static RuntimeValue Key(RuntimeValue k, string _what) => k;

    public static ClassVal ClassOf(string name, string what = "类型") => BuiltinClasses.ClassOf(name, what);

    public static ClassVal New(string name) => BuiltinClasses.New(name);

    public static string Guard(Func<string> show) => ShowDepth.Guard(show);

    public static RuntimeException Fail(string message, ErrorKind kind = ErrorKind.Value)
        => new(message, kind);

    // ── 值本身 ──
    //
    // 官方扩展(`Ravel.Extensions/`)要把原来那批 `System.*` 原语搬出去,而它们当时用的是
    // `Interpreter.As<T>` / `BuiltinClasses.IntArg` / `SysKit.BytesOf` —— **全是 `internal`**。
    // 下面这一段就是把那几件重新公开一遍:一件都不新造,规矩和引擎里那条**同一个**
    // (`BytesOf` 报错时照样说清是第几个不是字节,`Guarded` 兜的还是那几类异常)。

    public static RuntimeValue Void => VoidVal.Instance;

    public static ClassVal Any => BuiltinClasses.Object;

    public static FunctionVal Closure(string parameter, Func<RuntimeValue, RuntimeValue> body)
        => new NativeClosure(parameter, BuiltinClasses.Object, (_, v) => body(v));

    public static StringVal Text(RuntimeValue v, string what) => Interpreter.As<StringVal>(v, what);

    public static DictVal Dict(RuntimeValue v, string what) => Interpreter.As<DictVal>(v, what);

    public static ListVal List(RuntimeValue v, string what) => Interpreter.As<ListVal>(v, what);

    public static int Int(RuntimeValue v, string what) => BuiltinClasses.IntArg(v, what);

    public static bool Bool(RuntimeValue v, string what) => Interpreter.As<BoolVal>(v, what).Value;

    public static char Char(RuntimeValue v, string what) => Interpreter.As<CharVal>(v, what).Value;

    public static double Num(RuntimeValue v, string what)
        => BuiltinClasses.TryAsDouble(v, out var d)
            ? d
            : throw Fail($"{what} 需要数值参数，得到 {v.Type}", ErrorKind.Type);

    public static byte[] Bytes(RuntimeValue v, string what) => SysKit.BytesOf(v, what);

    public static ListVal BytesList(byte[] bytes) => SysKit.BytesList(bytes);

    public static RuntimeValue Narrow(long n)
        => n >= int.MinValue && n <= int.MaxValue ? IntVal.Of((int)n) : new BigIntVal(n);

    public static long Long(RuntimeValue v, string what) => v switch
    {
        IntVal i => i.Value,
        BigIntVal b when b.Value >= long.MinValue && b.Value <= long.MaxValue => (long)b.Value,
        _ => throw Fail($"{what} 需要整数参数，得到 {v.Type}", ErrorKind.Type),
    };

    // ── 数组 ──
    //
    // 参数表要 `T[]` 的 API 一大把(`String.Join` / `Path.Combine` / `File.WriteAllLines`…),
    // 而 Ravel 侧只有 `list`。这一格就是"把 `list` 逐个换成 `T[]`"。
    //
    // **错误里带上第几个**:一个元素类型不对,只说"某个元素不对"是没法查的。

    public static T[] ArrOf<T>(RuntimeValue v, string what, Func<RuntimeValue, string, T> one)
    {
        var elems = List(v, what).Elements;
        var outp = new T[elems.Count];
        for (var i = 0; i < outp.Length; i++)
            outp[i] = one(elems[i], $"{what} 的第 {i + 1} 个元素");
        return outp;
    }

    public static string[] Strs(RuntimeValue v, string what)  => ArrOf(v, what, (e, w) => Text(e, w).Value);
    public static char[]   Chars(RuntimeValue v, string what) => ArrOf(v, what, (e, w) => Char(e, w));
    public static int[]    Ints(RuntimeValue v, string what)  => ArrOf(v, what, (e, w) => Int(e, w));
    public static long[]   Longs(RuntimeValue v, string what) => ArrOf(v, what, (e, w) => Long(e, w));
    public static bool[]   Bools(RuntimeValue v, string what) => ArrOf(v, what, (e, w) => Bool(e, w));
    public static double[] Reals(RuntimeValue v, string what) => ArrOf(v, what, (e, w) => Num(e, w));

    // ── 路径与取值 ──

    public static string PathOf(RuntimeValue v, string what) => SysKit.PathOf(v, what);

    public static string Show(RuntimeValue v) => Interpreter.Show(v);

    public static void NeedFile(string p, string what) => SysKit.NeedFile(p, what);

    public static void NeedParentDir(string p, string what) => SysKit.NeedParentDir(p, what);

    public static RuntimeValue? Opt(DictVal d, string key) => SysKit.Opt(d, key);

    public static string OptText(DictVal d, string key, string dflt) => SysKit.OptText(d, key, dflt);
    public static int OptInt(DictVal d, string key, int dflt) => SysKit.OptInt(d, key, dflt);
    public static bool OptBool(DictVal d, string key, bool dflt) => SysKit.OptBool(d, key, dflt);

    // ── 和 .NET 打交道那几件 ──
    //
    // `NetBridge` 是引擎里的（`internal`，外部 dll 看不见），所以按这个文件一贯的规矩
    // 重新公开一遍。**一件都不新造**，背后就是 `NetBridge` 那三个方向。

    public static object? ToNet(RuntimeValue v) => NetBridge.ToNet(v);

    public static RuntimeValue ToRavel(object? o) => NetBridge.ToRavel(o);

    public static string NetShow(object? o) => NetBridge.Show(o);

    public static RuntimeValue Wrap(object? v, string className)
        => BuiltinClasses.ClassOrNull(className) is { } cls ? new DotNetVal(v, cls) : new DotNetVal(v);

    public static object? ToObject(RuntimeValue v, Type target, string what)
        => NetBridge.To(NetBridge.ToNet(v), target, what);

    public static void RegisterConverter(Type target, Func<object?, Type, object?> conv)
        => NetBridge.Register(target, conv);

    public static RuntimeValue Guarded(string what, Func<RuntimeValue> body) => SysKit.Fs(what, body);
}

internal static class PluginLoader
{
    public static void Load(Interpreter self, Assembly asm, Scope global, Dictionary<string, ModuleVal> modules)
    {
        HookDependencies(asm);

        var found = new List<Found>();

        foreach (var type in asm.GetTypes())
        {
            var module = type.GetCustomAttribute<RavelModuleAttribute>()?.Name;
            var cls = type.GetCustomAttribute<RavelClassAttribute>();

            if (cls is not null) InstallClass(self, type, cls, module, global, modules);
            if (module is null) continue;

            foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (m.GetCustomAttribute<RavelFnAttribute>() is { } fn)
                    found.Add(new Found(module, fn.Name, m, null));
                // 模块里的**属性** —— 落成一格 `by` 槽(读走 getter、写走 setter),
                // 和 `[ClassProperty]` 同一个机制,只是没有接收者
                else if (m.GetCustomAttribute<RavelPropertyAttribute>() is { } prop)
                    found.Add(new Found(module, prop.Name, m, null, Property: true, ReadOnly: prop.ReadOnly));

            // 常量挂在字段上:值就是那个 `RuntimeValue`(见 RavelConstAttribute)
            foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                if (f.GetCustomAttribute<RavelConstAttribute>() is { } c)
                    found.Add(new Found(module, c.Name, null, ReadField(f)));
        }

        // **按名字排**(先模块后名字)—— 和内置那趟(`SysRegistry.Scan`)同一条理由:
        // 反射给的先后取决定元数据顺序,**没有保证**,而成员表是**按登记先后**列的
        // (`Native.Fields ()` 打的就是它)。不排的话同一份 dll 换个运行时打出来顺序可能不一样,
        // 而那是没人会想到去查的坑。所以先收齐、排好、再登记。
        found.Sort((a, b) => string.CompareOrdinal(a.Module, b.Module) is var m && m != 0
            ? m
            : string.CompareOrdinal(a.Name, b.Name));

        foreach (var f in found)
        {
            // **`DefineOrReplace` 不是 `Define`**:装进来的是"这个模块该有的东西",
            // 而模块可能是**用户早就自己建过**的(`ravel "Math"` 建一个、之后才
            // `using "math.rav"`)。`Define` 撞名就报「已经定义过」,那是 `:=` 的规矩,
            // 不是"装库"的规矩 —— 装库这一刻该是**以库为准**。
            // (它只挡 `readonly` 那些:用户自己标了只读的东西,照样不许悄悄换掉。)
            var scope = ScopeOf(self, f.Module, global, modules);

            if (f.Property)
            {
                // 那一格是 **`by` 槽** —— 和 Ravel 里 `by x := property g s` 一个机制。
                // **类型约束给 `Any`**:写进来的是**属性值**,不是那个 property 本身
                // (同 `ClassProperty`,见 `MemberSet.Create` 那段)。
                var slot = scope.DefineOrReplace(f.Name, ClassRegistry.PropertyOf(self, f.Method!));
                slot.SetAttr(Attr.By);
                if (f.ReadOnly) slot.SetAttr(Attr.Readonly);
                continue;
            }

            scope.DefineOrReplace(f.Name, f.Method is null ? f.Value! : ClassRegistry.Fn(self, f.Method));
        }
    }

    private readonly record struct Found(string Module, string Name, MethodInfo? Method, RuntimeValue? Value,
                                         bool Property = false, bool ReadOnly = false);

    private static RuntimeValue ReadField(FieldInfo f)
        => f.GetValue(null) as RuntimeValue
           ?? throw new InvalidOperationException(
               $"常量 {f.DeclaringType?.Name}.{f.Name} 要是 static readonly RuntimeValue（现在 {f.FieldType.Name}）");

    private static void InstallClass(Interpreter self, Type type, RavelClassAttribute cls, string? module,
                                     Scope global, Dictionary<string, ModuleVal> modules)
    {
        // **幂等**:同一个 dll 被不同的解释器各 `using` 一次(每个用例一个解释器),
        // 类对象是**进程级**的(和内置类一个待遇),不能再建一个 —— 那会往 AllTypes 里灌一堆重复的。
        //
        // **"类建过没有"和"要不要把它放进这个解释器的模块"是两件事**,所以下面那一步在
        // 早退**之外**也得走:类对象是进程级的,而模块(`Structures` / `Native`)是**每个
        // 解释器各一份**。从前模块那一步在早退之后,于是**第二个**解释器里
        // `using "structures.rav"` 当场报「未定义的变量 'Structures'」—— 那个插件的成员
        // 全挂在类上(没有 `[RavelFn]`,那条路才不管早不早退),模块就再没人建了。
        if (!Known(cls.Name))
        {
            var klass = BuiltinClasses.New(cls.Name);
            var parent = BuiltinClasses.ClassOf(cls.Parent, $"类 {cls.Name} 的父类");
            BuiltinClasses.Link(klass, parent, BuiltinClasses.Type);
            BuiltinClasses.AddType(klass);

            // **"这个类是值类型"** —— 插件建不了 Ravel 的 `impl`,所以直接落那格索引
            // (见 `Implements` 与 `ClassVal.IsValueLike`)。名字写错当场报:
            // 留到运行期就成了"当键时才说不认识",够不着还难查。
            if (cls.Implements is { } impl)
            {
                if (impl != "IValue")
                    throw new InvalidOperationException(
                        $"类 {cls.Name} 写了 Implements = \"{impl}\" —— 这一格现在只认 \"IValue\""
                        + "(其它的接口请用 Ravel 侧的 `impl (某接口 类 { … })`)");
                klass.IsValueLike = true;
            }

            // **一份 `Scan` 收齐**:这个装载器类(C# 基类一并扫)上标了 `[ClassMethod]` /
            // `[ClassOp]` / `[ClassProperty]` / `[ClassCtor]` 的静态方法就是它的成员 ——
            // 和内置类同一套写法、同一个绑定(`ClassRegistry.Bind`)、同一份成员工厂
            // (`ClassVal.Members`)。从前这里是一段手写的 foreach,把每一格塞进一张外置
            // 注册表;那张注册表已经删了 —— 类自己带着,实例读成员时问自己。
            //
            // 父类那份**按引用接下**(`MemberSet.Scan` 的第二个参数):插件类照样白得
            // `Object` 的 `ToString` / `Fields`,以及它 C# 父类链上声明的那些。
            var members = MemberSet.Scan(type, (parent as ClassVal)?.Members ?? ObjectMembers.Set);
            klass.Members = members;

            // 构造器是**类体**那份(`ClassBody`),不是成员表里的一格 —— 对象实例化时
            // 沿祖先链跑类体、最后调它(见 `StepClassInit`);值类型不走那趟,它们的 `init`
            // 由装载器类上的 `[ClassCtor]` 提供(`VirtualScope` 就地交出去)。
            if (members.Ctor is { } ctor)
                BuiltinClasses.BuiltinCtor(klass, ctor);
        }

        if (module is not null)
            ScopeOf(self, module, global, modules)
                .DefineOrReplace(cls.Name, BuiltinClasses.ClassOf(cls.Name));
    }

    private static bool Known(string name)
        => BuiltinClasses.AllTypes.Any(t => t.DisplayName == name);

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

    private static readonly List<string> Dirs = [];
    private static bool _hooked;

    private static Scope ScopeOf(Interpreter self, string name, Scope global, Dictionary<string, ModuleVal> modules)
    {
        if (name.Length == 0) return global;
        if (modules.TryGetValue(name, out var mv)) return mv.Scope;

        // 扩展 dll 建的模块也是**这台引擎的**模块:它没写 `using`(`References ()` 给空表),
        // 但 `Owner` 这条得和别的模块一样有 —— `lib/math.rav` 那句 `ravel "Math"`
        // 认领的就是它(那时候引擎才接上引用那串)。
        mv = ModuleVal.WithOwnTable(name, global, self);
        modules[name] = mv;
        global.Define(name, mv);
        return mv.Scope;
    }
}
