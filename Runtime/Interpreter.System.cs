using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Ravel.Runtime;

/// <summary>内置注册:建 System 模块,填入类型别名、控制内建和核心函数。
/// 加一个内置 = 在对应分组里加一行(DefType/DefFn/DefControl/Def)。</summary>
public partial class Interpreter
{
    private void RegisterBuiltins()
    {
        var systemModule = BuildSystemModule();
        _modules["System"] = systemModule;
        _global.Define("System", systemModule.Type, systemModule);
    }

    /// <summary>组装 System 模块——它是唯一「用 C# 写死」的模块,其余模块都来自 .rav 文件</summary>
    private ModuleVal BuildSystemModule()
    {
        var moduleType = BuiltinClasses.NewModuleClass("System", BuiltinClasses.Ravel);
        var module = new ModuleVal(moduleType, new Scope(_global));
        _sysScope = module.Scope;

        Types();
        Constants();
        Controls();
        Output();
        Reflection();
        Diagnostics();
        RegexPrimitives();
        RandomSources();
        Files();
        Commands();
        Time();
        Env();
        Net();

        _sysScope = null;
        return module;
    }

    /// <summary>建 `System` 模块期间"往哪张表登记" —— `BuildSystemModule` 起个头,
    /// 下面那一串 `Xxx ()` 各登记一段,建完置回 null。它们从前是同一个方法里的局部函数与语句,
    /// 靠闭包攥着这张表;拆成方法之后就得有个地方放它。</summary>
    private Scope? _sysScope;

    private void Def(string name, ObjectVal type, RuntimeValue value) => _sysScope!.Define(name, type, value);
    // 类对象自己就是那个值,不再包一层
    private void DefType(string name, ObjectVal type) => Def(name, BuiltinClasses.Type, type);
    private void DefFn(string name, FunctionVal fn) => Def(name, BuiltinClasses.Function, fn);
    private void DefControl(string name, ControlKind kind, int arity) => DefFn(name, new ControlFunction(kind, arity, RList<RuntimeValue>.Empty));

    // ── 下面几段共用的小工具(从前它们是某一段里的局部函数,拆开之后得放这儿)──

