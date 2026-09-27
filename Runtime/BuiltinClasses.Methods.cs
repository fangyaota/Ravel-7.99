namespace Ravel.Runtime;

using System.Linq;

/// <summary>内置方法的注册（端口自旧的 RuntimeType.Methods.cs）。</summary>
internal static partial class BuiltinClasses
{

    /// <summary>注册各内置类型的方法(按类型分组,加方法时直接跳对应那块)</summary>
    private static void RegisterMethods()
    {
        RegisterObjectMethods();
        RegisterIntMethods();
        RegisterStringMethods();
        RegisterListMethods();
        RegisterSetMethods();
        RegisterDictMethods();
        // 序列方法:三种容器共用那一份(将来 IEnumerable 的落点)。
        // `items` = 「按枚举顺序取出元素」——字典给的是**值**(按键找用 `Has` / `Keys ()`)
        RegisterSequenceMethods(List, s => [.. ((ListVal)s).Elements]);
        RegisterSequenceMethods(Set, s => [.. ((SetVal)s).Elements]);
        RegisterSequenceMethods(Dict, s => [.. ((DictVal)s).Entries.Values]);
        RegisterHigherOrderMethods(List);
        RegisterHigherOrderMethods(Set);
        RegisterHigherOrderMethods(Dict);
        RegisterTypeMethods();
        RegisterFunctionMethods();
        RegisterScopeMethods();
        RegisterPropertyMethods();
    }

    private static void RegisterObjectMethods()
    {
        Object.DefineMethod("ToString", (s, _) => new StringVal(s.ToString()));
        Object.DefineMethod("Copy", (s, _) => CopyValue(s));
        // **枚举那个 Scope** —— 所有成员(字段和方法一视同仁)都在它里面,
        // 没有"猜哪张表"这一步。这里从前是两圈:一圈扫实例作用域里的**数据字段**,
        // 一圈沿类型链扫**方法名**,于是同一个类体里定义的东西一个列得出、一个列不出
        // (运算符恰好在类对象表里另有一份,所以它在;用户写的方法就凭空消失了)。
        // 现在两段判据只有一份,和成员查找共用(见 MemberView)。
        Object.DefineMethod("Fields", (s, _) =>
            new ListVal([.. s.MemberScope.MemberNames.Select(n => (RuntimeValue)new StringVal(n))]));
    }

    private static void RegisterIntMethods()
    {
        Int.DefineMethod("ToString", (s, _) => new StringVal(((IntVal)s).Value.ToString()));
    }

    private static void RegisterStringMethods()
    {
        String.DefineMethod("Length", (s, _) => new IntVal(((StringVal)s).Value.Length));
    }

    /// <summary>取下标之前的统一检查:收下索引、卡边界,顺手把列表交回去。
    /// 以前五处各写一遍检查、消息都只有「索引超出范围」四个字——越的是哪个界、
    /// 在几号元素上越的,全看不出来。allowEnd 给 Insert 用(插到末尾是合法的)。</summary>
    private static ListVal Indexed(RuntimeValue s, RuntimeValue a, string what, bool allowEnd = false)
    {
        if (a is not IntVal i) throw new RuntimeException($"{what} 需要 int 参数");
        var lst = (ListVal)s;
        var n = lst.Elements.Count;
        if (i.Value < 0 || i.Value > (allowEnd ? n : n - 1))
            throw new RuntimeException($"{what} 的索引 {i.Value} 越界 (列表长度 {n})");
        return lst;
    }

