using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Ravel.Runtime;

/// <summary>内置注册:建 System 模块,填入类型别名、控制内建和核心函数。
///
/// **加一个内置函数 = 写一个带 `[Sys("名字")]` 的方法**(放哪个主题的文件里都行,
/// 反射那趟自己捡,见 <see cref="RegisterSysFns"/>)—— 没有第二处要改。
/// 类型别名 / 常量 / 控制内建那三张表还是明着列,理由见 <see cref="SysAttribute"/>。</summary>
public partial class Interpreter
{
    private void RegisterBuiltins()
    {
        var systemModule = BuildSystemModule();
        _modules["System"] = systemModule;
        _global.Define("System", systemModule.Type, systemModule);
    }

    /// <summary>组装 System 模块——它是唯一「用 C# 写死」的模块,其余模块都来自 .rav 文件。
    /// 三张表先摆好(类型别名 / 常量 / 控制内建),函数则由反射那趟一次登记完 ——
    /// 顺序是**按名字排**的(见 `ScanSysMethods`),所以成员表打出来是稳定的一份。</summary>
    private ModuleVal BuildSystemModule()
    {
        var moduleType = BuiltinClasses.NewModuleClass("System", BuiltinClasses.Ravel);
        var module = new ModuleVal(moduleType, new Scope(_global));
        _sysScope = module.Scope;

        Types();
        Constants();
        Controls();
        RegisterSysFns();

        _sysScope = null;
        return module;
    }

    /// <summary>建 `System` 模块期间"往哪张表登记" —— `BuildSystemModule` 起个头,
    /// 下面那几段各登记一批,建完置回 null。它们从前是同一个方法里的局部函数与语句,
    /// 靠闭包攥着这张表;拆成方法之后就得有个地方放它。</summary>
    private Scope? _sysScope;

    private void Def(string name, ObjectVal type, RuntimeValue value) => _sysScope!.Define(name, type, value);
    // 类对象自己就是那个值,不再包一层
    private void DefType(string name, ObjectVal type) => Def(name, BuiltinClasses.Type, type);
    private void DefFn(string name, FunctionVal fn) => Def(name, BuiltinClasses.Function, fn);
    private void DefControl(string name, ControlKind kind, int arity) => DefFn(name, new ControlFunction(kind, arity, RList<RuntimeValue>.Empty));

    // ── 下面几段共用的小工具 ──

    private static string PathOf(RuntimeValue v, string what)
            => v is StringVal s
                ? s.Value
                : throw new RuntimeException($"{what} 需要一个路径字符串，得到 {v.Type}", ErrorKind.Argument);

    private static void NeedFile(string p, string what)
    {
        if (Directory.Exists(p))
            throw new RuntimeException($"{what}: 这是个目录，不是文件 —— {p}", ErrorKind.Io);
        if (!File.Exists(p))
            throw new RuntimeException($"{what}: 找不到文件 —— {p}", ErrorKind.Io);
    }

    private static void NeedParentDir(string p, string what)
    {
        // 父目录**按用户写的那串**切(最后一个分隔符之前),不转绝对路径、也不交给 .NET 归一化 ——
        // 报错里要看到的就是他写的那串:`.io-test/nodir`,而不是被换成反斜杠的版本,
        // 更不是带盘符的完整路径。就一个文件名(没有分隔符)时没有父目录可查,交给下面去管。
        var cut = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
        if (cut <= 0) return;
        var dir = p[..cut];
        if (!Directory.Exists(dir))
            throw new RuntimeException($"{what}: 目录不存在 —— {dir}", ErrorKind.Io);
    }

    private static string EnvName(RuntimeValue v, string what)
    {
        var name = As<StringVal>(v, $"{what} 的名字").Value;
        if (name.Length == 0) throw new RuntimeException($"{what}: 变量名不能是空的", ErrorKind.Value);
        return name;
    }

