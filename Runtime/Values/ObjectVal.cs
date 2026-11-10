namespace Ravel.Runtime;

public record ObjectVal : RuntimeValue
{
    public ClassVal ClassType { get; internal set; }

    public virtual Scope Scope => _scope ??= new Values.MemberTable(this);

    private Scope? _scope;

    public ObjectVal(ClassVal? classType, Scope? scope)
    {
        ClassType = classType!;
        _scope = scope;
        // 传进来的那张表**就是这个值的成员表**(模块、插件的值、`ScopeVal` 都是这么给的)——
        // 在这儿登记,它才能在"本层没有"时兜底去 `BuiltinClasses` 的注册表里拿
        // `Fields` / `Push` / `References` 这些引擎挂上去的成员。
        // 没传表(稍后懒造 <see cref="Values.MemberTable"/>)的不标:那张表自己有 `Create`。
        if (scope is not null) scope.MemberOf = this;
    }

    private const int MaxFields = 8;

    // 成员名——机制内部用,别在 C# 里到处写字符串
    internal const string ParentMember = "parent";
    internal const string RequiresMember = "requires";
    internal const string BlockMember = "block";
    internal const string NameMember = "name";
    internal const string InitMember = "init";
    internal const string ThisMember = "this";
    internal const string GetterMember = "Get";
    internal const string SetterMember = "Set";
    internal const string ThisTypeMember = "thistype";

    public override ObjectVal Type => ClassType;

    public override Scope MemberScope => Scope;

    public override bool HasOwnMembers => true;

    // ============================================================
    //  类性:读 Scope 里的成员
    // ============================================================

    public bool IsClass => Type.IsAssignableTo(BuiltinClasses.Type);
    public ObjectVal? Parent => Scope.LookupField(ParentMember)?.Value as ObjectVal;

    public BlockVal? ClassBody
    {
        get => Scope.LookupField(BlockMember)?.Value as BlockVal;
        set
        {
            if (value != null)
                Scope.DefineOrReplace(BlockMember, BuiltinClasses.Block, value).SetAttr(Attr.Readonly);
        }
    }

    internal IReadOnlyList<BlockVal> BodyExtras { get; set; } = [];

    internal bool IsSealed;

    internal List<ObjectVal>? Requires;

    public string? Name
    {
        get => (Scope.LookupField(NameMember)?.Value as StringVal)?.Value;
        set
        {
            if (value != null) Scope.DefineOrReplace(NameMember, BuiltinClasses.String, new StringVal(value));
        }
    }

    internal virtual string DisplayName => Name is { Length: > 0 } n ? n : "class";

    // ============================================================
    //  成员查找 / 类型判定
    // ============================================================

    internal static bool IsMethodName(string n)
        => n is not (BlockMember or ParentMember or RequiresMember or NameMember or InitMember or ThisMember);

    public bool IsAssignableTo(ObjectVal target)
    {
        if (this == BuiltinClasses.Every) return true;
        if (target == BuiltinClasses.Any) return true;
        return ChainOrRequires(this, target, null);

        // 已访问集合是**惰性**建的:有要求表才可能绕圈(`A 要求 B`、`B 要求 A`),
        // 而绝大多数调用(值类型、容器)一路 null 检查就走完了,一次分配都不花。
        static bool ChainOrRequires(ObjectVal from, ObjectVal target, HashSet<ObjectVal>? seen)
        {
            for (var t = from; t != null; t = t.Parent)
            {
                if (t == target) return true;
                if (t.Requires is { Count: > 0 } reqs)
                {
                    seen ??= new HashSet<ObjectVal>(ReferenceEqualityComparer.Instance);
                    if (seen.Add(t))
                        foreach (var r in reqs)
                            if (ChainOrRequires(r, target, seen))
                                return true;
                }

                if (t.Parent == t) break;   // 自引用(object/Every/Any)= 链到头
            }

            return false;
        }
    }

    public static FunctionVal BindMethod(FunctionVal methodFn, RuntimeValue self)
    {
        var bound = (FunctionVal)methodFn.Body(self);
        bound.Name = methodFn.Name;
        return bound;
    }

    // ============================================================
    //  显示
    // ============================================================

    public override string ToString()
        => IsClass ? DisplayName
         : BuiltinClasses.ExceptionMessage(this) is { } msg ? msg
         : InstanceText();

    private string InstanceText()
    {
        var name = ClassType.DisplayName;
        var fields = new List<string>();
        foreach (var kv in Scope.Variables)
        {
            // 只列数据字段:方法(含 init)是噪音;this/parent/block 是机制成员,不是这个对象的数据。
            // `thistype` 一并排掉:它如今只是保留字(用户定义不出这个名字),但实例作用域里
            // 或许还留着老代码的痕迹,排掉不会有副作用。
            if (kv.Value.Value.IsClosure) continue;
            // `instance` 也排掉:它是实现身上的机制槽(一条指向"这一次在服务谁"的 by 槽),
            // 不是这个对象的数据 —— 打印实现时拎出来只会让人以为实现里存着个实例。
            if (kv.Key is ThisMember or ParentMember or ThisTypeMember or BlockMember
                or BuiltinClasses.InstanceMember or BuiltinClasses.ImplIdMember) continue;
            if (fields.Count == MaxFields) { fields.Add("..."); break; }
            fields.Add(kv.Key + " = " + Brief(kv.Value.Value));
        }

        return fields.Count == 0 ? name + " {}" : name + " { " + string.Join(", ", fields) + " }";
    }

    private static string Brief(RuntimeValue v) => v switch
    {
        // 类对象显示名字,实例显示 `类名 {...}`
        ObjectVal { IsClass: true } o => o.DisplayName,
        // 函数排在实例之前:FunctionVal 也是 ObjectVal,按实例展开会打出一串成员
        FunctionVal f => f.ToString(),
        // 容器也排在 ObjectVal 之前(它们现在也是 ObjectVal),不然会打成 `List {...}`
        ListVal l => "[" + l.Elements.Count + " items]",
        SetVal s => "{" + s.Elements.Count + " items}",
        DictVal d => "{" + d.Entries.Count + " items}",
        // 走 `Type` 而不是 `ClassType` —— 只有它保证非 null(见 FunctionVal.Type 的懒回填)
        ObjectVal o => o.Type.DisplayName + " {...}",
        _ => v.ToString() ?? "()",
    };

    // ============================================================
    //  相等性 = 身份
    // ============================================================

    public virtual bool Equals(ObjectVal? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
