namespace Ravel.Runtime;

using System.IO;

public partial class Interpreter
{
    internal Scope GlobalScope => _global;

    internal bool SawScopedUse;

    internal int HasTraitMemoVersion = -1;
    internal readonly Dictionary<(ObjectVal Cls, ObjectVal Trait), bool> HasTraitMemo = [];

    private readonly Scope _global;

    public Scope CurrentScope { get; set; }

    public IReadOnlyList<string> ScriptArgs { get; }

    public bool WarnForgotCall { get; set; }

    public bool MoreControlFlow { get; set; }

    static Interpreter()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    private void RegisterBuiltins()
    {
        var module = ModuleVal.WithOwnTable("System", _global, this);
        SysModule.Fill(this, module.Scope);
        _modules["System"] = module;
        _global.Define("System", BuiltinClasses.Ravel, module);
    }

    public Interpreter(IEnumerable<string>? scriptArgs = null)
    {
        ScriptArgs = scriptArgs is null ? [] : [.. scriptArgs];
        _global = new Scope();
        CurrentScope = _global;
        BuiltinClasses.ResetUserTypes();   // 别让上一个 Interpreter 建的类漏进本实例的 Subtypes
        RegisterBuiltins();
        LoadPredefined();
    }

    private readonly Dictionary<string, ModuleVal> _modules = [];
    private readonly Dictionary<string, ModuleVal> _moduleByPath = [];
    private ModuleVal? _globalModule;
    private readonly Dictionary<string, List<string>> _refsByPath = [];
    private ModuleVal? _currentModule;
    private readonly HashSet<string> _loaded = [];
    private readonly Stack<string> _loading = new();
    internal readonly HashSet<Scope> UnsafeScopes = [];

    internal bool IsUnsafe
    {
        get
        {
            for (var s = CurrentScope; s != null; s = s.Parent)
                if (UnsafeScopes.Contains(s)) return true;
            return false;
        }
    }

    private static readonly Dictionary<(string Path, bool Flow), Program> _libPrograms = [];
    private static readonly Dictionary<(string Path, bool Flow), BlockExpr> _libBlocks = [];

    private static Program ParseLibSource(string path)
    {
        var text = File.ReadAllText(path);
        var flow = Parser.DeclaresMoreControlFlow(text);
        if (_libPrograms.TryGetValue((path, flow), out var hit)) return hit;
        return _libPrograms[(path, flow)] = Parser.ParseSource(text, path, flow);
    }

    private static BlockExpr ParseLibBlock(string path, bool commandLineFlow)
    {
        var text = File.ReadAllText(path);
        var flow = commandLineFlow || Parser.DeclaresMoreControlFlow(text);
        if (_libBlocks.TryGetValue((path, flow), out var hit)) return hit;
        return _libBlocks[(path, flow)] = Parser.ParseBlock(text, path, flow);
    }

    private void LoadPredefined()
    {
        foreach (var d in ModuleSearchPath.Defaults)
        {
            var p = Path.Combine(d, "predefined.rav");
            if (File.Exists(p))
            {
                RunStack(ParseLibSource(p));
                return;
            }
        }

        throw new RuntimeException(
            "找不到 lib/predefined.rav（别名与控制流都在那里）。搜过:"
            + string.Join(" ", ModuleSearchPath.Defaults), ErrorKind.Io);
    }

    public RuntimeValue Interpret(Program p)
    {
        if (WarnForgotCall || DeclaresWarnIn(p.Source)) ReportCompileWarnings(p.Statements, p.Source);
        // **当前解释器**:内置运算符的体是纯 C#,签名里写 `Interpreter` 头参的那几个
        // (`:`)通过这一格拿到"是哪一个解释器在跑" —— 运算符当值用(`x.: int`、`f := :.int`)
        // 走的是普通调用那条路,没有别的办法把 `this` 递进去。
        var prev = _current;
        _current = this;
        try { return RunStack(p); }
        finally { _current = prev; }
    }

    [System.ThreadStatic] private static Interpreter? _current;

    internal static Interpreter? Current => _current;

    private void ReportCompileWarnings(IReadOnlyList<Statement> statements, string? file)
    {
        foreach (var (line, col) in CompileWarnings.Report(statements, file))
            _warnedForgotCall.Add((file, line, col));
    }

    private static bool DeclaresWarnIn(string? path)
    {
        if (path is not { Length: > 0 } || path[0] == '<') return false;
        try
        {
            return File.Exists(path) && Parser.DeclaresWarn(File.ReadAllText(path));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal bool CheckFieldAccess(Variable field, ObjectVal obj)
    {
        if (!field.HasAttr(Attr.Private) && !field.HasAttr(Attr.Protected)) return true;
        for (var cur = CurrentScope; cur != null; cur = cur.Parent)
        {
            // 类体/方法体跑在 `BodyScope` 上,实例表不在词法链上 —— 那一层要连它的实例一起认
            // (不然 `private` 从"类自己的代码读得到"变成"谁都读不到")。
            if (cur == obj.Scope || cur.InstanceScope == obj.Scope) return true;
            if (field.HasAttr(Attr.Protected))
            {
                var t = cur.TryLookup(ObjectVal.ThisMember);
                if (t?.Value is ObjectVal o && o.ClassType.IsAssignableTo(obj.ClassType)) return true;
            }
        }

        return false;
    }

    private bool Accepts(RuntimeValue value, ObjectVal target)
        => BuiltinClasses.Matches(this, value, target);

    internal FunctionVal PropertyGetter(RuntimeValue prop, string owner)
        => (prop as PropertyVal)?.Getter ?? throw NotAProperty(prop, "getter", owner);

    internal FunctionVal PropertySetter(RuntimeValue prop, string owner)
        => (prop as PropertyVal)?.Setter ?? throw NotAProperty(prop, "setter", owner);

    internal PropertyVal SlotProperty(RuntimeValue prop, string owner, string what = "getter/setter")
        => prop as PropertyVal ?? throw NotAProperty(prop, what, owner);

    private static RuntimeException NotAProperty(RuntimeValue prop, string what, string owner)
        => new($"'{owner}' 标了 by，但它的值不是 property（{prop.Type} 上没有 {what}）");

    internal static T As<T>(RuntimeValue v, string what) where T : RuntimeValue
        => v as T ?? throw new RuntimeException($"{what}需要{ArgNames.Of(typeof(T))}，得到 {v.Type}", ErrorKind.Type);

    internal static string Show(RuntimeValue v) => v.ToString();
}