    private static void RegisterListMethods()
    {
        List.DefineMethod("At", (s, a) =>
        {
            var lst = Indexed(s, a, "list.At");
            return lst.Elements[((IntVal)a).Value];
        });
        List.DefineMethod("Add", (s, a) =>
        {
            ((ListVal)s).Elements.Add(a);
            return VoidVal.Instance;
        });
        List.DefineMethod("Remove", (s, a) =>
        {
            var lst = Indexed(s, a, "list.Remove");
            var idx = ((IntVal)a).Value;
            var v = lst.Elements[idx];
            lst.Elements.RemoveAt(idx);
            return v;
        });
        List.DefineMethod("Insert", (s, a) =>
        {
            var lst = Indexed(s, a, "list.Insert", allowEnd: true);
            var idx = ((IntVal)a).Value;
            return FunctionVal.From(v =>
            {
                lst.Elements.Insert(idx, v);
                return VoidVal.Instance;
            });
        });
        List.DefineMethod("Set", (s, a) =>
        {
            var lst = Indexed(s, a, "list.Set");
            var idx = ((IntVal)a).Value;
            return FunctionVal.From(v =>
            {
                lst.Elements[idx] = v;
                return VoidVal.Instance;
            });
        });

        // 下面这些是**列表才有**的:要么带下标,要么依赖"顺序是有意义的"
        // (集合/字典没有这条 —— 所以它们只在序列方法那一批里出现,见 BuiltinClasses.Sequences.cs)
        List.DefineMethod("IndexOf", (s, a) => new IntVal(((ListVal)s).Elements.IndexOf(a)));
        // `RemoveAt i` 和 `Remove i` 是同一件事:名字对齐 C#(`List.RemoveAt` 按下标、
        // `List.Remove` 按值),老名字留着 —— 库里(HandlerStack)和好几个用例都在用
        List.DefineMethod("RemoveAt", (s, a) =>
        {
            var lst = Indexed(s, a, "list.RemoveAt");
            var idx = ((IntVal)a).Value;
            var v = lst.Elements[idx];
            lst.Elements.RemoveAt(idx);
            return v;
        });
        List.DefineMethod("AddRange", (s, a) =>
        {
            ((ListVal)s).Elements.AddRange(ElementsOf(a, "AddRange"));
            return VoidVal.Instance;
        });
        List.DefineMethod("Clear", (s, _) =>
        {
            ((ListVal)s).Elements.Clear();
            return VoidVal.Instance;
        });
        // 排序**交回新的**(和 Map/Where 那批一个规矩),稳定的,比不了当场说人话(见 SortByKey)
        List.DefineMethod("Sort", (s, _) => SortByKey(((ListVal)s).Elements, x => x));
    }

    private static void RegisterSetMethods()
    {
        Set.DefineMethod("Add", (s, a) =>
        {
            ((SetVal)s).Elements.Add(a);
            return VoidVal.Instance;
        });
        Set.DefineMethod("Remove", (s, a) =>
        {
            ((SetVal)s).Elements.Remove(a);
            return VoidVal.Instance;
        });
        Set.DefineMethod("Clear", (s, _) =>
        {
            ((SetVal)s).Elements.Clear();
            return VoidVal.Instance;
        });
        // 集合代数(C# 的 HashSet 同名方法):交回**新的 set**,原集合不动
        Set.DefineMethod("Union", (s, a) => new SetVal([.. ((SetVal)s).Elements, .. ElementsOf(a, "Union")]));
        Set.DefineMethod("Intersect", (s, a) =>
        {
            var other = ElementsOf(a, "Intersect").ToHashSet();
            return new SetVal([.. ((SetVal)s).Elements.Where(other.Contains)]);
        });
        Set.DefineMethod("Except", (s, a) =>
        {
            var other = ElementsOf(a, "Except").ToHashSet();
            return new SetVal([.. ((SetVal)s).Elements.Where(x => !other.Contains(x))]);
        });
        Set.DefineMethod("IsSubsetOf", (s, a) =>
        {
            var other = ElementsOf(a, "IsSubsetOf").ToHashSet();
            return new BoolVal(((SetVal)s).Elements.All(other.Contains));
        });
    }

    private static void RegisterDictMethods()
    {
        Dict.DefineMethod("Get", (s, a) =>
        {
            if (a is not StringVal key) throw new RuntimeException("dict.Get 需要 string 参数");
            var d = (DictVal)s;
            if (d.Entries.TryGetValue(key.Value, out var v)) return v;
            throw new RuntimeException($"键不存在: {key.Value}");
        });
        Dict.DefineMethod("Set", (s, a) =>
        {
            if (a is not StringVal key) throw new RuntimeException("dict.Set 需要 string 键");
            return FunctionVal.From(v =>
            {
                ((DictVal)s).Entries[key.Value] = v;
                return VoidVal.Instance;
            });
        });
        Dict.DefineMethod("Has", (s, a) =>
        {
            if (a is not StringVal key) throw new RuntimeException("dict.Has 需要 string 参数");
            return new BoolVal(((DictVal)s).Entries.ContainsKey(key.Value));
        });
        Dict.DefineMethod("Keys", (s, _) =>
        {
            var keys = new List<RuntimeValue>();
            foreach (var k in ((DictVal)s).Entries.Keys)
                keys.Add(new StringVal(k));
            return new ListVal(keys);
        });
        Dict.DefineMethod("Values", (s, _) =>
        {
            var vals = new List<RuntimeValue>();
            foreach (var v in ((DictVal)s).Entries.Values)
                vals.Add(v);
            return new ListVal(vals);
        });
        Dict.DefineMethod("Remove", (s, a) =>
        {
            if (a is not StringVal key) throw new RuntimeException("dict.Remove 需要 string 键");
            return new BoolVal(((DictVal)s).Entries.Remove(key.Value));   // 有没有删掉(C# 也交回 bool)
        });
        Dict.DefineMethod("HasValue", (s, a) => new BoolVal(((DictVal)s).Entries.ContainsValue(a)));
        // `d.GetOr "k" 0` —— 有就取值,没有就给替代值(C# 的 GetValueOrDefault)。
        // 默认的 `Get` 是**响亮**的(键不存在就报错),这个是"明知可能没有"时用的。
        Dict.DefineMethod("GetOr", (s, a) =>
        {
            if (a is not StringVal key) throw new RuntimeException("dict.GetOr 需要 string 键");
            var d = (DictVal)s;
            return d.Entries.TryGetValue(key.Value, out var v)
                ? FunctionVal.From(_ => v)        // 有:替代值收下但不用
                : FunctionVal.From(fallback => fallback);
        });
        Dict.DefineMethod("Clear", (s, _) =>
        {
            ((DictVal)s).Entries.Clear();
            return VoidVal.Instance;
        });
    }

