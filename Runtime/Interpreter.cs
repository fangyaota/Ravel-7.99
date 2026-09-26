namespace Ravel.Runtime;

using System.IO;

public partial class Interpreter
{
    private readonly Scope _global;

    /// <summary>当前作用域——随执行动态变化(同步自帧栈)</summary>
    public Scope CurrentScope { get; set; }

    /// <summary>内建类型名 → ObjectVal 速查表</summary>
    /// <summary>创建解释器：注册内置、加载预定义模块</summary>
    public Interpreter()
    {
        _global = new Scope();
        CurrentScope = _global;
        BuiltinClasses.ResetUserTypes();   // 别让上一个 Interpreter 建的类漏进本实例的 Subtypes
        RegisterBuiltins();
        LoadPredefined();
    }

    private readonly Dictionary<string, ModuleVal> _modules = [];
    private readonly HashSet<string> _loaded = [];
    private readonly Stack<string> _loading = new();
    /// <summary>`unsafe ()` 标记过的作用域。core 字段只在「当前作用域往上走得到某个被标记的作用域」
    /// 时才放行。
    ///
    /// 从前这里是个只增不减的计数器(`UnsafeDepth++`,没有对应的 --),于是**任何一次**
    /// `unsafe ()` 之后整个程序余下的 core 检查全关掉:在某个无关函数里调一次
    /// ——哪怕它早就返回了——外部就能直接读核心字段,core 修饰符等于不存在。
    /// 按作用域标记才对得上「读写之前先 `unsafe ()`」这个用法:标记随调用帧走,
    /// 函数返回后自然失效。</summary>
    internal readonly HashSet<Scope> UnsafeScopes = [];

    /// <summary>当前作用域是否在被 `unsafe ()` 标记的范围内</summary>
    internal bool IsUnsafe
    {
        get
        {
            for (var s = CurrentScope; s != null; s = s.Parent)
                if (UnsafeScopes.Contains(s)) return true;
            return false;
        }
    }

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

    /// <summary>按名称解析类型 —— **只查作用域**(词法链,一直到 `_global`)。
    ///
    /// 类型名就是普通变量:`predefined.rav` 里 `int := System.Integer` 那一批。
    /// 所以这个名字查到的**必须是个类对象**,查到别的就报错。
    ///
    /// 注解**只收一个标识符**(`x: int`),写不了 `System.List` 那种点号路径 ——
    /// 所以内置名的别名必须先定义好,`predefined.rav` 自己的注解才有来源。
    /// 那个文件里 `references: list = []` 因此写在别名**之后**(位置是有讲究的,见那里的注释)。
    /// 从前它在第一行,于是这里不得不兜一张 15 条的内置类型表;现在不需要了。</summary>
    private ObjectVal ResolveType(string n, Scope? extra = null)
    {
        var v = (extra ?? CurrentScope).TryLookup(n) ?? throw new RuntimeException($"未知的类型: {n}");
        return v.Value is ObjectVal { IsClass: true } cls
            ? cls
            : throw new RuntimeException($"'{n}' 不是类型（它是个 {v.Value.Type}）");
    }

    /// <summary>Ravel 错误:抛 RuntimeException(路由到 Ex.throw 后续再做)</summary>
    internal void ThrowRavel(string msg) => throw new RuntimeException(msg);

    /// <summary>访问控制:private 仅本对象 scope;protected 额外允许子类实例 scope。无访问控制时直接放行</summary>
    internal bool CheckFieldAccess(Variable field, ObjectVal obj)
    {
        if (!field.HasAttr(Attr.Private) && !field.HasAttr(Attr.Protected)) return true;
        for (var cur = CurrentScope; cur != null; cur = cur.Parent)
        {
            if (cur == obj.Scope) return true;
            if (field.HasAttr(Attr.Protected))
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
