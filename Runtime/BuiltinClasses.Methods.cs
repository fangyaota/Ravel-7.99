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
        RegisterTypeMethods();
        RegisterFunctionMethods();
        RegisterScopeMethods();
        RegisterPropertyMethods();
    }

    private static void RegisterObjectMethods()
    {
        Object.DefineMethod("ToString", (s, _) => new StringVal(s.ToString()));
        Object.DefineMethod("Copy", (s, _) =>
        {
            switch (s)
            {
                case IntVal v: return new IntVal(v.Value);
                case FloatVal v: return new FloatVal(v.Value);
                case BoolVal v: return new BoolVal(v.Value);
                case StringVal v: return new StringVal(v.Value);
                case BigIntVal v: return new BigIntVal(v.Value);
                case FractionVal v: return new FractionVal(v.Num, v.Den);
                case BigFractionVal v: return new BigFractionVal(v.Num, v.Den);
                // 容器的成员表跟着副本走 —— 和 CopyObject 一个道理(它们现在也是 ObjectVal)
                case ListVal v: return new ListVal([.. v.Elements], CopyScope(v.Scope));
                case SetVal v: return new SetVal([.. v.Elements], CopyScope(v.Scope));
                case DictVal v: return new DictVal(new Dictionary<string, RuntimeValue>(v.Entries), CopyScope(v.Scope));
                // 函数/类对象"拷"出来只会变成一个不可调用的普通对象 —— 原样交回。
                // 排在 ObjectVal 之前:FunctionVal 现在也是 ObjectVal。
                case FunctionVal v: return v;
                case ObjectVal v: return CopyObject(v);
                default: return s;
            }
        });
        Object.DefineMethod("Fields", (s, _) =>
        {
            var all = new List<RuntimeValue>();
            var seen = new HashSet<string>();

            // 模块(Ravel 实例):字段 = 模块作用域里的变量,排在类型方法前面
            if (s is ModuleVal mv)
            {
                foreach (var kv in mv.ModuleScope.Variables)
                    if (seen.Add(kv.Key))
                        all.Add(new StringVal(kv.Key));
            }
            // 对象:字段 = 实例作用域里的数据字段(和 print 显示的是同一批)。
            // 以前只特判了模块,对象这边漏了,于是 `obj.Fields ()` 只给类型方法——
            // 名字叫 Fields 却拿不到字段。方法仍会由下面那圈并进来。
            else if (s is ObjectVal ov)
            {
                foreach (var kv in ov.Scope.Variables)
                    if (!kv.Value.Value.IsClosure &&
                        kv.Key is not ("this" or "base" or "parent" or "thistype" or "block") &&
                        seen.Add(kv.Key))
                        all.Add(new StringVal(kv.Key));
            }

            // 任何值都再并上类型方法(沿原型链到 object),去重
            for (var t = s.Type; t != null; t = t.Parent)
            {
                foreach (var n in t.MethodNames)
                    if (seen.Add(n))
                        all.Add(new StringVal(n));
                if (t.Parent == t) break;
            }

            return new ListVal(all);
        });
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
        List.DefineMethod("Count", (s, _) => new IntVal(((ListVal)s).Elements.Count));
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
    }

    private static void RegisterSetMethods()
    {
        Set.DefineMethod("Count", (s, _) => new IntVal(((SetVal)s).Elements.Count));
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
        Set.DefineMethod("Contains", (s, a) =>
            new BoolVal(((SetVal)s).Elements.Contains(a)));
    }

    private static void RegisterDictMethods()
    {
        Dict.DefineMethod("Count", (s, _) => new IntVal(((DictVal)s).Entries.Count));
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
        Type.DefineMethod("Is", (s, a) =>
        {
            if (a is not ObjectVal other) throw new RuntimeException("type.Is 需要 type 参数");
            return new BoolVal(((ObjectVal)s).IsAssignableTo(other));
        });
        Type.DefineMethod("Default", (s, _) => ConvertDirect((ObjectVal)s, DefaultVal.Instance));
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
        // Name / scope 走 IFunction:函数和类对象都有这两个,不用转
        Function.DefineMethod("Name", (s, a) =>
        {
            var f = (IFunction)s;
            if (a is StringVal sv)
            {
                f.Name = sv.Value;
                return VoidVal.Instance;
            }

            return new StringVal(f.Name ?? "");
        });
        // 捕获作用域是**函数独有**的:对象没有捕获作用域,它那半边叫 MemberScope
        Function.DefineMethod("scope", (s, _) => new ScopeVal(AsFunction(s, "scope").CaptureScope ?? new Scope()));
        // 换作用域 / prepend / append 只有真函数能做(类对象的作用域是它的实例作用域,不能换)
        Function.DefineMethod("setScope", (s, a) =>
        {
            if (a is not ScopeVal sv) throw new RuntimeException("setScope 需要 Scope 参数");
            AsFunction(s, "setScope").CaptureScope = sv.Scope;
            return VoidVal.Instance;
        });
        Function.DefineMethod("prepend", (s, a) =>
        {
            if (a is not BlockVal p) throw new RuntimeException("prepend 需要代码块参数");
            return AsFunction(s, "prepend").Prepend(p);
        });
        Function.DefineMethod("append", (s, a) =>
        {
            if (a is not BlockVal p) throw new RuntimeException("append 需要代码块参数");
            return AsFunction(s, "append").Append(p);
        });
    }

    private static void RegisterScopeMethods()
    {
        ScopeType.DefineMethod("Push", (s, _) =>
        {
            var scope = ((ScopeVal)s).Scope;
            return new ScopeVal(scope.Push());
        });
        ScopeType.DefineMethod("Define", (s, a) =>
        {
            if (a is not StringVal name)
                throw new RuntimeException("scope.Define 需要字符串名称");
            var scope = ((ScopeVal)s).Scope;
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
            var scope = ((ScopeVal)s).Scope;
            var vr = scope.Lookup(name.Value);
            return new PropertyVal(
                FunctionVal.From(_ => vr.Value),
                FunctionVal.From(v =>
                {
                    vr.Assign(v);
                    return VoidVal.Instance;
                }),
                [.. vr.Attrs]
            );
        });
        ScopeType.DefineMethod("Variables", (s, _) =>
        {
            var scope = ((ScopeVal)s).Scope;
            var d = new Dictionary<string, RuntimeValue>();
            foreach (var kv in scope.Variables)
            {
                if (kv.Key == "this" || kv.Key == "block" || kv.Key == "thistype") continue;
                var vr = scope.Lookup(kv.Key);
                var getter = FunctionVal.From(_ => vr.Value);
                var setter = FunctionVal.From(v =>
                {
                    vr.Assign(v);
                    return VoidVal.Instance;
                });
                d[kv.Key] = new PropertyVal(getter, setter, [.. kv.Value.Attrs]);
            }

            return new DictVal(d);
        });
    }

    private static void RegisterPropertyMethods()
    {
        Property.DefineMethod("Attrs", (s, _) =>
        {
            var pv = (PropertyVal)s;
            return pv.Attrs != null
                ? new ListVal([.. pv.Attrs.Select(a => new StringVal(a))])
                : new ListVal([]);
        });
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

    /// <summary>拷贝一个对象:新实例 scope(方法闭包重绑,见 CopyScope)+ `this` 指向副本。
    /// `with` 和 `obj.Copy ()` 都要这一套——漏掉重绑 `this` 的话,副本里写 `this.v = n`
    /// 会落到原对象上,而裸写 `v = n` 却是对的,行为自相矛盾。</summary>
    internal static ObjectVal CopyObject(ObjectVal src)
    {
        var copy = new ObjectVal(src.ClassType, CopyScope(src.Scope));
        copy.Scope.DefineOrReplace("this", src.ClassType, copy);
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