    private static void RegisterTypeMethods()
    {
        // 这里**不能**再注册 `name` 方法:类名现在是类对象 Scope 里的 `name` 成员
        // (见 ObjectVal.Name),和它同住一个作用域的方法会把它覆盖掉。
        // 而且那个方法本来就是死代码 —— GetMember 的 `name` 伪成员排在方法查找之前,
        // 读 `int.name` 永远走伪成员,够不着方法。读走伪成员、写走成员赋值,行为不变。
        Type.DefineMethod("Parent", (s, _) =>
        {
            var t = (ObjectVal)s;
            var p = t.Parent;
            if (p == null || p == t) return s;      // 自引用(链到头)就返回自己
            return p;
        });
        // `Is` 这个**方法**删掉了:它问类型、`is` 问值,两个长得像的东西各管一头,
        // 最常踩的是拿实例去调(`C.Is (C ())` 从前静默给 false,读起来还像"这个实例是不是 C")。
        // 要问类型之间的关系就用运算符:`int <: object` / `object :> int`(两边都得是类型);
        // 要问值就用 `x is T`。
        // `T.GetImplements ()`:这个类型现在实现了哪些接口(实现登记在作用域里,所以真正算它的是
        // 求值器 —— 这里只挂上那个"绑好接收者、调用时由 CallInto 认出"的成员值)。
        Type.DefineMethod("Default", (s, _) => ConvertDirect((ObjectVal)s, DefaultVal.Instance));
        Type.Scope.DefineOrReplace("GetImplements", Function, new ImplementsQuery("GetImplements"));
        Type.DefineMethod("Subtypes", (s, _) =>
        {
            var t = (ObjectVal)s;
            var subs = new List<RuntimeValue>();
            foreach (var sub in AllTypes)
            {
                if (sub != t && sub.IsAssignableTo(t))
                    subs.Add(sub);
            }

            return new ListVal(subs);
        });
    }

    /// <summary>取接收者当函数用。**不能硬转 `(FunctionVal)s`**:类对象的元类链里有
    /// `Function`(type <: function),所以 `Fields ()` 会把 Function 的方法列成类的可用方法,
    /// 而类对象是 `ObjectVal` —— 硬转就抛 InvalidCastException 漏到顶层。
    /// 换句话说,这是 BoolVal 那个「类型说有、值却接不住」的**反向**同款。</summary>
    private static FunctionVal AsFunction(RuntimeValue s, string what)
        => s as FunctionVal ?? throw new RuntimeException($"{what} 只对函数有意义，{s.Type} 不行");