    /// <summary>类型与错误族 —— `System.Integer` 那些**权威名**(小写别名在 predefined.rav)</summary>
    private void Types()
    {
        DefType("Integer", BuiltinClasses.Int);
        DefType("String", BuiltinClasses.String);
        DefType("Char", BuiltinClasses.Char);
        DefType("Bool", BuiltinClasses.Bool);
        DefType("Float", BuiltinClasses.Float);
        DefType("BigInteger", BuiltinClasses.BigInt);
        DefType("Fraction", BuiltinClasses.Fraction);
        DefType("BigFraction", BuiltinClasses.BigFraction);
        DefType("Range", BuiltinClasses.Range);
        DefType("List", BuiltinClasses.List);
        DefType("Set", BuiltinClasses.Set);
        DefType("Dict", BuiltinClasses.Dict);
        DefType("Object", BuiltinClasses.Object);
        DefType("Function", BuiltinClasses.Function);
        DefType("Continuation", BuiltinClasses.Continuation);   // callcc 交出来的那枚续延
        DefType("Void", BuiltinClasses.Void);
        DefType("Type", BuiltinClasses.Type);
        DefType("Interface", BuiltinClasses.Interface);
        DefType("BaseInterface", BuiltinClasses.BaseInterface);   // 所有接口的基类
        DefType("ValueType", BuiltinClasses.ValueType);
        DefType("Any", BuiltinClasses.Any);
        DefType("Every", BuiltinClasses.Every);
        DefType("Exception", BuiltinClasses.Exception);
        // 错误那一族 —— 引擎报错时挑的就是它们(见 Runtime/BuiltinClasses.Errors.cs)
        DefType("TypeError", BuiltinClasses.TypeError);
        DefType("NameError", BuiltinClasses.NameError);
        DefType("AttributeError", BuiltinClasses.AttributeError);
        DefType("IndexError", BuiltinClasses.IndexError);
        DefType("KeyError", BuiltinClasses.KeyError);
        DefType("ZeroDivisionError", BuiltinClasses.ZeroDivisionError);
        DefType("AssertionError", BuiltinClasses.AssertionError);
        DefType("AccessError", BuiltinClasses.AccessError);
        DefType("ArgumentError", BuiltinClasses.ArgumentError);
        DefType("ValueError", BuiltinClasses.ValueError);
        DefType("IoError", BuiltinClasses.IoError);
        DefType("RegexError", BuiltinClasses.RegexError);
        DefType("Json", BuiltinClasses.Json);
    }

    /// <summary>常量,以及几个"顺手"的建库钩子(错误钩子、模块加载状态、`use`/`impl`/`eval` 那几个控制内建)</summary>
    private void Constants()
    {
        Def("True", BuiltinClasses.Bool, new BoolVal(true));
        Def("False", BuiltinClasses.Bool, new BoolVal(false));
        // 特殊浮点值。和 True/False 同款:系统模块里的一个值,`predefined.rav` 给全局别名
        // (`-Inf` 不用另设,一元 `-` 对 Float 就是取负)
        Def("NaN", BuiltinClasses.Float, new FloatVal(double.NaN));
        Def("Inf", BuiltinClasses.Float, new FloatVal(double.PositiveInfinity));
        Def("Default", BuiltinClasses.Every, DefaultVal.Instance);
    }

    /// <summary>控制内建 —— 收满参数后由求值器推控制帧</summary>
    private void Controls()
    {
        // if/while/foreach 不在这里——它们在 predefined.rav 用 Ravel 写(靠可调用的 true/false + callcc)
        DefControl("With", ControlKind.With, 2);
        DefControl("CallCC", ControlKind.CallCC, 1);
        // **模块加载状态**的拍 / 还原:引擎只管它自己这两样(`_loading` / `_loaded`),
        // handler 栈那种库的状态不在引擎视野里(见 predefined.rav 的 `callcc`)。
        // 错误交给谁:库注册一个钩子(引擎不认识 handler 栈),没人接时库调 Unhandled 交回引擎报告
        DefFn("SetErrorHook", FunctionVal.From(a => {
            _errorHook = a as FunctionVal
                ?? throw new RuntimeException($"SetErrorHook 要一个函数，得到 {a.Type}", ErrorKind.Argument);
            return VoidVal.Instance;
        }));
        DefFn("Unhandled", FunctionVal.From(Unhandled));
        DefFn("LoadingState", FunctionVal.From(_ => SnapshotLoading()));
        DefFn("RestoreLoading", FunctionVal.From(RestoreLoading));
        DefControl("Using", ControlKind.Using, 1);
        DefControl("Eval", ControlKind.Eval, 1);
    }

