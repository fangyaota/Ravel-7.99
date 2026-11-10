namespace Ravel.Runtime.Values;

using System.Reflection;

internal interface IMemberSet
{
    IEnumerable<string> Names { get; }

    FunctionVal? Ctor { get; }

    Variable? Create(ClassVal owner, string name);
}

internal enum MemberKind
{
    Method,
    Operator,
    Property,
    Value,
}

internal sealed class MemberSet : IMemberSet
{
    private readonly record struct Spec(MethodInfo? Method, RuntimeValue? Value, MemberKind Kind, bool ReadOnly);

    private readonly Dictionary<string, Spec> _own = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly IMemberSet? _parent;

    private MemberSet(IMemberSet? parent) => _parent = parent;

    public FunctionVal? Ctor { get; private set; }

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
                if (name is null || !set._own.TryAdd(name, new Spec(m, null, kind, readOnly))) continue;
                set._order.Add(name);
            }

            // 字段那一档:`[ClassValue]` —— 值在静态初始化时就建好了,这里只收名字和它。
            foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public
                                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.GetCustomAttribute<ClassValueAttribute>() is not { } value) continue;
                if (f.GetValue(null) is not RuntimeValue built)
                    throw new InvalidOperationException(
                        $"`[ClassValue(\"{value.Name}\")]` 得打在 RuntimeValue 字段上：{t.Name}.{f.Name}");
                if (!set._own.TryAdd(value.Name, new Spec(null, built, MemberKind.Value, false))) continue;
                set._order.Add(value.Name);
            }
        }

        return set;
    }

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

        if (spec.Kind == MemberKind.Value)
        {
            // `[ClassValue]` 那条:值本身已经是成员,不用再绑 —— 直接交出去。
            // 造出来的一格和其它成员一样只读(成员不是拿来重新赋值的)。
            var given = new Variable(name, BuiltinClasses.Function, spec.Value!);
            given.SetAttr(Attr.Readonly);
            return given;
        }

        // 类和插件两条路在这儿合流:`ClassRegistry.Bind` 就是 `[ClassMethod]` 那套绑定
        // (多参会柯里化,只收 self 的那个多给实参要报错 —— 见那边的注释)。
        var acc = ClassRegistry.Bind(owner, spec.Method!, name);

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