    private static byte[] ReadAllBytes(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string DecodeOutput(byte[] bytes)
    {
        try
        {
            return new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (System.Text.DecoderFallbackException)
        {
            return Console.OutputEncoding.GetString(bytes);
        }
    }

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
    private void Output()
    {
        DefFn("WriteLine", FunctionVal.From(a =>
        {
            Console.WriteLine(Show(a));
            return VoidVal.Instance;
        }));
        DefFn("Write", FunctionVal.From(a =>
        {
            Console.Write(Show(a));
            return VoidVal.Instance;
        }));
        DefFn("ReadLine", FunctionVal.From(_ => new StringVal(Console.ReadLine() ?? "")));

        // 标准错误:和 Write / WriteLine 一个样,只是走 Console.Error(报错报告、[outdated] 那些
        // 引擎自己的话也在那儿)。控制台当文件用时,`stderr` 那个文件落到这儿。
        DefFn("WriteErr", FunctionVal.From(a =>
        {
            Console.Error.Write(Show(a));
            return VoidVal.Instance;
        }));
        DefFn("WriteLineErr", FunctionVal.From(a =>
        {
            Console.Error.WriteLine(Show(a));
            return VoidVal.Instance;
        }));

        // 把标准输入读到 EOF(终端上要 Ctrl+Z / Ctrl+D 收)。`stdin` 那个文件的 `Read ()` 靠它 ——
        // 想读一行用 ReadLine(它是 `input`)。
        DefFn("ReadAllInput", FunctionVal.From(_ => new StringVal(Console.In.ReadToEnd())));
    }

    /// <summary>诊断开关 —— 引擎自己那几条"提醒一声但不拦你"的东西</summary>
    private void Diagnostics()
    {
        // 「是不是忘了调用?」:块里**不是最后一条**的语句,值求出来是个函数就在 stderr 上提醒一句
        // (为什么只管非最后一条、为什么默认关,见 Interpreter.WarnForgotCall)。
        // CLI 的 `--warn` 在第一条语句之前就把它打开;脚本里也能随时开/关 —— 比如只想盯住某一段:
        //
        //     System.WarnForgotCall true
        //     … 可疑的那几行 …
        //     System.WarnForgotCall false
        DefFn("WarnForgotCall", FunctionVal.From(a =>
        {
            WarnForgotCall = As<BoolVal>(a, "WarnForgotCall").Value;
            return VoidVal.Instance;
        }));
    }

    /// <summary>反射与作用域</summary>
    private void Reflection()
    {
        DefFn("TypeOf", FunctionVal.From(a => a.Type));
        // 接口实现:`use` 登记进**当前作用域**(随作用域在/不在),`impl` 登记进**全局作用域**
        // (每个作用域链都到全局,于是处处生效)。两个共用一套登记与查找,见 BuiltinClasses.Interfaces.cs
        DefFn("Use", FunctionVal.From(a => BuiltinClasses.Use(this, a, CurrentScope, "use")));
        DefFn("Impl", FunctionVal.From(a => BuiltinClasses.Use(this, a, _global, "impl")));
        DefFn("CurrentScope", FunctionVal.From(_ => new ScopeVal(CurrentScope)));
        // 标记**当前**作用域:core 检查沿作用域链往上找标记,所以函数返回后标记自然失效
        DefFn("Unsafe", FunctionVal.From(_ =>
        {
            UnsafeScopes.Add(CurrentScope);
            return VoidVal.Instance;
        }));
    }

    /// <summary>正则 —— 那六条的实现在 `Interpreter.Regex.cs`,策略在 `lib/regex.rav`</summary>
    private void RegexPrimitives()
    {
        RegisterRegexPrimitives(DefFn);
    }

    /// <summary>随机数的**源头** —— 引擎只造"一枚取数的函数",哪几台、怎么用是 `lib/random.rav` 的事</summary>
    private void RandomSources()
    {
        // 引擎只造**一枚取数的函数**(`() => int`,范围 0 .. 2^30-1);"哪几台、怎么用"是库的事
        // (lib/random.rav:三台 —— 进程共享 / 带种子可复现 / 加密级 —— 各包一枚它,
        //  掷骰子 / 洗牌 / 抽样那些写成 `IRandom` 的默认实现)。
        //
        // 交回**函数**而不是新造一个值类型:和 `Cached` 一个路子("是个函数,不是要实例化的类型"),
        // 引擎面最小,也不必动类型树。范围由这一层保证(原生闭包自己 `Next` 出来的)。
        DefFn("NewRandom", FunctionVal.From(a => Fs("造随机源", () =>
        {
            var seed = As<IntVal>(a, "NewRandom 的种子").Value;
            var rng = new Random(seed);
            return NumberSource(() => rng.Next(1 << 30));
        })));
        // 进程共享那台(`Random.Shared`)
        DefFn("SharedRandom", FunctionVal.From(_ => NumberSource(() => Random.Shared.Next(1 << 30))));
        // xoshiro256** —— **算法定死**的那台:同一个种子在哪个运行时、哪台机器上都给同一串
        // (`.NET` 的 `Random(seed)` 只保证同一个运行时里可复现)。写法见 Runtime/Xoshiro256.cs
        DefFn("XoshiroRandom", FunctionVal.From(a => Fs("造随机源", () =>
        {
            var seed = As<IntVal>(a, "XoshiroRandom 的种子").Value;
            var rng = new Xoshiro256(seed);
            return NumberSource(rng.Next30);
        })));
        // 原始随机字节(0..255):加密那台的原料,也留给"就是要字节"(拿它自己拼整数)的人
        DefFn("RandomBytes", FunctionVal.From(a => Fs("取随机字节", () =>
        {
            var n = As<IntVal>(a, "RandomBytes 的个数").Value;
            if (n < 0) throw new RuntimeException($"RandomBytes 的个数不能是负的，得到 {n}", ErrorKind.Value);
            var buf = new byte[n];
            RandomNumberGenerator.Fill(buf);
            return new ListVal([.. buf.Select(b => (RuntimeValue)new IntVal(b))]);
        })));

        static FunctionVal NumberSource(Func<int> next)
            => new NativeClosure("_", BuiltinClasses.Any, (_, _) => new IntVal(next()));

        DefFn("Property", FunctionVal.From((g, s) =>
        {
            if (g is not FunctionVal gf) throw new RuntimeException("property 需要 getter 函数", ErrorKind.Argument);
            if (s is not FunctionVal sf) throw new RuntimeException("property 需要 setter 函数", ErrorKind.Argument);
            return new PropertyVal(gf, sf);
        }));
        DefFn("Assert", FunctionVal.From((cond, msg) =>
        {
            if (cond is not BoolVal b)
                throw new RuntimeException("assert 需要 bool 参数", ErrorKind.Argument);
            if (!b.Value)
                throw new RuntimeException(msg is StringVal s ? s.Value : "assertion failed: " + Show(cond), ErrorKind.Assert);
            return VoidVal.Instance;
        }));
        // 参数**必须是字符串**:从前非字符串会退化成空消息,CLI 打出一个光秃秃的 `Error:` ——
        // `exit 0` 看起来像解释器坏了。要结束程序就写一条消息(`exit "bye"`)。
        DefFn("Exit", FunctionVal.From(a => throw new ExitException(As<StringVal>(a, "exit 的消息").Value)));
        DefFn("RavelMod", FunctionVal.From(a => EnterModule(As<StringVal>(a, "ravel 的模块名").Value)));
    }

    /// <summary>文件系统 —— **只做 syscall,不做策略**</summary>
    private void Files()
    {
        // 失败就报 Ravel 错误(中文、带路径);"要不要先问一句"交给 FileExists / DirExists 那两个探针,
        // 它们**不报错**。路径基准 = 进程当前目录;不做沙箱 —— 和 `using` 找模块一个待遇,用户自己负责。
        // 上面那些预检查是为了消息说人话;**Fs 那层兜底是为了不让 C# 异常漏到顶层**
        // (目录不存在、没权限、路径里有非法字符…… 漏出去会绕过 Ravel 的 try 把程序打掉,
        //  和这批原语一条道理)。

        // 外部命令的输出按什么编码解?——**先按 UTF-8 试,不合法就退回控制台编码**。
        // 两边都常见:git / python 那些吐 UTF-8,而 `dir` 这类走的是控制台那套(中文 Windows
        // 上就是 GBK)。合法的 UTF-8 里出现 GBK 字节的概率极低,所以这个判据够用。

        DefFn("FileExists", FunctionVal.From(a => Fs("FileExists", () => new BoolVal(File.Exists(PathOf(a, "FileExists"))))));
        DefFn("DirExists", FunctionVal.From(a => Fs("DirExists", () => new BoolVal(Directory.Exists(PathOf(a, "DirExists"))))));

        DefFn("ReadText", FunctionVal.From(a => Fs("读文件", () =>
        {
            var p = PathOf(a, "ReadText");
            NeedFile(p, "读文件");
            return new StringVal(File.ReadAllText(p));
        })));

        DefFn("WriteText", FunctionVal.From((a, b) => Fs("写文件", () =>
        {
            var p = PathOf(a, "WriteText");
            NeedParentDir(p, "写文件");
            File.WriteAllText(p, As<StringVal>(b, "WriteText 的内容").Value);
            return VoidVal.Instance;
        })));

        DefFn("AppendText", FunctionVal.From((a, b) => Fs("追加文件", () =>
        {
            var p = PathOf(a, "AppendText");
            NeedParentDir(p, "追加文件");
            File.AppendAllText(p, As<StringVal>(b, "AppendText 的内容").Value);
            return VoidVal.Instance;
        })));

        // 字节那两条:和文本那三条对称 —— 字节表就是 `Encoding` / `Random.Bytes` / `Bits` 用的
        // 那串 0..255。加它之前那些字节表只能在自己肚子里转,存不下来也读不回来。
        DefFn("ReadBytes", FunctionVal.From(a => Fs("读字节", () =>
        {
            var p = PathOf(a, "ReadBytes");
            NeedFile(p, "读字节");
            return BytesList(File.ReadAllBytes(p));
        })));

        DefFn("WriteBytes", FunctionVal.From((a, b) => Fs("写字节", () =>
        {
            var p = PathOf(a, "WriteBytes");
            NeedParentDir(p, "写字节");
            File.WriteAllBytes(p, BytesOf(b, "WriteBytes 的内容"));
            return VoidVal.Instance;
        })));

        // 删文件,或删**空**目录 —— 不提供递归删除(那是个危险默认值)。
        DefFn("DeletePath", FunctionVal.From(a => Fs("删除", () =>
        {
            var p = PathOf(a, "DeletePath");
            if (Directory.Exists(p))
            {
                if (Directory.EnumerateFileSystemEntries(p).Any())
                    throw new RuntimeException($"删除失败: 目录不是空的（不递归删）—— {p}", ErrorKind.Io);
                Directory.Delete(p);
                return VoidVal.Instance;
            }

            if (!File.Exists(p)) throw new RuntimeException($"删除失败: 找不到要删的东西 —— {p}", ErrorKind.Io);
            File.Delete(p);
            return VoidVal.Instance;
        })));

        DefFn("CreateDir", FunctionVal.From(a => Fs("建目录", () =>
        {
            Directory.CreateDirectory(PathOf(a, "CreateDir"));   // 父目录一并建;已存在不算错
            return VoidVal.Instance;
        })));

        DefFn("ListDir", FunctionVal.From(a => Fs("列目录", () =>
        {
            var p = PathOf(a, "ListDir");
            if (!Directory.Exists(p)) throw new RuntimeException($"列目录失败: 找不到目录 —— {p}", ErrorKind.Io);

            var entries = new Dictionary<RuntimeValue, RuntimeValue>();
            foreach (var d in Directory.EnumerateDirectories(p)) entries[new StringVal(Path.GetFileName(d))] = new BoolVal(true);
            foreach (var f in Directory.EnumerateFiles(p)) entries[new StringVal(Path.GetFileName(f))] = new BoolVal(false);
            return new DictVal(entries);
        })));
    }

    /// <summary>跑外部命令</summary>
    private void Commands()
    {
        // 走**系统 shell**(Windows 上是 `cmd.exe /c`,别处 `/bin/sh -c`):管道、重定向、通配符
        // 这些都归它管,我们不解析。**非零退出码不是错误** —— 程序失败是常事,原样放在
        // `code` 里交给调用方判断(真正起不来进程才是错)。
        //
        // 交回的是一张 dict:`out`(标准输出)/ `err`(标准错误)/ `code`(退出码)。
        // 两路输出**同时**抽走(各自一个线程)—— 顺序读会在大输出时死锁。
        DefFn("Cmd", FunctionVal.From(a => Fs("跑命令", () =>
        {
            var command = As<StringVal>(a, "cmd 的命令").Value;
            var windows = OperatingSystem.IsWindows();
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = windows ? "cmd.exe" : "/bin/sh",
                Arguments = (windows ? "/c " : "-c ") + command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new RuntimeException("跑命令失败: 进程起不来", ErrorKind.Io);
            var outTask = System.Threading.Tasks.Task.Run(() => ReadAllBytes(proc.StandardOutput.BaseStream));
            var errTask = System.Threading.Tasks.Task.Run(() => ReadAllBytes(proc.StandardError.BaseStream));
            proc.WaitForExit();

            var entries = new Dictionary<RuntimeValue, RuntimeValue>
            {
                [new StringVal("out")] = new StringVal(DecodeOutput(outTask.Result)),
                [new StringVal("err")] = new StringVal(DecodeOutput(errTask.Result)),
                [new StringVal("code")] = new IntVal(proc.ExitCode),
            };
            return new DictVal(entries);
        })));

        DefFn("PathSize", FunctionVal.From(a => Fs("看文件大小", () =>
        {
            var p = PathOf(a, "PathSize");
            NeedFile(p, "看文件大小");
            return new IntVal((int)new FileInfo(p).Length);
        })));

        DefFn("PathTime", FunctionVal.From(a => Fs("看修改时间", () =>
        {
            var p = PathOf(a, "PathTime");
            NeedFile(p, "看修改时间");
            return new StringVal(File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss"));
        })));

        DefFn("CopyPath", FunctionVal.From((a, b) => Fs("复制", () =>
        {
            var src = PathOf(a, "CopyPath");
            var dst = PathOf(b, "CopyPath");
            NeedFile(src, "复制");
            if (Directory.Exists(dst)) throw new RuntimeException($"复制失败: 目标是目录 —— {dst}", ErrorKind.Io);
            NeedParentDir(dst, "复制");
            File.Copy(src, dst, overwrite: true);
            return VoidVal.Instance;
        })));

        DefFn("MovePath", FunctionVal.From((a, b) => Fs("移动", () =>
        {
            var src = PathOf(a, "MovePath");
            var dst = PathOf(b, "MovePath");
            NeedFile(src, "移动");
            NeedParentDir(dst, "移动");
            File.Move(src, dst, overwrite: true);
            return VoidVal.Instance;
        })));

        DefFn("CurrentDir", FunctionVal.From(_ => Fs("读当前目录", () => new StringVal(Directory.GetCurrentDirectory()))));

        DefFn("ChDir", FunctionVal.From(a => Fs("切目录", () =>
        {
            var p = PathOf(a, "ChDir");
            if (!Directory.Exists(p)) throw new RuntimeException($"切目录失败: 找不到目录 —— {p}", ErrorKind.Io);
            Directory.SetCurrentDirectory(p);
            return VoidVal.Instance;
        })));

        // 按行切开。Ravel 的字符串是不透明的(只有 Length 和拼接),这事只能在这层做:
        // 按换行符切,顺手去掉每行末尾那个回车(Windows 的换行是回车+换行);
        // 末尾的空行不产出(文件最后有个换行是常态,不该多出一行空的),中间的空行保留。
        DefFn("SplitLines", FunctionVal.From(a => Fs("按行切", () =>
        {
            var text = As<StringVal>(a, "SplitLines 的内容").Value;
            var lines = new List<RuntimeValue>();
            if (text.Length > 0)
            {
                var parts = text.Split('\n');
                var last = parts.Length - 1;
                while (last > 0 && parts[last].Length == 0) last--;      // 末尾的空行不产出
                for (var i = 0; i <= last; i++)
                    lines.Add(new StringVal(parts[i].TrimEnd('\r')));
            }

            return new ListVal(lines);
        })));

        // 规整路径:去掉末尾的分隔符(`sub/` 和 `sub` 当同一个)。**不转绝对路径** ——
        // 组件里存的是用户给的那个,打印出来才是人看的(代价是中途 ChDir 会让早先的条目走样)。
        // `TrimEndingDirectorySeparator` 会保住根(`/`、`C:/`)。
        DefFn("PathClean", FunctionVal.From(a => Fs("规整路径", () =>
            new StringVal(Path.TrimEndingDirectorySeparator(PathOf(a, "PathClean"))))));

        // 路径函数:交给 .NET 的 Path(分隔符、盘符、`..` 这些自己写容易错)
        DefFn("PathJoin", FunctionVal.From((a, b) => Fs("拼路径", () =>
            new StringVal(Path.Combine(PathOf(a, "PathJoin"), PathOf(b, "PathJoin"))))));
        DefFn("PathDir", FunctionVal.From(a => Fs("取目录名", () =>
            new StringVal(Path.GetDirectoryName(PathOf(a, "PathDir")) ?? ""))));
        DefFn("PathBase", FunctionVal.From(a => Fs("取文件名", () =>
            new StringVal(Path.GetFileName(PathOf(a, "PathBase"))))));
        DefFn("PathExt", FunctionVal.From(a => Fs("取扩展名", () =>
            new StringVal(Path.GetExtension(PathOf(a, "PathExt"))))));
    }

    /// <summary>时间</summary>
    private void Time()
    {
        // 一个时刻就是"1970-01-01 00:00:00 UTC 起的**毫秒数**"(bigint —— 毫秒那个数量级 int 装不下)。
        // 原语只做换算,不做判断:显示成什么样、哪天算一周开头,都是库(lib/time.rav)的事。
        // 时区一律**本地**;格式串按 .NET 那一套(`yyyy-MM-dd HH:mm:ss`),这几条只负责转交。
        DefFn("NowMs", FunctionVal.From(_ => Fs("取当前时间", () =>
            new BigIntVal(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))));

        DefFn("TimeParts", FunctionVal.From(a => Fs("拆时间", () =>
        {
            var t = LocalTime(a, "TimeParts");
            return new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
            {
                [new StringVal("year")] = new IntVal(t.Year),
                [new StringVal("month")] = new IntVal(t.Month),
                [new StringVal("day")] = new IntVal(t.Day),
                [new StringVal("hour")] = new IntVal(t.Hour),
                [new StringVal("minute")] = new IntVal(t.Minute),
                [new StringVal("second")] = new IntVal(t.Second),
                [new StringVal("millisecond")] = new IntVal(t.Millisecond),
                // 0 = 周日(和 .NET 的 DayOfWeek 一个口径;翻成中文名是库那边的事)
                [new StringVal("weekday")] = new IntVal((int)t.DayOfWeek),
            });
        })));

        DefFn("MakeTime", FunctionVal.From(a => Fs("拼时间", () =>
        {
            var d = As<DictVal>(a, "MakeTime 的 parts");
            int Part(string k, int dflt) =>
                d.Entries.TryGetValue(new StringVal(k), out var v) && v is IntVal i ? i.Value : dflt;
            var t = new DateTime(Part("year", 1970), Part("month", 1), Part("day", 1),
                                 Part("hour", 0), Part("minute", 0), Part("second", 0),
                                 DateTimeKind.Local).AddMilliseconds(Part("millisecond", 0));
            return new BigIntVal(new DateTimeOffset(t).ToUnixTimeMilliseconds());
        })));

        // 睡一会儿。0 或负数就是"什么都不等"(`retries` 退避算出来是 0 很正常,别为这个报错),
        // 拿它限速也是这个用法 —— 「别把人家服务器打疼了」。
        DefFn("Sleep", FunctionVal.From(a => Fs("等待", () =>
        {
            var ms = BuiltinClasses.IntArg(a, "Sleep 的毫秒数");
            if (ms > 0) Thread.Sleep(ms);
            return VoidVal.Instance;
        })));

        DefFn("FormatTime", FunctionVal.From((a, b) => Fs("格式化时间", () =>
            new StringVal(LocalTime(a, "FormatTime").ToString(
                As<StringVal>(b, "FormatTime 的格式串").Value, CultureInfo.InvariantCulture)))));

        DefFn("ParseTime", FunctionVal.From((a, b) => Fs("解析时间", () =>
        {
            var text = As<StringVal>(a, "ParseTime 的文本").Value;
            var fmt = As<StringVal>(b, "ParseTime 的格式串").Value;
            // 用 TryParseExact 而不是 Parse:FormatException 不在 Fs 的白名单里,
            // 漏出去就是"解释器内部错误"、Ravel 的 try 接不住(和 Json 那边同款说明)
            if (!DateTime.TryParseExact(text, fmt, CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out var t))
                throw new RuntimeException($"ParseTime: 读不动 —— '{text}' 对不上格式 '{fmt}'", ErrorKind.Value);
            return new BigIntVal(new DateTimeOffset(t).ToUnixTimeMilliseconds());
        })));
    }

    /// <summary>命令行参数与环境变量</summary>
    private void Env()
    {
        // 和文件系统那层一个规矩:**只做 syscall,不做策略** —— 外面给进来什么就是什么,
        // "要不要先问一句"交给 `EnvOr`(不报错的那条)。变量名空着那种没用的情况当场拦下,
        // 免得 `System.SetEnv "" "x"` 静默变成一次什么都没做的调用。

        // `Args ()` 给的是**脚本名之后**那些参数,由 CLI 填进来(见 Program.cs 的 RunFile)。
        // 解释器自己不读命令行 —— 不这么切的话,`dotnet out/ravel.dll a.rav` 里第一个参数
        // 就变成了解释器自己的路径,脚本作者还得自己去认那几个。
        DefFn("Args", FunctionVal.From(_ =>
            new ListVal([.. ScriptArgs.Select(a => (RuntimeValue)new StringVal(a))])));

        // 没有这个变量就报错(要默认值用 EnvOr)。**空串不算"有"** —— .NET 那边
        // `SetEnvironmentVariable` 收空串就是把这变量删掉,所以 `SetEnv "X" ""` 之后
        // `Env "X"` 照样报错(要"空着的值"这个状态,环境变量这套里本来也存不下)。
        DefFn("Env", FunctionVal.From(a => Fs("读环境变量", () =>
        {
            var name = EnvName(a, "Env");
            var v = Environment.GetEnvironmentVariable(name);
            if (v == null) throw new RuntimeException($"环境变量 '{name}' 没有（想给个默认值用 System.EnvOr）", ErrorKind.Key);
            return new StringVal(v);
        })));

        DefFn("EnvOr", FunctionVal.From((a, b) => Fs("读环境变量", () =>
        {
            var name = EnvName(a, "EnvOr");
            var dflt = As<StringVal>(b, "EnvOr 的默认值").Value;
            return new StringVal(Environment.GetEnvironmentVariable(name) ?? dflt);
        })));

        DefFn("SetEnv", FunctionVal.From((a, b) => Fs("设环境变量", () =>
        {
            var name = EnvName(a, "SetEnv");
            var value = As<StringVal>(b, "SetEnv 的值").Value;
            // 只动**本进程**那份(.NET 还能写用户级/机器级的 —— 那是装环境,不该由一句赋值顺手做掉)。
            // 新起的子进程(`cmd` / `Io` 里那些)照常看得见。
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
            return VoidVal.Instance;
        })));

        DefFn("UnsetEnv", FunctionVal.From(a => Fs("删环境变量", () =>
        {
            Environment.SetEnvironmentVariable(EnvName(a, "UnsetEnv"), null, EnvironmentVariableTarget.Process);
            return VoidVal.Instance;
        })));

        // 全部环境变量:名字 → 值。**按名字排序** —— 进程那本字典的先后随机器而定,
        // 排一下打印出来才可期待(和 `Io.Dir.List ()` 那边同一个理由)。
        DefFn("EnvAll", FunctionVal.From(_ =>
        {
            var entries = new Dictionary<RuntimeValue, RuntimeValue>();
            foreach (var n in Environment.GetEnvironmentVariables().Keys.Cast<string>().OrderBy(n => n, StringComparer.Ordinal))
                entries[new StringVal(n)] = new StringVal(Environment.GetEnvironmentVariable(n) ?? "");
            return new DictVal(entries);
        }));
    }

    // ======================== 网络 ========================

    /// <summary>网络 —— 只做到 HTTP 这一层(裸 TCP 以后再说)。
    ///
    /// 和文件系统那批一个规矩:**只做 syscall,不做策略** —— URL 怎么拼、响应怎么解、
    /// 重试几次、重定向跟不跟,全是 `lib/http.rav` 的事(和正则、随机数一个路子)。
    /// 四条原语都**收一个 dict、交回一个 dict**:加字段不用改签名,而且以后加并发时,
    /// "发请求"整个变成一次**挂起点**,库里那些 `Http.Get` 一个字都不用改。
    ///
    /// 请求 dict 认这些键(缺的都走默认):
    /// `url` / `method`(默认 GET)/ `headers`(名字→值,值也能给 list)/ `body`(字节表)/
    /// `bodyFile`(从文件流式发)/ `follow`(默认 true)/ `timeout`(毫秒,默认 30 秒)/
    /// `max`(响应正文封顶,默认 16 MB)。
    /// 交回:`status` / `reason` / `headers`(小写名→值,重复的用 `, ` 连起来)/ `body`(字节表)/
    /// `url`(跟完重定向之后那个)。下载、上传那两条把 `body` 换成 `bytes`(写出去多少字节)。</summary>
    private void Net()
    {
        DefFn("HttpReq", FunctionVal.From(a => Http("发请求", () => Send(As<DictVal>(a, "HttpReq 的请求"), null))));

        DefFn("HttpDownload", FunctionVal.From(a => Http("下载", () =>
        {
            var req = As<DictVal>(a, "HttpDownload 的请求");
            var path = OptText(req, "path", "");
            NeedParentDir(path, "下载");
            // 边收边落盘:正文**不进 Ravel 堆** —— 下载一个 500 MB 的东西,堆里不该多一个字节表
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
            return Send(req, file);
        })));

        DefFn("HttpUpload", FunctionVal.From(a => Http("上传", () =>
        {
            var req = As<DictVal>(a, "HttpUpload 的请求");
            var path = OptText(req, "path", "");
            NeedFile(path, "上传的文件");
            return Send(req, null, path, OptText(req, "field", "file"));
        })));

        // 字节 → 文本。**认不出来的字符集不报错**、按 UTF-8 解:网上写着 `charset=utf8`、
        // `charset=UTF8`、甚至拼错的大把,为一个只影响显示的字段把整次请求打断不值当。
        DefFn("DecodeText", FunctionVal.From((a, b) => Fs("解码文本", () =>
            new StringVal(Decode(BytesOf(a, "DecodeText 的字节"), (As<StringVal>(b, "DecodeText 的字符集")).Value)))));
    }

    /// <summary>进程级共享的那一台 —— **别每次请求 new 一个**:连接池挂在它身上,
    /// 一次请求一台的话连接永远复用不上,请求一多就把本机端口耗光(.NET 上最经典的那个坑)。
    ///
    /// 超时**不设在这台身上**(设了就是全进程一把尺子),每个请求自己用 CancellationToken 管。
    /// 两台只差一件事:跟不跟重定向 —— 那是**处理器**(handler)级的开关,没法按请求改,
    /// 所以宁可养两台也不给用户一个"说是不跟、其实跟了"的 `follow`。</summary>
    private static readonly HttpClient Web = MakeWeb(true);
    private static readonly HttpClient WebNoRedirect = MakeWeb(false);

    private static HttpClient MakeWeb(bool follow) => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = follow,
        MaxAutomaticRedirections = 10,
        AutomaticDecompression = DecompressionMethods.All,   // gzip / deflate / br 全让 .NET 顺手解开
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>发出去、等回来。`bodyOut` 不是 null 时把正文**流式写进它**(下载那条路),
    /// `uploadPath` 不是 null 时把那个文件**流式发出去**(multipart,上传那条路)。</summary>
    private static RuntimeValue Send(DictVal req, Stream? bodyOut, string? uploadPath = null, string field = "file")
    {
        var url = OptText(req, "url", "");
        if (url.Length == 0) throw new RuntimeException("请求 dict 里没有 url", ErrorKind.Argument);
        // 网址**先自己验一遍**:`HttpRequestMessage` 对"这是个相对 URI"是构造得出来的,
        // 那要等到发的时候才报,而那时的话是 .NET 说的(还跟着系统语言变)。
        // 这里挡下来,报的是我们自己那句,用例也钉得住。
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) ||
            (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            throw new RuntimeException($"不是个能发的网址（要 http:// 或 https:// 开头）：{url}", ErrorKind.Value);

        var what = OptText(req, "method", uploadPath is null ? "GET" : "POST");
        var timeout = OptInt(req, "timeout", DefaultTimeoutMs);
        var max = OptInt(req, "max", 0) is var m && m > 0 ? m : DefaultMaxBytes;

        using var msg = new HttpRequestMessage(new HttpMethod(what), target);
        if (Opt(req, "headers") is DictVal hs) ApplyHeaders(msg, hs);
        if (uploadPath is { } up)
        {
            var form = new MultipartFormDataContent();
            form.Add(new StreamContent(File.OpenRead(up)), field, Path.GetFileName(up));
            msg.Content = form;
        }
        else if (Opt(req, "bodyFile") is StringVal bf)
        {
            NeedFile(bf.Value, "上传的文件");
            msg.Content = new StreamContent(File.OpenRead(bf.Value));
        }
        else if (Opt(req, "body") is { } body)
        {
            msg.Content = new ByteArrayContent(BytesOf(body, "请求正文"));
        }

        using var cts = new CancellationTokenSource(timeout <= 0 ? Timeout.Infinite : timeout);
        var client = OptBool(req, "follow", true) ? Web : WebNoRedirect;
        try
        {
            // ResponseHeadersRead:正文**边到边收**,不先在内存里攒成一大坨
            using var resp = client.Send(msg, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var entries = new Dictionary<RuntimeValue, RuntimeValue>
            {
                [new StringVal("status")] = new IntVal((int)resp.StatusCode),
                [new StringVal("reason")] = new StringVal(resp.ReasonPhrase ?? ""),
                [new StringVal("headers")] = HeaderDict(resp),
                // 跟完重定向之后真正停在哪儿 —— 短链、跳登录页这些一眼看得出来
                [new StringVal("url")] = new StringVal(resp.RequestMessage?.RequestUri?.ToString() ?? url),
            };
            if (bodyOut is null)
            {
                using var stream = resp.Content.ReadAsStream(cts.Token);
                entries[new StringVal("body")] = BytesList(ReadCapped(stream, max, url));
            }
            else
            {
                using var stream = resp.Content.ReadAsStream(cts.Token);
                entries[new StringVal("bytes")] = BuiltinClasses.Narrow(CopyTo(stream, bodyOut, cts.Token), "下载的字节数");
            }

            return new DictVal(entries);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new RuntimeException($"{what} {url} 超时（{timeout} 毫秒没回来）", ErrorKind.Io);
        }
        catch (HttpRequestException ex)
        {
            // 连不上、域名解析不了、证书不对…… 都落这儿。消息里带上 URL:批量抓的时候才知道是哪一个
            throw new RuntimeException($"{what} {url} 失败: {Why(ex)}", ErrorKind.Io);
        }
    }

    /// <summary>网络错误的那句中文。**不能直接用 `ex.Message`**:那是 .NET 跟着系统语言给的
    /// (中文机器上是一种说法、英文机器上又一种),而报错消息是要被 golden 用例**钉住**的。
    /// 认不出来的照样把原文带上 —— 总比一句"失败了"强。</summary>
    private static string Why(HttpRequestException ex) => ex.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => "域名解析不了",
        HttpRequestError.ConnectionError when ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }
            => "连接被拒绝（对面那个端口没人听）",
        HttpRequestError.ConnectionError => "连不上（网络不通，或者地址/端口不对）",
        HttpRequestError.SecureConnectionError => "TLS 握手没过（证书不对？）",
        HttpRequestError.InvalidResponse => "对面回的响应读不动",
        _ => ex.Message,
    };

    /// <summary>请求头。名字撞上"内容头"(`content-type` 这类)时要加在 Content 那半边 ——
    /// 加错地方 .NET 会抛「不能把 content header 加到 request headers 上」,而那是个
    /// 用起来很意外的错误。</summary>
    private static void ApplyHeaders(HttpRequestMessage msg, DictVal headers)
    {
        foreach (var (k, v) in headers.Entries)
        {
            var name = k as StringVal ?? throw new RuntimeException($"请求头的名字要是字符串，得到 {k.Type}", ErrorKind.Type);
            var values = (v is ListVal l ? l.Elements : [v]).Select(x => Show(x)).ToArray();
            if (!msg.Headers.TryAddWithoutValidation(name.Value, values))
            {
                msg.Content ??= new ByteArrayContent([]);
                msg.Content.Headers.TryAddWithoutValidation(name.Value, values);
            }
        }
    }

    /// <summary>响应头:响应那一层 + 内容那一层合成一张。名字**一律小写**(HTTP 头不区分大小写,
    /// 给两套写法只会让查的人猜);同名的多个值用 `, ` 连起来(和 HTTP 自己的规矩一致)。</summary>
    private static DictVal HeaderDict(HttpResponseMessage resp)
    {
        var merged = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Take(IEnumerable<KeyValuePair<string, IEnumerable<string>>> src)
        {
            foreach (var (name, values) in src)
            {
                var key = name.ToLowerInvariant();
                if (!merged.TryGetValue(key, out var list)) merged[key] = list = [];
                list.AddRange(values);
            }
        }

        Take(resp.Headers);
        Take(resp.Content.Headers);

        return new DictVal(merged.ToDictionary(
            kv => (RuntimeValue)new StringVal(kv.Key),
            kv => (RuntimeValue)new StringVal(string.Join(", ", kv.Value))));
    }

    /// <summary>读正文,超过 `cap` 就停下报错。不封顶的话一个大文件会先在堆里攒成字节表,
    /// 而堆是有上限的(测试里 256 MB)—— 那种时候该走 `Http.Download`。</summary>
    private static byte[] ReadCapped(Stream s, long cap, string url)
    {
        var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (ms.Length + n > cap)
                throw new RuntimeException(
                    $"响应太大（超过 {Human(cap)}）：{url} —— 大文件用 `Http.Download` 直接落盘", ErrorKind.Io);
            ms.Write(buffer, 0, n);
        }

        return ms.ToArray();
    }

