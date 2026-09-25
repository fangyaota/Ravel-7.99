namespace Ravel.Runtime;

using System.IO;

public partial class Interpreter
{
    private readonly Scope _global;

    /// <summary>当前作用域——随执行动态变化(同步自帧栈)</summary>
    public Scope CurrentScope { get; set; }

    /// <summary>内建类型名 → RuntimeType 速查表</summary>
    private static readonly Dictionary<string, RuntimeType> TypeRegistry = new()
    {
        ["object"] = RuntimeType.Object,
        ["int"] = RuntimeType.Int,
        ["bool"] = RuntimeType.Bool,
        ["string"] = RuntimeType.String,
        ["function"] = RuntimeType.Function,
        ["list"] = RuntimeType.List,
        ["void"] = RuntimeType.Void,
        ["type"] = RuntimeType.Type,
        ["Any"] = RuntimeType.Any,
        ["float"] = RuntimeType.Float,
        ["bigint"] = RuntimeType.BigInt,
        ["fraction"] = RuntimeType.Fraction,
        ["bigfraction"] = RuntimeType.BigFraction,
        ["set"] = RuntimeType.Set,
        ["dict"] = RuntimeType.Dict,
    };

    /// <summary>创建解释器：注册内置、加载预定义模块</summary>
    public Interpreter()
    {
        _global = new Scope();
        CurrentScope = _global;
        RuntimeType.ResetUserTypes();   // 别让上一个 Interpreter 建的类漏进本实例的 Subtypes
        RegisterBuiltins();
        LoadPredefined();
    }

    private readonly Dictionary<string, ModuleVal> _modules = [];
    private readonly HashSet<string> _loaded = [];
    private readonly Stack<string> _loading = new();
    internal int UnsafeDepth;

    /// <summary>从磁盘加载 predefined.rav（别名、导入标准库）</summary>
    private void LoadPredefined()
    {
        foreach (var d in ModuleSearchPath.Defaults)
        {
            var p = Path.Combine(d, "predefined.rav");
            if (File.Exists(p))
            {
                RunStack(Parser.ParseSource(File.ReadAllText(p), p));
                return;
            }
        }
    }

    /// <summary>执行一个程序的全部语句，返回最后一条语句的值</summary>
    public RuntimeValue Interpret(Program p) => RunStack(p);

    /// <summary>按名称解析类型：先查作用域，再查类型注册表</summary>
    private RuntimeType ResolveType(string n, Scope? extra = null)
    {
        if (extra != null)
        {
            var v = extra.TryLookup(n);
            if (v?.Value is TypeVal tv) return tv.Value;
        }

        if (TypeRegistry.TryGetValue(n, out var t)) return t;
        var v2 = CurrentScope.TryLookup(n);
        if (v2?.Value is TypeVal tv2) return tv2.Value;
        throw new RuntimeException($"未知的类型: {n}");
    }

    /// <summary>Ravel 错误:抛 RuntimeException(路由到 Ex.throw 后续再做)</summary>
    internal void ThrowRavel(string msg) => throw new RuntimeException(msg);

    /// <summary>访问控制:private 仅本对象 scope;protected 额外允许子类实例 scope。无访问控制时直接放行</summary>
    internal bool CheckFieldAccess(Variable field, ObjectVal obj)
    {
        if (!field.HasAttr("private") && !field.HasAttr("protected")) return true;
        for (var cur = CurrentScope; cur != null; cur = cur.Parent)
        {
            if (cur == obj.Scope) return true;
            if (field.HasAttr("protected"))
            {
                var t = cur.TryLookup("this");
                if (t?.Value is ObjectVal o && o.ClassType.IsAssignableTo(obj.ClassType)) return true;
            }
        }

        return false;
    }

    /// <summary>把内建函数的参数收成指定类型,否则报 Ravel 错误。
    /// 直接硬转会抛 C# 的 InvalidCastException,消息里全是 Ravel.Runtime.XXXVal。</summary>
    private static T As<T>(RuntimeValue v, string what) where T : RuntimeValue
        => v as T ?? throw new RuntimeException($"{what}需要 {RavelName<T>()}，得到 {v.Type}");

    /// <summary>C# 值类型 → 报错文案里该说的 Ravel 类型名</summary>
    private static string RavelName<T>() => typeof(T) == typeof(StringVal) ? "string"
        : typeof(T) == typeof(IntVal) ? "int"
        : typeof(T) == typeof(BoolVal) ? "bool"
        : typeof(T) == typeof(ListVal) ? "list"
        : typeof(T) == typeof(DictVal) ? "dict"
        : typeof(T).Name;

    /// <summary>值转字符串（Ravel 语义）</summary>
    private static string Show(RuntimeValue v) => v.ToString();
}
