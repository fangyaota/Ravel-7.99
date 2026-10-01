namespace Ravel.Runtime;

using System.IO;

public partial class Interpreter
{
    private readonly Scope _global;

    /// <summary>当前作用域——随执行动态变化(同步自帧栈)</summary>
    public Scope CurrentScope { get; set; }

    /// <summary>脚本名**之后**那些命令行参数(`System.Args ()` 交回的就是它,复制成一份 list)。
    ///
    /// 解释器自己**不读命令行**:是谁把它跑起来的、命令行长什么样,那是 CLI 的事(见 Program.cs
    /// 的 RunFile)。所以 REPL 和 `ravel test` 里这份就是空的 —— 它们没有"脚本的参数"可言。</summary>
    public IReadOnlyList<string> ScriptArgs { get; }

    /// <summary>创建解释器：注册内置、加载预定义模块</summary>
    /// <param name="scriptArgs">脚本名之后那些参数;不给就是空的(REPL、测试运行器这么用)</param>
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

    /// <summary>从磁盘加载 predefined.rav（别名、导入标准库）。
    ///
    /// **找不到就报错**,不静默跳过:别名(`print` / `true` / `int` / …)和控制流全在那个文件里,
    /// 空着手往下跑的话,用户拿到的是「未定义的变量 'print'」—— 指不到真正的原因。</summary>
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

        throw new RuntimeException(
            "找不到 lib/predefined.rav（别名与控制流都在那里）。搜过:"
            + string.Join(" ", ModuleSearchPath.Defaults));
    }

    /// <summary>执行一个程序的全部语句，返回最后一条语句的值</summary>
    public RuntimeValue Interpret(Program p) => RunStack(p);

    /// <summary>访问控制:private 仅本对象 scope;protected 额外允许子类实例 scope。无访问控制时直接放行。
    ///
    /// 内置方法不带这两个属性(它们住在类的**实例表**里,谁都能从实例上读),所以这里
    /// 只管用户写在自己类里的字段 —— 判据就是"当前作用域在不在这个对象的类里"。</summary>
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

    /// <summary>这个值算不算 target 类型的值 —— **接口也算数**:名义继承链够不着时,再看当前作用域里
    /// 有没有生效中的实现把它接到 target 上(判据同 `x is T`,见 `BuiltinClasses.HasTrait`)。
    /// 类型检查(注解、参数)一律走这一条:接口不在继承链上,但"在这个作用域里实现了"就该收下。</summary>
    private bool Accepts(RuntimeValue value, ObjectVal target)
        => value.Type.IsAssignableTo(target) || BuiltinClasses.HasTrait(this, value.Type, target);

    /// <summary>把"接口也算数"那半递给 <see cref="Variable.CheckAssignable"/> / `Assign` 当补充判据。
    /// 是个委托:名义链够得着就不问它(接口那一问要走一遍作用域链)。</summary>
    private Func<ObjectVal, bool> ViaTrait(RuntimeValue value) => t => BuiltinClasses.HasTrait(this, value.Type, t);

    /// <summary>取 `by` 属性值上的 getter / setter。
    ///
    /// 标了 `by` 却没有那两个函数(值不是 `property g s` 造的)就是**用错了** —— 报错,
    /// 别静默原样交回:那样 `by v := 5` 读起来像个普通变量,`v = 1` 更是什么都不发生。
    /// 从前四条读写路各有各的兜底:`obj.v` 报「没有方法 'Get'」,而裸读 `v` **悄悄把那个
    /// 值交出去**、写回来还什么都不做 —— 同一个错误四个答案,现在只有一个。
    ///
    /// `owner` 是报错文案里的名字(变量名/字段名)。</summary>
    internal FunctionVal PropertyGetter(RuntimeValue prop, string owner)
        => (prop as PropertyVal)?.Getter ?? throw NotAProperty(prop, "getter", owner);

    internal FunctionVal PropertySetter(RuntimeValue prop, string owner)
        => (prop as PropertyVal)?.Setter ?? throw NotAProperty(prop, "setter", owner);

    /// <summary>那一**份** property 本身(不是它的 getter/setter)。
    ///
    /// 拿它做什么:接口那条槽交出去之前要**连 getter/setter 一起**绑到这一次的作用域上
    /// (见 `BuiltinClasses.Activate`),光取 getter 不够。`what` 只是报错文案里那个词,
    /// 和上面两条共用一套措辞(测试 138 钉着 `没有 getter` / `没有 setter` 两种)。</summary>
    internal PropertyVal SlotProperty(RuntimeValue prop, string owner, string what = "getter/setter")
        => prop as PropertyVal ?? throw NotAProperty(prop, what, owner);

    private static RuntimeException NotAProperty(RuntimeValue prop, string what, string owner)
        => new($"'{owner}' 标了 by，但它的值不是 property（{prop.Type} 上没有 {what}）");

    /// <summary>把内建函数的参数收成指定类型,否则报 Ravel 错误。
    /// 直接硬转会抛 C# 的 InvalidCastException,消息里全是 Ravel.Runtime.XXXVal。</summary>
    private static T As<T>(RuntimeValue v, string what) where T : RuntimeValue
        => v as T ?? throw new RuntimeException($"{what}需要{ArgNames.Of(typeof(T))}，得到 {v.Type}", ErrorKind.Type);

    /// <summary>值转字符串（Ravel 语义）</summary>
    private static string Show(RuntimeValue v) => v.ToString();
}