    private static void RegisterFunctionMethods()
    {
        // Name 落在成员表里(ObjectVal.Name),函数和类对象都有,不用转
        Function.DefineMethod("Name", (s, a) =>
        {
            var f = (ObjectVal)s;
            if (a is StringVal sv)
            {
                f.Name = sv.Value;
                return VoidVal.Instance;
            }

            return new StringVal(f.Name ?? "");
        });
        // 捕获作用域是**函数独有**的:对象没有捕获作用域,它那半边叫 MemberScope。
        // 没有捕获作用域的值(类对象不是闭包)给一个**空 Scope** —— **类型恒定是 Scope**,
        // 不拿 `()` 顶替,也不报错:`x.scope ()` 拿到的东西该始终能当作用域用。
        Function.DefineMethod("Scope", (s, _) => new ScopeVal(AsFunction(s, "Scope").CaptureScope ?? new Scope()));
        // body:这个函数/类的**体**。同样类型恒定是 Block —— 没有体的(内置方法、
        // 原生闭包、没类体的内建类)给一个**空块**,而不是 `()`:`f.body` 始终能当块用
        // (`(f.body) 1` 就是跑一遍它)。
        Function.DefineMethod("Body", (s, _) => s switch
        {
            LambdaVal lam => new BlockVal(lam.Block, lam.CaptureScope),
            ClassVal c => c.ClassBody ?? EmptyBody,
            BlockVal b => b,
            _ => EmptyBody,
        });
        // 换作用域 / prepend / append 只有真函数能做(类对象的作用域是它的实例作用域,不能换)
        Function.DefineMethod("SetScope", (s, a) =>
        {
            if (a is not ScopeVal sv) throw new RuntimeException("setScope 需要 Scope 参数");
            AsFunction(s, "SetScope").CaptureScope = sv.Inner;
            return VoidVal.Instance;
        });
        Function.DefineMethod("Prepend", (s, a) =>
        {
            if (a is not BlockVal p) throw new RuntimeException("prepend 需要代码块参数");
            return AsFunction(s, "Prepend").Prepend(p);
        });
        Function.DefineMethod("Append", (s, a) =>
        {
            if (a is not BlockVal p) throw new RuntimeException("append 需要代码块参数");
            return AsFunction(s, "Append").Append(p);
        });
    }

    private static void RegisterScopeMethods()
    {
        ScopeType.DefineMethod("Push", (s, _) =>
        {
            var scope = ((ScopeVal)s).Inner;
            return new ScopeVal(scope.Push());
        });
        ScopeType.DefineMethod("Define", (s, a) =>
        {
            if (a is not StringVal name)
                throw new RuntimeException("scope.Define 需要字符串名称");
            var scope = ((ScopeVal)s).Inner;
            return FunctionVal.From(tv =>
            {
                if (tv is not ObjectVal t)
                    throw new RuntimeException("scope.Define 需要 type 参数");
                scope.Define(name.Value, t, VoidVal.Instance);
                return VoidVal.Instance;
            });
        });
        ScopeType.DefineMethod("Lookup", (s, a) =>
        {
            if (a is not StringVal name)
                throw new RuntimeException("scope.Lookup 需要字符串参数");
            return Wrap(((ScopeVal)s).Inner.Lookup(name.Value));
        });
        ScopeType.DefineMethod("Variables", (s, _) =>
        {
            var scope = ((ScopeVal)s).Inner;
            var d = new Dictionary<string, RuntimeValue>();
            foreach (var kv in scope.Variables)
            {
                if (kv.Key is ObjectVal.ThisMember or ObjectVal.BlockMember or ObjectVal.ThisTypeMember) continue;
                d[kv.Key] = Wrap(kv.Value);
            }

            return new DictVal(d);
        });
    }

    /// <summary>**属性的默认值** —— 一对什么都不做的函数:
    ///
    ///     Get = () => { (); }
    ///     Set = (_ : object) => { (); }
    ///
    /// 和 `int default` 给 `0`、`function default` 给空函数是同一个道理:
    /// `by a: int = default` 里那个 `default` 就是它 —— 槽里先放一个**能读能写、
    /// 但什么都不做**的属性(读出来是 `()`、写进去丢掉),之后用 `by a = property g s`
    /// 把真实现换上。
    ///
    /// 它**没有状态**,所以 `with` / `Copy ()` 让副本和原件共享同一个也无害
    /// (`CopyScope` 那条复制路不用为它做任何事)。</summary>
    internal static PropertyVal DefaultProperty()
        => new(FunctionVal.From(_ => VoidVal.Instance), FunctionVal.From(_ => VoidVal.Instance));

    /// <summary>把一个变量包成 property(getter 读、setter 写),attrs 原样带上。
    /// `scope.Lookup` 和 `scope.Variables` 都要这一套 —— 从前各写了一遍。
    ///
    /// 直接收 `Variable`(而不是名字)是故意的:`Variables` 正在遍历的那份变量就是它,
    /// 再 `Lookup` 一次等于把同一个名字查两遍(而且是沿链查,不是查那层那一格)。</summary>
    private static PropertyVal Wrap(Variable vr)
        => new(
            FunctionVal.From(_ => vr.Value),
            FunctionVal.From(v =>
            {
                vr.Assign(v);
                return VoidVal.Instance;
            }),
            vr);

