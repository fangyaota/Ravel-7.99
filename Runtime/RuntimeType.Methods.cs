namespace Ravel.Runtime;

using System.Linq;

public partial class RuntimeType
{
    /// <summary>注册各内置类型的方法</summary>
    private static void RegisterMethods()
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
            // 加类型方法（含继承链）
            var t = s.Type;
            while (true)
            {
                foreach (var n in t.MethodNames)
                    if (seen.Add(n))
                        all.Add(new StringVal(n));
                if (t == t.Parent) break;
                t = t.Parent;
            }

            return new ListVal(all);
        });
        Int.DefineMethod("ToString", (s, _) => new StringVal(((IntVal)s).Value.ToString()));
        String.DefineMethod("Length", (s, _) => new IntVal(((StringVal)s).Value.Length));
        List.DefineMethod("Count", (s, _) => new IntVal(((ListVal)s).Elements.Count));
        List.DefineMethod("At", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.At 需要 int 参数");
            var lst = (ListVal)s;
            if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return lst.Elements[i.Value];
        });
        List.DefineMethod("Add", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("list.Add 需要 1 个参数");
            ((ListVal)s).Elements.Add(a[0]);
            return VoidVal.Instance;
        });
        List.DefineMethod("Remove", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.Remove 需要 int 参数");
            var lst = (ListVal)s;
            if (i.Value < 0 || i.Value >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            var v = lst.Elements[i.Value];
            lst.Elements.RemoveAt(i.Value);
            return v;
        });
        List.DefineMethod("Insert", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.Insert 需要 int 参数");
            var lst = (ListVal)s;
            var idx = i.Value;
            if (idx < 0 || idx > lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("list.Insert 需要值参数");
                lst.Elements.Insert(idx, va[0]);
                return VoidVal.Instance;
            });
        });
        List.DefineMethod("Set", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not IntVal i) throw new RuntimeException("list.Set 需要 int 参数");
            var lst = (ListVal)s;
            var idx = i.Value;
            if (idx < 0 || idx >= lst.Elements.Count) throw new RuntimeException("索引超出范围");
            return FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("list.Set 需要值参数");
                lst.Elements[idx] = va[0];
                return VoidVal.Instance;
            });
        });

        // ---- Set 方法 ----
        Set.DefineMethod("Count", (s, _) => new IntVal(((SetVal)s).Elements.Count));
        Set.DefineMethod("Add", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Add 需要 1 个参数");
            ((SetVal)s).Elements.Add(a[0]);
            return VoidVal.Instance;
        });
        Set.DefineMethod("Remove", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Remove 需要 1 个参数");
            ((SetVal)s).Elements.Remove(a[0]);
            return VoidVal.Instance;
        });
        Set.DefineMethod("Contains", (s, a) =>
        {
            if (a.Length != 1) throw new RuntimeException("set.Contains 需要 1 个参数");
            return new BoolVal(((SetVal)s).Elements.Contains(a[0]));
        });

        // ---- Dict 方法 ----
        Dict.DefineMethod("Count", (s, _) => new IntVal(((DictVal)s).Entries.Count));
        Dict.DefineMethod("Get", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal key) throw new RuntimeException("dict.Get 需要 string 参数");
            var d = (DictVal)s;
            if (d.Entries.TryGetValue(key.Value, out var v)) return v;
            throw new RuntimeException($"键不存在: {key.Value}");
        });
        Dict.DefineMethod("Set", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal key) throw new RuntimeException("dict.Set 需要 string 键");
            return FunctionVal.FromDirect(va =>
            {
                if (va.Length != 1) throw new RuntimeException("dict.Set 需要值参数");
                ((DictVal)s).Entries[key.Value] = va[0];
                return VoidVal.Instance;
            });
        });
        Dict.DefineMethod("Has", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal key) throw new RuntimeException("dict.Has 需要 string 参数");
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

        // type 类型方法
        Type.DefineMethod("name", (s, _) => new StringVal(((TypeVal)s).Value.Name));
        Type.DefineMethod("Parent", (s, _) =>
        {
            var p = ((TypeVal)s).Value.Parent;
            if (p == ((TypeVal)s).Value) return s;
            return new TypeVal(p);
        });
        Type.DefineMethod("Is", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not TypeVal other) throw new RuntimeException("type.Is 需要 type 参数");
            return new BoolVal(((TypeVal)s).Value.IsAssignableTo(other.Value));
        });
        Type.DefineMethod("Default", (s, _) =>
        {
            var tv = (TypeVal)s;
            return ConvertDirect(tv.Value, DefaultVal.Instance);
        });
        Function.DefineMethod("Name", (s, a) =>
        {
            var fn = (FunctionVal)s;
            if (a.Length > 0 && a[0] is StringVal sv)
            {
                fn.Name = sv.Value;
                return VoidVal.Instance;
            }

            return fn.Name != null ? new StringVal(fn.Name) : VoidVal.Instance;
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

        // ---- Scope 方法 ----
        ScopeType.DefineMethod("Push", (s, _) =>
        {
            var scope = ((ScopeVal)s).Scope;
            return new ScopeVal(scope.Push());
        });
        ScopeType.DefineMethod("Define", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal name)
                throw new RuntimeException("scope.Define 需要字符串名称");
            var scope = ((ScopeVal)s).Scope;
            return FunctionVal.FromDirect(b =>
            {
                if (b.Length != 1 || b[0] is not TypeVal tv)
                    throw new RuntimeException("scope.Define 需要 type 参数");
                scope.Define(name.Value, tv.Value, VoidVal.Instance);
                return VoidVal.Instance;
            });
        });
        ScopeType.DefineMethod("Lookup", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not StringVal name)
                throw new RuntimeException("scope.Lookup 需要字符串参数");
            var scope = ((ScopeVal)s).Scope;
            var vr = scope.Lookup(name.Value);
            return new PropertyVal(
                FunctionVal.FromDirect(_ => vr.Value),
                FunctionVal.FromDirect(args =>
                {
                    vr.Assign(args[0]);
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
                if (kv.Key == "this" || kv.Key == "base" || kv.Key == "block" || kv.Key == "thistype") continue;
                var vr = scope.Lookup(kv.Key);
                var getter = FunctionVal.FromDirect(_ => vr.Value);
                var setter = FunctionVal.FromDirect(a =>
                {
                    vr.Assign(a[0]);
                    return VoidVal.Instance;
                });
                d[kv.Key] = new PropertyVal(getter, setter, [.. kv.Value.Attrs]);
            }

            return new DictVal(d);
        });

        // ---- Property 方法 ----
        Property.DefineMethod("Attrs", (s, _) =>
        {
            var pv = (PropertyVal)s;
            return pv.Attrs != null
                ? new ListVal([.. pv.Attrs.Select(a => new StringVal(a))])
                : new ListVal([]);
        });

        // ---- Function 方法 ----
        Function.DefineMethod("scope", (s, _) =>
        {
            var fn = (FunctionVal)s;
            return new ScopeVal(fn.Scope ?? new Scope());
        });
        Function.DefineMethod("setScope", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not ScopeVal sv) throw new RuntimeException("setScope 需要 Scope 参数");
            ((FunctionVal)s).Scope = sv.Scope;
            return VoidVal.Instance;
        });
        Function.DefineMethod("prepend", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not BlockVal p) throw new RuntimeException("prepend 需要代码块参数");
            return ((FunctionVal)s).Prepend(p);
        });
        Function.DefineMethod("append", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not BlockVal p) throw new RuntimeException("append 需要代码块参数");
            return ((FunctionVal)s).Append(p);
        });

        // ---- Type 方法 ----
        Type.DefineMethod("Instantiate", (s, a) =>
        {
            if (a.Length != 1 || a[0] is not ScopeVal sv) throw new RuntimeException("Instantiate 需要 Scope 参数");
            var rt = ((TypeVal)s).Value;
            return new ObjectVal(rt, sv.Scope);
        });
    }

    /// <summary>浅拷贝作用域（with / Copy 用）</summary>
    internal static Scope CopyScope(Scope src)
    {
        var dst = new Scope(src.Parent);
        foreach (var kv in src.Variables)
            dst.Define(kv.Key, kv.Value.TypeConstraint, kv.Value.Value);
        return dst;
    }
}