    /// <summary>打印与输入</summary>
    private static RuntimeValue Fs(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or OverflowException
                                   or NotSupportedException or System.Security.SecurityException)
        {
            throw new RuntimeException($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }

    /// <summary>ravel "M":切换到命名模块的作用域(首次访问时创建),后续语句落在该模块里。
    /// `ravel ""` 是**回全局作用域**——文档就是这么用的(`ravel "MyMath"` … `ravel ""` … 引用它)。</summary>
    private RuntimeValue EnterModule(string name)
    {
        // 空名字特判成「回全局」。不特判的话会建出一个**名字叫 "" 的模块**,
        // 后面的定义全落在那儿;而模块作用域只挂到 _global 上,所以那些定义
        // 从别的模块根本看不见:`ravel ""` 之后 `b := 2`,再 `ravel "A"` 就读不到 b。
        if (name.Length == 0)
        {
            SetAmbientScope(_global);
            return VoidVal.Instance;
        }

        if (!_modules.TryGetValue(name, out var mv))
        {
            var mt = BuiltinClasses.NewModuleClass(name, BuiltinClasses.Ravel);
            mv = new ModuleVal(mt, new Scope(_global));
            // 成员是 C# 造的那几个模块(Math),在这里填 —— 所以它们**要显式 ravel 才有**
            if (ModuleFillers.TryGetValue(name, out var fill)) fill(mv.Scope);
            _modules[name] = mv;
            _global.Define(name, mt, mv);
        }

        SetAmbientScope(mv.Scope);
        return VoidVal.Instance;
    }

    // ======================== 内置函数:方法 + 特性 ========================

    /// <summary>把一个方法登记成 `System` 里的内置函数:方法上挂 `[Sys("名字")]`,反射那趟自己捡。
    ///
    /// 规矩就这么几条:
    /// <list type="bullet">
    /// <item>方法**返回 `RuntimeValue`**、收 1~3 个 `RuntimeValue` —— 柯里化和"半成品"标记
    ///   都交给 `FunctionVal.From` 那几个重载,不用自己写;</item>
    /// <item>参数个数由**签名**定,特性里不写 arity(写重了迟早对不上);</item>
    /// <item>多半是**实例方法**(内置里有一半要动 `this`:错误钩子、模块加载状态、当前作用域…),
    ///   纯的那几条写成 `static` 也行 —— 两样都收。</item>
    /// </list>
    ///
    /// **类型别名 / 常量 / 控制内建不走这条路**(它们还在 `Types` / `Constants` / `Controls` 里
    /// 明着列):那三样是**数据**,一眼看全的一张表比撒在各处的声明好读也好查;
    /// 而函数是**代码**,一个方法一个家、写到哪个主题的文件里都行。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class SysAttribute(string name) : Attribute
    {
        /// <summary>`System.xxx` 那个名字(方法名只管给 C# 看,不必和它对上)</summary>
        public string Name { get; } = name;
    }

    /// <summary>扫出来的那张表。**静态的、扫一次**:反射不便宜,而解释器是每跑一次就新建一个的
    /// (每个用例、每次进 REPL 都是新的)。</summary>
    private static readonly List<(string Name, MethodInfo Method)> SysMethods = ScanSysMethods();

    private static List<(string Name, MethodInfo Method)> ScanSysMethods()
    {
        var found = new List<(string, MethodInfo)>();
        foreach (var m in typeof(Interpreter).GetMethods(
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.GetCustomAttribute<SysAttribute>() is not { } attr) continue;

            var ps = m.GetParameters();
            if (m.ReturnType != typeof(RuntimeValue) || ps.Length is < 1 or > 3 ||
                ps.Any(p => p.ParameterType != typeof(RuntimeValue)))
                throw new InvalidOperationException(
                    $"内置 '{attr.Name}'（{m.Name}）的签名不对:要返回 RuntimeValue、收 1~3 个 RuntimeValue");

            if (found.Any(f => f.Item1 == attr.Name))
                throw new InvalidOperationException($"内置 '{attr.Name}' 登记了两次（{m.Name}）");

            found.Add((attr.Name, m));
        }

        // **按名字排**:反射给的方法先后没有保证(取决定元数据顺序),而成员表是**按登记先后**的
        // —— `System.Fields ()` 打出来就是那个顺序。不排的话同一份源码在不同运行时上
        // 列出来的可能不一样,那是个没人会想到去查的坑。
        found.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return found;
    }

    /// <summary>把扫出来的全登记进 `System`。**每建一个解释器绑一次**:`CreateDelegate` 要
    /// 目标实例,而调用在热路上(每个 `print` 都过它)—— 不能留着 `MethodInfo.Invoke`。</summary>
    private void RegisterSysFns()
    {
        foreach (var (name, method) in SysMethods)
            DefFn(name, Bind(method));
        return;

        FunctionVal Bind(MethodInfo m)
        {
            var target = m.IsStatic ? null : this;
            // `Direct` 而不是 `From`:**少包一层**。`CreateDelegate` 出来的委托没法像
            // 编译器生成的闭包那样被 JIT 去虚化,再叠一层 lambda 就是白多一跳
            // (实测每次调用差 80ns —— 见 `FunctionVal.Direct` 上那段)。
            return m.GetParameters().Length switch
            {
                1 => FunctionVal.Direct(m.CreateDelegate<Func<RuntimeValue, RuntimeValue>>(target)),
                2 => FunctionVal.Direct(m.CreateDelegate<Func<RuntimeValue, RuntimeValue, RuntimeValue>>(target)),
                _ => FunctionVal.Direct(m.CreateDelegate<Func<RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue>>(target)),
            };
        }
    }
}