    /// <summary>一个字节数怎么说人话(报错里用)。默认那 16 MB 不能印成 16777216。
    /// (和 `Format.Bytes` 那个不一样:这儿是**上限**、不是给人看的文件大小,整数就够)</summary>
    private static string Human(long n)
        => n >= 1024 * 1024 ? $"{n / (1024 * 1024)} MB"
         : n >= 1024 ? $"{n / 1024} KB"
         : $"{n} 字节";

    /// <summary>正文直接抄进别的流(下载),交回抄了多少字节 —— 中途不经过内存里那张字节表</summary>
    private static long CopyTo(Stream from, Stream to, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            to.Write(buffer, 0, n);
            total += n;
        }

        return total;
    }

    /// <summary>字节表(list,0..255)→ byte[]。元素不是字节就当场说清楚是**第几个**不对</summary>
    internal static byte[] BytesOf(RuntimeValue v, string what)
    {
        if (v is not ListVal l)
            throw new RuntimeException($"{what}需要一个字节表（list），得到 {v.Type}", ErrorKind.Type);

        var bytes = new byte[l.Elements.Count];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (l.Elements[i] is not IntVal n || n.Value is < 0 or > 255)
                throw new RuntimeException($"{what}的第 {i} 个不是字节（要在 0..255 里，得到 {l.Elements[i]}）", ErrorKind.Value);
            bytes[i] = (byte)n.Value;
        }

        return bytes;
    }

    private static ListVal BytesList(byte[] bytes) => new([.. bytes.Select(b => (RuntimeValue)new IntVal(b))]);

    /// <summary>按字符集解字节。GBK 那一批要 `CodePagesEncodingProvider` 才认得
    /// (见静态构造函数);认不出来的名字退回 UTF-8,不报错。</summary>
    private static string Decode(byte[] bytes, string charset)
    {
        if (charset.Length == 0) return LenientUtf8.GetString(bytes);
        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"', '\'')).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return LenientUtf8.GetString(bytes);
        }
    }

    /// <summary>宽松 UTF-8:坏字节变成 U+FFFD,不抛异常、也不静默丢掉 ——
    /// 网页上出现个把乱码不值得把整次请求打断。</summary>
    private static readonly UTF8Encoding LenientUtf8 = new(false, throwOnInvalidBytes: false);

    static Interpreter()
    {
        // 老编码(GBK / GB2312 / Big5…)在 .NET Core 上要显式注册才认 —— 中文网页常见,
        // 不注册的话 `charset=gbk` 会静默退回 UTF-8,整页乱码还找不到原因
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private const int DefaultTimeoutMs = 30_000;
    private const int DefaultMaxBytes = 16 * 1024 * 1024;

    private static RuntimeValue? Opt(DictVal d, string key)
        => d.Entries.TryGetValue(new StringVal(key), out var v) && v is not VoidVal ? v : null;

    private static string OptText(DictVal d, string key, string dflt) => Opt(d, key) is StringVal s ? s.Value : dflt;
    private static int OptInt(DictVal d, string key, int dflt) => Opt(d, key) is IntVal i ? i.Value : dflt;
    private static bool OptBool(DictVal d, string key, bool dflt) => Opt(d, key) is BoolVal b ? b.Value : dflt;

    /// <summary>网络那一批的兜底:兜住的比 <see cref="Fs"/> 宽 —— 网络能出的岔子
    /// (DNS、连接被拒、TLS、坏 URL)本来就不是 `IOException` 那一族,漏出去会把程序打掉。</summary>
    private static RuntimeValue Http(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or InvalidOperationException
                                   or System.Security.SecurityException or System.Net.Sockets.SocketException)
        {
            throw new RuntimeException($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }

    /// <summary>把一类 C# 异常兜成 Ravel 错误 —— 文件 / 进程 / 正则那批原语共用。
    ///
    /// **不让 C# 异常漏到顶层**是这批原语一条老规矩:漏出去会绕过 Ravel 的 `try`
    /// 把程序打掉(从前 `randint` 那条注释里说的也是这个)。这里是它唯一的家 ——
    /// 从前它是 `BuildSystemModule` 里的局部静态函数,正则那批(另一个 partial 文件)
    /// 也要用,就挪上来了。</summary>
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

    /// <summary>把 Ravel 那个"毫秒数"收成 <see cref="DateTime"/>(本地时区)。
    /// 收 int 也收 bigint —— 手写 `Time 0` 那种小常量不该被迫写 `bigint 0`。</summary>
    private static DateTime LocalTime(RuntimeValue v, string what) => v switch
    {
        BigIntVal b => DateTimeOffset.FromUnixTimeMilliseconds((long)b.Value).LocalDateTime,
        IntVal i => DateTimeOffset.FromUnixTimeMilliseconds(i.Value).LocalDateTime,
        _ => throw new RuntimeException($"{what} 的毫秒数需要 bigint，得到 {v.Type}", ErrorKind.Type),
    };

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
}
