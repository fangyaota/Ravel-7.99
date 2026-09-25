namespace Ravel.Runtime;

/// <summary>对象——**实例和类用的是同一个表示**。
///
/// 一个对象"是类" ⟺ 它的 Scope 里有 `parent`(父类对象) 和 `block`(类体) 两个成员;
/// 没有就是普通实例。类性不另设 C# 字段,这让"类"和"实例"彻底同构,
/// 元类要的「一个类被另一个类造出来」才有地方安放。
///
/// `ClassType` 是它的**元类**(创建它的那个类对象)。`typeof X` 就是取这个。
/// `type` 的元类是它自己(自指,链的起点)。
///
/// 成员全在 <see cref="Scope"/> 里——方法、字段、`init`、`parent`、`block` 一视同仁。
/// 沿原型链的查找见 <see cref="LookupInChain{T}"/>。</summary>
public record ObjectVal : RuntimeValue, IFunction
{
    /// <summary>元类(创建者)。`null` 传进来表示**自指**——只有 `type` 是这样(根元类).
    /// 建内置类对象的时候 `type` 还没造出来,所以先当自指、之后由 BuiltinClasses 回填,
    /// 因此这里给 internal set。</summary>
    public ObjectVal ClassType { get; internal set; }

    public Scope Scope { get; }

    public ObjectVal(ObjectVal? classType, Scope scope)
    {
        ClassType = classType ?? this;
        Scope = scope;
    }

    /// <summary>最多列几个字段,超出用 ... 收尾</summary>
    private const int MaxFields = 8;

    // 成员名——机制内部用,别在 C# 里到处写字符串
    internal const string ParentMember = "parent";
    internal const string BlockMember = "block";
    internal const string NameMember = "name";
    internal const string CallMember = "call";

    public override ObjectVal Type => ClassType;

    /// <summary>**临时**(S1 阶段):内置类型的同步转换器。有它就走它、交出它的返回值,
    /// 没有就走"实例化并交出实例"——这是今天 `RuntimeType.Initializer` vs `Body` 那条分叉的搬运。
    /// S2 会把它换成"每个类的类体里都有一个 `init` 成员",然后删掉这个字段。</summary>
    internal FunctionVal? Initializer { get; set; }

    // ============================================================
    //  类性:读 Scope 里的成员
    // ============================================================

    /// <summary>这个对象是个类吗?判据是**它的元类继承自 `type`**。
    ///
    /// - `C := class {...}` 的元类是 `type` ✓
    /// - 元类建的类,元类是那个元类(它也继承自 `type`)✓
    /// - `type` 自己元类自指 ✓
    /// - 实例的元类是它的类,而那个类的 parent 链走到 `Object` 就停、够不着 `type` ✗
    ///
    /// 走的是**类型关系**,不是"某个成员名在不在"——后者是鸭子类型该管的事,
    /// 会跟 interface/shape 的判据混在一起。</summary>
    public bool IsClass => ClassType.IsAssignableTo(BuiltinClasses.Type);

    /// <summary>在 Ravel 层"能不能调用"的判据:自己那层有没有 `call` 成员。
    /// 只看本层——类对象建出来时会被装上(见 BuiltinClasses),实例不会被装,
    /// 这样 `c ()` 不会因为"它的类有 call"就跟着可调用。
    ///
    /// `call` 是**唯一的**可调用判据,引擎因此不需要知道"什么是类":
    /// 将来 interface / shape 这类"要求某组成员"的概念可以直接建在同一套判据上。</summary>
    public bool HasCall => Scope.LookupField(CallMember) != null;

    /// <summary>父类对象(原型链的上游)。自引用(如 `object`/`Every`/`Any`)表示链到头。</summary>
    public ObjectVal? Parent => Scope.LookupField(ParentMember)?.Value as ObjectVal;

    /// <summary>类体——实例化时重跑的配方</summary>
    public BlockVal? Body => Scope.LookupField(BlockMember)?.Value as BlockVal;

    /// <summary>类名。`C := class {...}` 建的类**没有名字**(只有 `::=` 会命名)</summary>
    public string? Name
    {
        get => (Scope.LookupField(NameMember)?.Value as StringVal)?.Value;
        set
        {
            if (value != null) Scope.DefineOrReplace(NameMember, BuiltinClasses.String, new StringVal(value));
        }
    }

    /// <summary>显示用的名字。空名字会让报错变成「类型 '' 不支持运算符」,所以退化成 "class"</summary>
    internal string DisplayName => Name is { Length: > 0 } n ? n : "class";

    // ============================================================
    //  成员查找 / 类型判定
    // ============================================================

    /// <summary>沿原型链找成员,找不到返回 null。与 <see cref="Scope.LookupField"/> 的区别:
    /// 那个是**一层**(实例的字段),这个是**一条链**(类的方法要沿 parent 找)。
    ///
    /// 自引用(`object`/`Every`/`Any` 的 parent 是自己)要就地停,否则转不出去。</summary>
    internal T? LookupInChain<T>(string name) where T : RuntimeValue
    {
        for (var t = this; t != null; t = t.Parent)
        {
            if (t.Scope.LookupField(name)?.Value is T hit) return hit;
            if (t.Parent == t) break;      // 链到头
        }

        return null;
    }

