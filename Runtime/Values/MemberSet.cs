namespace Ravel.Runtime.Values;

using System.Reflection;

/// <summary>一个类"给它的实例准备什么"：一张**名字单** + 按名字造一格的能力。
///
/// 从前这份东西摊在 `BuiltinClasses` 的一张外置注册表里(`Dictionary&lt;ObjectVal, …&gt;`),
/// 类对象自己不知道它有哪些成员 —— 于是"问我是谁要定义"没法实现,只能问外面那张表。
/// 现在每个类自己带着它(见 `ClassVal.Members`),`VirtualScope` 读不到时问的就是**类型自己**。
///
/// 它不是表:不存 `Variable`,只存"怎么造"(`Create`)。造出来的那一格进**值的表**
/// (`VirtualScope._made`),同一个值读第二次不再造;不同的值各自一格 —— 这正是
/// "一个对象一张表"的意思。</summary>
internal interface IMemberSet
{
    /// <summary>这一类**本来就有**哪些名字(不管造没造出来)。顺序稳定:自己的在前、父类的在后。</summary>
    IEnumerable<string> Names { get; }

    /// <summary>构造器 —— 类体那条 `init := …` 的 caster。没有就 null。
    /// 值类型还要把它当 `init` 成员交出去(`(5).init "12"`),对象不走这条(类体跑过了)。</summary>
    FunctionVal? Ctor { get; }

    /// <summary>按名字造一格。这个名字不归我就 null,让查找继续(父类那份、再到 null)。</summary>
    Variable? Create(ClassVal owner, string name);
}

/// <summary>一条成员的形状 —— 只影响"造出来那格长什么样",不影响怎么调。</summary>
internal enum MemberKind
{
    /// <summary>`[ClassMethod]`:普通方法,`(self, arg)`。</summary>
    Method,
    /// <summary>`[ClassOp]`:运算符,`(左, 右)` —— 第一格就是接收者。</summary>
    Operator,
    /// <summary>`[ClassProperty]`:一个名字两个方向,落 `by` 槽。</summary>
    Property,
}

/// <summary>**反射出来的一组成员** —— 静态方法上挂 `[ClassMethod]` / `[ClassOp]` /
/// `[ClassProperty]` / `[ClassCtor]`,和插件那套(`PluginApi`)是**同一批特性、同一个绑定**
/// (`ClassRegistry.Bind`)。内置类和插件类从此没有第二种写法。
///
/// 继承照 Ravel 的类树走:`StringMembers` 的父是 `ObjectMembers`,名字和造法都按
/// "自己的在前、父类的在后"接上去 —— 只接一层句柄(`_parent`),不拷表。
/// 反射**只做一次**(建成员集的时候),之后每次造一格都是字典命中 + 建一个 `Variable`。</summary>
internal sealed class MemberSet : IMemberSet
{
    private readonly record struct Spec(MethodInfo Method, MemberKind Kind, bool ReadOnly);

    private readonly Dictionary<string, Spec> _own = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly IMemberSet? _parent;

    private MemberSet(IMemberSet? parent) => _parent = parent;

    public FunctionVal? Ctor { get; private set; }