    private static void RegisterPropertyMethods()
    {
        // attrs 只有一份,在变量上(见 PropertyVal.Var);裸的 property 没挂变量 -> 空表
        Property.DefineMethod("Attrs", (s, _) =>
            new ListVal([.. (((PropertyVal)s).Var?.Attrs ?? []).Select(a => new StringVal(a))]));
    }

    /// <summary>拷贝作用域（with / Copy 用）：逐字段浅拷贝，词法父照搬。
    /// 字段已平铺在同一个 scope 里，所以不再需要沿继承链递归深拷贝。</summary>
    /// <summary>把指回原实例 scope 的闭包重绑到新 scope。方法(lambda)直接重绑;
    /// `by` 属性要连 getter/setter 一起——它们也是 lambda,只是装在 PropertyVal 里。</summary>
    private static RuntimeValue Rebind(RuntimeValue value, Scope dst) => value switch
    {
        LambdaVal lam => lam with { CaptureScope = dst },
        PropertyVal pv => pv with { Getter = RebindFn(pv.Getter, dst), Setter = RebindFn(pv.Setter, dst) },
        _ => value,
    };

    private static FunctionVal RebindFn(FunctionVal f, Scope dst)
        => f is LambdaVal lam ? lam with { CaptureScope = dst } : f;

    /// <summary>浅拷贝一个值 —— `obj.Copy ()` 和 `with` **共用这一份**。
    ///
    /// 只有"带字段的数据"才有副本可谈:
    /// - **函数/类对象**"拷"出来只会变成一个不可调用的普通对象,**模块**被拷成普通对象后
    ///   `EnterModule`/`_modules` 就认不出它了,**属性/作用域值**同理 —— 一律原样交回。
    /// - **原子值**是值语义的 record,交回自己和交回副本在 `==` 下没有区别,不必逐个列。
    ///
    /// 后两条都靠"原样交回"表达,所以调用方 `ReferenceEquals(结果, 原值)` 就能问出
    /// "到底拷没拷"(`with` 用它决定块跑在副本的成员表里还是自己的捕获作用域里)。
    /// **会交出不同引用的只有**下面那三条容器臂和 `ObjectVal` 那条 —— 副本一律是 `ObjectVal`。</summary>
    internal static RuntimeValue CopyValue(RuntimeValue v) => v switch
    {
        // 容器的成员表跟着副本走 —— 和 CopyObject 一个道理(它们也是 ObjectVal)
        ListVal l => new ListVal([.. l.Elements], CopyScope(l.Scope)),
        SetVal s => new SetVal([.. s.Elements], CopyScope(s.Scope)),
        DictVal d => new DictVal(new Dictionary<string, RuntimeValue>(d.Entries), CopyScope(d.Scope)),
        // 必须排在 ObjectVal 之前:这四类现在也是 ObjectVal
        FunctionVal or ModuleVal or PropertyVal or ScopeVal => v,
        ObjectVal o => CopyObject(o),
        _ => v,
    };

    /// <summary>拷贝一个对象:新实例 scope(方法闭包重绑,见 CopyScope)+ `this` 指向副本。
    /// 漏掉重绑 `this` 的话,副本里写 `this.v = n` 会落到原对象上,
    /// 而裸写 `v = n` 却是对的,行为自相矛盾。</summary>
    internal static ObjectVal CopyObject(ObjectVal src)
    {
        var copy = new ObjectVal(src.ClassType, CopyScope(src.Scope));
        copy.Scope.DefineOrReplace(ObjectVal.ThisMember, src.ClassType, copy);
        return copy;
    }

    internal static Scope CopyScope(Scope src)
    {
        var dst = new Scope(src.Parent);
        foreach (var kv in src.Variables)
        {
            var value = kv.Value.Value;
            // 方法(lambda)的闭包 Scope 指向原实例。浅拷贝共享它的话,
            // 在副本上调方法会读写到原对象的字段——`with` 就白拷了。
            value = Rebind(value, dst);
            // attrs 也要带过去:丢了 by 的话副本上的 `v = x` 不走 setter,
            // 丢了 readonly/private/core 就等于副本绕过了这些约束
            var v = dst.Define(kv.Key, kv.Value.TypeConstraint, value);
            foreach (var a in kv.Value.Attrs) v.SetAttr(a);
        }

        return dst;
    }
}