    /// <summary>沿原型链查方法(存的是"self → (arg → impl)"的自绑定函数)</summary>
    public FunctionVal? TryLookupMethod(string name) => LookupInChain<FunctionVal>(name);

    /// <summary>沿原型链查方法,找不到抛异常</summary>
    public FunctionVal LookupMethod(string name)
        => TryLookupMethod(name) ?? throw new RuntimeException($"类型 '{DisplayName}' 没有方法 '{name}'");

    /// <summary>this 是否兼容 target?即 this &lt;: target。底类型 Every 全局特判(它是所有类的子类)。</summary>
    public bool IsAssignableTo(ObjectVal target)
    {
        if (this == BuiltinClasses.Every) return true;
        if (target == BuiltinClasses.Any) return true;
        for (var t = this; t != null; t = t.Parent)
        {
            if (t == target) return true;
            if (t.Parent == t) break;      // 自引用(object/Every/Any)= 链到头
        }

        return false;
    }

    /// <summary>绑定方法 self:方法值存为 Curried(self, arg),绑 self 得等待 arg 的函数</summary>
    public static FunctionVal BindMethod(FunctionVal methodFn, RuntimeValue self)
        => (FunctionVal)methodFn.Body(self);

    // ============================================================
    //  注册(S1 阶段内置成员仍走这里,S2 会改成往类体的 Scope 里定义)
    // ============================================================

    /// <summary>注册内置同步方法:BuiltinMethodVal 标记,分派走快速同步路径</summary>
    internal void DefineMethod(string name, Func<RuntimeValue, RuntimeValue, RuntimeValue> impl)
        => Scope.DefineOrReplace(name, BuiltinClasses.Function, new BuiltinMethodVal(impl));

    /// <summary>注册类运算符:op 为符号("+")。存自绑函数——self 绑定得 BoundClassOp,走 CallInto 推 ClassOp 帧</summary>
    internal void DefineClassOperator(string op)
        => Scope.DefineOrReplace(op, BuiltinClasses.Function, new ClassOperatorFactory(op));

    /// <summary>本层定义过的**方法**名(给 `Fields ()` 用)。
    /// 机制成员要排掉:`block` 是代码块(它也是 FunctionVal)、`call` 是"可调用"的凭据、
    /// `parent` 是原型链指针——它们都不是用户眼里的"方法"。</summary>
    internal IEnumerable<string> MethodNames => Scope.Variables
        .Where(kv => kv.Value.Value is FunctionVal
                     && kv.Key is not (BlockMember or CallMember or ParentMember or NameMember))
        .Select(kv => kv.Key);

    // ============================================================
    //  显示
    // ============================================================

    /// <summary>类是打印它的名字(`print C` → `class`);实例打印字段列表(`class { x = 1 }`)。</summary>
    public override string ToString()
        => IsClass ? DisplayName : InstanceText();

    private string InstanceText()
    {
        var name = ClassType.DisplayName;
        var fields = new List<string>();
        foreach (var kv in Scope.Variables)
        {
            // 只列数据字段:方法(含 init)是噪音;parent/block 是类才有的机制成员
            if (kv.Value.Value.IsClosure) continue;
            if (kv.Key is "this" or ParentMember or "base" or "thistype" or BlockMember or CallMember) continue;
            if (fields.Count == MaxFields) { fields.Add("..."); break; }
            fields.Add(kv.Key + " = " + Brief(kv.Value.Value));
        }

        return fields.Count == 0 ? name + " {}" : name + " { " + string.Join(", ", fields) + " }";
    }

    /// <summary>字段值的短形式:嵌套的对象/集合不再展开。列表本身就展开元素,
    /// 这里跟着展开的话,`a.Add a` 这种自引用会直接把栈打爆。</summary>
    private static string Brief(RuntimeValue v) => v switch
    {
        // 类对象显示名字,实例显示 `类名 {...}`
        ObjectVal { IsClass: true } o => o.DisplayName,
        ObjectVal o => o.ClassType.DisplayName + " {...}",
        ListVal l => "[" + l.Elements.Count + " 项]",
        SetVal s => "{" + s.Elements.Count + " 项}",
        DictVal d => "{" + d.Entries.Count + " 项}",
        _ => v.ToString() ?? "()",
    };

    // ============================================================
    //  相等性 = 身份
    // ============================================================

    /// <summary>**必须按身份比**,不能用 record 自动生成的相等:那会递归比较 `ClassType`,
    /// 而 `type` 的 ClassType 是它自己——比较两个类对象会直接转不出来。
    /// 这也正是要的语义:对象(含类)在集合里按身份算,标量才比值。</summary>
    public virtual bool Equals(ObjectVal? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