    /// <summary>扫一个**装载器类**:里面那些标了特性的静态方法就是这个类型的成员。
    /// 自己也标了特性的 C# 基类一并扫(从最具体的往下,子类先声明的那条赢)。</summary>
    /// <param name="parent">Ravel 那边的父类成员(比如 `StringMembers` 的 `ObjectMembers`)。
    /// 给了就按"自己 → 父"接上去,不给就是这一支的顶。</param>
    internal static MemberSet Scan(Type spec, IMemberSet? parent = null)
    {
        var set = new MemberSet(parent);
        for (var t = spec; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public
                                         | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.GetCustomAttribute<ClassCtorAttribute>() is not null)
                {
                    set.Ctor ??= FunctionVal.From(arg => ClassRegistry.Call(m, [arg]));
                    continue;
                }

                var (name, kind, readOnly) = Classify(m);
                if (name is null || !set._own.TryAdd(name, new Spec(m, kind, readOnly))) continue;
                set._order.Add(name);
            }
        }

        return set;
    }

    /// <summary>把几份成员集**接成一条**:先问第一份,没有再问第二份……
    ///
    /// 一个类的成员可以按来源分开放(`String` 的运算符在 `Operators.cs` 那份、方法在
    /// `Methods.cs` 那份),建类时把它们接起来就行 —— 不用为"一个类只能有一份"把两个
    /// 源文件塞进同一个 `Scan`。名字按给出的顺序去重。</summary>
    internal static IMemberSet Combine(params IMemberSet[] sets) => new Combined(sets);

    private sealed class Combined(IMemberSet[] sets) : IMemberSet
    {
        public IEnumerable<string> Names
        {
            get
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var set in sets)
                    foreach (var name in set.Names)
                        if (seen.Add(name)) yield return name;
            }
        }

        public FunctionVal? Ctor
        {
            get
            {
                foreach (var set in sets)
                    if (set.Ctor is { } ctor) return ctor;
                return null;
            }
        }

        public Variable? Create(ClassVal owner, string name)
        {
            foreach (var set in sets)
                if (set.Create(owner, name) is { } v) return v;
            return null;
        }
    }

    /// <summary>这个方法是哪种成员。没标特性(或者标了个不认识的)就是 null。</summary>
    private static (string? Name, MemberKind Kind, bool ReadOnly) Classify(MethodInfo m)
    {
        if (m.GetCustomAttribute<ClassMethodAttribute>() is { } method)
            return (method.Name, MemberKind.Method, false);

        if (m.GetCustomAttribute<ClassOpAttribute>() is { } op)
        {
            // **装的时候就查符号** —— 不在 `OperatorSymbols.All` 里的用户根本写不出来
            // (`a <> b` 解析就错),留到运行期就成了个够不着的成员
            if (!OperatorSymbols.IsSymbol(op.Op))
                throw new InvalidOperationException(
                    $"运算符 '{op.Op}' 不认 —— 得是 Ravel 那批里的一个"
                    + "（`+` `-` `*` `/` `%` `**` `==` `!=` `<` `>` `<=` `>=` `&` `|` `^` "
                    + "`<<` `>>` `<<<` `>>>` / `is` `isnot` / `<:` `:>`）");
            return (op.Op, MemberKind.Operator, false);
        }

        if (m.GetCustomAttribute<ClassPropertyAttribute>() is { } prop)
            return (prop.Name, MemberKind.Property, prop.ReadOnly);

        return (null, default, false);
    }

    public IEnumerable<string> Names
    {
        get
        {
            foreach (var name in _order) yield return name;
            if (_parent is null) yield break;
            foreach (var name in _parent.Names)
                if (!_own.ContainsKey(name)) yield return name;
        }
    }

    public Variable? Create(ClassVal owner, string name)
    {
        if (!_own.TryGetValue(name, out var spec)) return _parent?.Create(owner, name);

        // 类和插件两条路在这儿合流:`ClassRegistry.Bind` 就是 `[ClassMethod]` 那套绑定
        // (多参会柯里化,只收 self 的那个多给实参要报错 —— 见那边的注释)。
        var acc = ClassRegistry.Bind(owner, spec.Method, name);

        if (spec.Kind == MemberKind.Property)
        {
            // **`by` 槽**:读走 getter、写走 setter,和用户写的 `by x := property g s` 一条路。
            // 类型约束给 `Any`:那格存的是 property,写进来的是**属性值**(见 `Variable.CheckAssignable`)
            var slot = new Variable(name, BuiltinClasses.Any, new PropertyVal(acc, acc));
            slot.SetAttr(Attr.By);
            if (spec.ReadOnly) slot.SetAttr(Attr.Readonly);
            return slot;
        }

        var v = new Variable(name, BuiltinClasses.Function, acc);
        v.SetAttr(Attr.Readonly);
        return v;
    }
}
