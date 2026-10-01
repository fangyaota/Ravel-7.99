using System.Globalization;
using System.Security.Cryptography;

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
        var scope = module.Scope;

        void Def(string name, ObjectVal type, RuntimeValue value) => scope.Define(name, type, value);
        // 类对象自己就是那个值,不再包一层
        void DefType(string name, ObjectVal type) => Def(name, BuiltinClasses.Type, type);
        void DefFn(string name, FunctionVal fn) => Def(name, BuiltinClasses.Function, fn);
        void DefControl(string name, ControlKind kind, int arity) => DefFn(name, new ControlFunction(kind, arity, RList<RuntimeValue>.Empty));

        // ---- 类型(System.Integer 等权威名;小写别名在 predefined.rav) ----
        DefType("Integer", BuiltinClasses.Int);
        DefType("String", BuiltinClasses.String);
        DefType("Char", BuiltinClasses.Char);
        DefType("Bool", BuiltinClasses.Bool);
        DefType("Float", BuiltinClasses.Float);
        DefType("BigInteger", BuiltinClasses.BigInt);
        DefType("Fraction", BuiltinClasses.Fraction);
        DefType("BigFraction", BuiltinClasses.BigFraction);
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
        DefType("Json", BuiltinClasses.Json);

        // ---- 常量 ----
        Def("True", BuiltinClasses.Bool, new BoolVal(true));
        Def("False", BuiltinClasses.Bool, new BoolVal(false));
        // 特殊浮点值。和 True/False 同款:系统模块里的一个值,`predefined.rav` 给全局别名
        // (`-Inf` 不用另设,一元 `-` 对 Float 就是取负)
        Def("NaN", BuiltinClasses.Float, new FloatVal(double.NaN));
        Def("Inf", BuiltinClasses.Float, new FloatVal(double.PositiveInfinity));
        Def("Default", BuiltinClasses.Every, DefaultVal.Instance);

        // ---- 控制内建:收满参数后由求值器推控制帧 ----
        // if/while/foreach 不在这里——它们在 predefined.rav 用 Ravel 写(靠可调用的 true/false + callcc)
        DefControl("With", ControlKind.With, 2);
        DefControl("CallCC", ControlKind.CallCC, 1);
        // **模块加载状态**的拍 / 还原:引擎只管它自己这两样(`_loading` / `_loaded`),
        // handler 栈那种库的状态不在引擎视野里(见 predefined.rav 的 `callcc`)。
        // 错误交给谁:库注册一个钩子(引擎不认识 handler 栈),没人接时库调 Unhandled 交回引擎报告
        DefFn("SetErrorHook", FunctionVal.From(a => {
            _errorHook = a as FunctionVal ?? throw new RuntimeException($"SetErrorHook 要一个函数，得到 {a.Type}");
            return VoidVal.Instance;
        }));
        DefFn("Unhandled", FunctionVal.From(Unhandled));
        DefFn("LoadingState", FunctionVal.From(_ => SnapshotLoading()));
        DefFn("RestoreLoading", FunctionVal.From(RestoreLoading));
        DefControl("Using", ControlKind.Using, 1);
        DefControl("Eval", ControlKind.Eval, 1);

        // ---- 输出 ----
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

        // ---- 反射 / 作用域 ----
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

        // ---- 其他核心函数 ----
        // ── 随机数的"源头" ──
        // 引擎只造**一枚取数的函数**(`() => int`,范围 0 .. 2^30-1);"哪几台、怎么用"是库的事
        // (lib/random.rav:三台 —— 进程共享 / 带种子可复现 / 加密级 —— 各包一枚它,
        //  掷骰子 / 洗牌 / 抽样那些写成 `IRandom` 的默认实现)。
        //
        // 交回**函数**而不是新造一个值类型:和 `Cached` 一个路子("是个函数,不是要实例化的类型"),
        // 引擎面最小,也不必动类型树。范围由这一层保证(原生闭包自己 `Next` 出来的)。
        DefFn("NewRandom", FunctionVal.From(a => Fs("造随机源", () =>
        {
            var seed = As<IntVal>(a, "NewRandom 的种子").Value;
            return NumberSource(new Random(seed));
        })));
        // 进程共享那台(`Random.Shared`)
        DefFn("SharedRandom", FunctionVal.From(_ => NumberSource(Random.Shared)));
        // 原始随机字节(0..255):加密那台的原料,也留给"就是要字节"(拿它自己拼整数)的人
        DefFn("RandomBytes", FunctionVal.From(a => Fs("取随机字节", () =>
        {
            var n = As<IntVal>(a, "RandomBytes 的个数").Value;
            if (n < 0) throw new RuntimeException($"RandomBytes 的个数不能是负的，得到 {n}");
            var buf = new byte[n];
            RandomNumberGenerator.Fill(buf);
            return new ListVal([.. buf.Select(b => (RuntimeValue)new IntVal(b))]);
        })));

        static FunctionVal NumberSource(Random rng)
            => new NativeClosure("_", BuiltinClasses.Any, (_, _) => new IntVal(rng.Next(1 << 30)));

        DefFn("Property", FunctionVal.From((g, s) =>
        {
            if (g is not FunctionVal gf) throw new RuntimeException("property 需要 getter 函数");
            if (s is not FunctionVal sf) throw new RuntimeException("property 需要 setter 函数");
            return new PropertyVal(gf, sf);
        }));
        DefFn("Assert", FunctionVal.From((cond, msg) =>
        {
            if (cond is not BoolVal b) throw new RuntimeException("assert 需要 bool 参数");
            if (!b.Value) throw new RuntimeException(msg is StringVal s ? s.Value : "assertion failed: " + Show(cond));
            return VoidVal.Instance;
        }));
        // 参数**必须是字符串**:从前非字符串会退化成空消息,CLI 打出一个光秃秃的 `Error:` ——
        // `exit 0` 看起来像解释器坏了。要结束程序就写一条消息(`exit "bye"`)。
        DefFn("Exit", FunctionVal.From(a => throw new ExitException(As<StringVal>(a, "exit 的消息").Value)));
        DefFn("RavelMod", FunctionVal.From(a => EnterModule(As<StringVal>(a, "ravel 的模块名").Value)));

        // ---- 文件系统:只做 syscall,不做策略 ----
        // 失败就报 Ravel 错误(中文、带路径);"要不要先问一句"交给 FileExists / DirExists 那两个探针,
        // 它们**不报错**。路径基准 = 进程当前目录;不做沙箱 —— 和 `using` 找模块一个待遇,用户自己负责。
        // 上面那些预检查是为了消息说人话;**Fs 那层兜底是为了不让 C# 异常漏到顶层**
        // (目录不存在、没权限、路径里有非法字符…… 漏出去会绕过 Ravel 的 try 把程序打掉,
        //  和这批原语一条道理)。
        static byte[] ReadAllBytes(Stream s)
        {
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        // 外部命令的输出按什么编码解?——**先按 UTF-8 试,不合法就退回控制台编码**。
        // 两边都常见:git / python 那些吐 UTF-8,而 `dir` 这类走的是控制台那套(中文 Windows
        // 上就是 GBK)。合法的 UTF-8 里出现 GBK 字节的概率极低,所以这个判据够用。
        static string DecodeOutput(byte[] bytes)
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

        static string PathOf(RuntimeValue v, string what)
            => v is StringVal s ? s.Value : throw new RuntimeException($"{what} 需要一个路径字符串，得到 {v.Type}");

        static RuntimeValue Fs(string what, Func<RuntimeValue> body)
        {
            try
            {
                return body();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or OverflowException
                                       or NotSupportedException or System.Security.SecurityException)
            {
                throw new RuntimeException($"{what}失败: {ex.Message}");
            }
        }

        static void NeedFile(string p, string what)
        {
            if (Directory.Exists(p)) throw new RuntimeException($"{what}: 这是个目录，不是文件 —— {p}");
            if (!File.Exists(p)) throw new RuntimeException($"{what}: 找不到文件 —— {p}");
        }

        static void NeedParentDir(string p, string what)
        {
            // 父目录**按用户写的那串**切(最后一个分隔符之前),不转绝对路径、也不交给 .NET 归一化 ——
            // 报错里要看到的就是他写的那串:`.io-test/nodir`,而不是被换成反斜杠的版本,
            // 更不是带盘符的完整路径。就一个文件名(没有分隔符)时没有父目录可查,交给下面去管。
            var cut = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
            if (cut <= 0) return;
            var dir = p[..cut];
            if (!Directory.Exists(dir))
                throw new RuntimeException($"{what}: 目录不存在 —— {dir}");
        }

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

        // 删文件,或删**空**目录 —— 不提供递归删除(那是个危险默认值)。
        DefFn("DeletePath", FunctionVal.From(a => Fs("删除", () =>
        {
            var p = PathOf(a, "DeletePath");
            if (Directory.Exists(p))
            {
                if (Directory.EnumerateFileSystemEntries(p).Any())
                    throw new RuntimeException($"删除失败: 目录不是空的（不递归删）—— {p}");
                Directory.Delete(p);
                return VoidVal.Instance;
            }

            if (!File.Exists(p)) throw new RuntimeException($"删除失败: 找不到要删的东西 —— {p}");
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
            if (!Directory.Exists(p)) throw new RuntimeException($"列目录失败: 找不到目录 —— {p}");

            var entries = new Dictionary<RuntimeValue, RuntimeValue>();
            foreach (var d in Directory.EnumerateDirectories(p)) entries[new StringVal(Path.GetFileName(d))] = new BoolVal(true);
            foreach (var f in Directory.EnumerateFiles(p)) entries[new StringVal(Path.GetFileName(f))] = new BoolVal(false);
            return new DictVal(entries);
        })));

        // ── 跑外部命令 ──
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
                ?? throw new RuntimeException("跑命令失败: 进程起不来");
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
            if (Directory.Exists(dst)) throw new RuntimeException($"复制失败: 目标是目录 —— {dst}");
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
            if (!Directory.Exists(p)) throw new RuntimeException($"切目录失败: 找不到目录 —— {p}");
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

        // ── 时间 ──
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
                throw new RuntimeException($"ParseTime: 读不动 —— '{text}' 对不上格式 '{fmt}'");
            return new BigIntVal(new DateTimeOffset(t).ToUnixTimeMilliseconds());
        })));

        // ── 命令行参数与环境变量 ──
        // 和文件系统那层一个规矩:**只做 syscall,不做策略** —— 外面给进来什么就是什么,
        // "要不要先问一句"交给 `EnvOr`(不报错的那条)。变量名空着那种没用的情况当场拦下,
        // 免得 `System.SetEnv "" "x"` 静默变成一次什么都没做的调用。
        static string EnvName(RuntimeValue v, string what)
        {
            var name = As<StringVal>(v, $"{what} 的名字").Value;
            if (name.Length == 0) throw new RuntimeException($"{what}: 变量名不能是空的");
            return name;
        }

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
            if (v == null) throw new RuntimeException($"环境变量 '{name}' 没有（想给个默认值用 System.EnvOr）");
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

        return module;
    }

    /// <summary>把 Ravel 那个"毫秒数"收成 <see cref="DateTime"/>(本地时区)。
    /// 收 int 也收 bigint —— 手写 `Time 0` 那种小常量不该被迫写 `bigint 0`。</summary>
    private static DateTime LocalTime(RuntimeValue v, string what) => v switch
    {
        BigIntVal b => DateTimeOffset.FromUnixTimeMilliseconds((long)b.Value).LocalDateTime,
        IntVal i => DateTimeOffset.FromUnixTimeMilliseconds(i.Value).LocalDateTime,
        _ => throw new RuntimeException($"{what} 的毫秒数需要 bigint，得到 {v.Type}"),
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
