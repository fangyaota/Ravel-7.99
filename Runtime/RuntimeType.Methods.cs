namespace Ravel.Runtime;

using System.Linq;

public partial class RuntimeType
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
                case ListVal v: return new ListVal([.. v.Elements]);
                case SetVal v: return new SetVal([.. v.Elements]);
                case DictVal v: return new DictVal(new Dictionary<string, RuntimeValue>(v.Entries));
                case ObjectVal v:
                {
                    return new ObjectVal(v.ClassType, CopyScope(v.Scope));
                }
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
                    if (kv.Value.Value is not FunctionVal &&
                        kv.Key is not ("this" or "base" or "parent" or "thistype" or "block") &&
                        seen.Add(kv.Key))
                        all.Add(new StringVal(kv.Key));
            }

            // 任何值都再并上类型方法(沿继承链到 object),去重
            for (var t = s.Type; ; t = t.Parent)
            {
                foreach (var n in t.MethodNames)
                    if (seen.Add(n))
                        all.Add(new StringVal(n));
                if (t == t.Parent) break;
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

    private static void RegisterListMethods()
    {
        List.DefineMethod("Count", (s, _) => new IntVal(((ListVal)s).Elements.Count));
        List.DefineMethod("At", (s, a) =>
        {
            if (a is not IntVal i) throw new RuntimeException("list.At 需要 int 参数");
            var lst = (ListVal)s;
            if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return lst.Elements[i.Value];
        });
        List.DefineMethod("Add", (s, a) =>
        {
            ((ListVal)s).Elements.Add(a);
            return VoidVal.Instance;
        });
        List.DefineMethod("Remove", (s, a) =>
        {
            if (a is not IntVal i) throw new RuntimeException("list.Remove 需要 int 参数");
            var lst = (ListVal)s;
            if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            var v = lst.Elements[i.Value];
            lst.Elements.RemoveAt(i.Value);
            return v;
        });
        List.DefineMethod("Insert", (s, a) =>
        {
            if (a is not IntVal i) throw new RuntimeException("list.Insert 需要 int 参数");
            var lst = (ListVal)s;
            var idx = i.Value;
            if (idx < 0 || idx > lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return FunctionVal.From(v =>
            {
                lst.Elements.Insert(idx, v);
                return VoidVal.Instance;
            });
        });
        List.DefineMethod("Set", (s, a) =>
        {
            if (a is not IntVal i) throw new RuntimeException("list.Set 需要 int 参数");
            var lst = (ListVal)s;
            var idx = i.Value;
            if (idx < 0 || idx >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
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
        Type.DefineMethod("name", (s, _) => new StringVal(((TypeVal)s).Value.DisplayName));
        Type.DefineMethod("Parent", (s, _) =>
        {
            var p = ((TypeVal)s).Value.Parent;
            if (p == ((TypeVal)s).Value) return s;
            return new TypeVal(p);
        });
        Type.DefineMethod("Is", (s, a) =>
        {
            if (a is not TypeVal other) throw new RuntimeException("type.Is 需要 type 参数");
            return new BoolVal(((TypeVal)s).Value.IsAssignableTo(other.Value));
        });
        Type.DefineMethod("Default", (s, _) =>
        {
            var tv = (TypeVal)s;
            return ConvertDirect(tv.Value, DefaultVal.Instance);
        });
        Type.DefineMethod("Initializer", (s, _) =>
        {
            var tv = (TypeVal)s;
            return new PropertyVal(
                FunctionVal.From(_ => (RuntimeValue?)tv.Value.Initializer ?? VoidVal.Instance),
                FunctionVal.From(v =>
                {
                    if (v is not FunctionVal f) throw new RuntimeException("initializer 必须是函数");
                    tv.Value.Initializer = f;
                    return VoidVal.Instance;
                })
            );
        });
        Type.DefineMethod("Subtypes", (s, _) =>
        {
            var tv = (TypeVal)s;
            var subs = new List<RuntimeValue>();
            foreach (var t in AllTypes)
            {
                if (t != tv.Value && t.IsAssignableTo(tv.Value))
                    subs.Add(new TypeVal(t));
            }

            return new ListVal(subs);
        });
    }

    private static void RegisterFunctionMethods()
    {
        Function.DefineMethod("Name", (s, a) =>
        {
            var fn = (FunctionVal)s;
            if (a is StringVal sv)
            {
                fn.Name = sv.Value;
                return VoidVal.Instance;
            }

            return new StringVal(fn.Name ?? "");
        });
        Function.DefineMethod("scope", (s, _) =>
        {
            var fn = (FunctionVal)s;
            return new ScopeVal(fn.Scope ?? new Scope());
        });
        Function.DefineMethod("setScope", (s, a) =>
        {
            if (a is not ScopeVal sv) throw new RuntimeException("setScope 需要 Scope 参数");
            ((FunctionVal)s).Scope = sv.Scope;
            return VoidVal.Instance;
        });
        Function.DefineMethod("prepend", (s, a) =>
        {
            if (a is not BlockVal p) throw new RuntimeException("prepend 需要代码块参数");
            return ((FunctionVal)s).Prepend(p);
        });
        Function.DefineMethod("append", (s, a) =>
        {
            if (a is not BlockVal p) throw new RuntimeException("append 需要代码块参数");
            return ((FunctionVal)s).Append(p);
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
                if (tv is not TypeVal t)
                    throw new RuntimeException("scope.Define 需要 type 参数");
                scope.Define(name.Value, t.Value, VoidVal.Instance);
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
    internal static Scope CopyScope(Scope src)
    {
        var dst = new Scope(src.Parent);
        foreach (var kv in src.Variables)
            dst.Define(kv.Key, kv.Value.TypeConstraint, kv.Value.Value);
        return dst;
    }
}
