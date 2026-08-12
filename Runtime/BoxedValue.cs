namespace Ravel.Runtime;

public class BoxedValue
{
    public RuntimeValue Value { get; }

    public BoxedValue(RuntimeValue value) { Value = value; }

    public BoxedValue GetMember(string name)
    {
        // 内置类型运算符访问：1.operator+ → 返回 (rhs) => 1 + rhs
        if (name.StartsWith("operator"))
        {
            var op = name[8..];
            var builtin = RuntimeType.GetBuiltinOperator(Value.Type, op);
            if (builtin != null)
            {
                var captured = Value;
                return new BoxedValue(RuntimeValue.FunctionVal.FromDirect(args =>
                    builtin(captured, args[0])));
            }
        }

        // base 引用：init → 父类 init 绑定实例；其他 → 实例字段
        if (Value is RuntimeValue.BaseRef br)
        {
            if (name == "init" && br.ParentMeta.Init is RuntimeValue.FunctionVal pInit)
            {
                var pThis = br.ParentMeta.ThisVar;
                var inst = br.Instance;
                return new BoxedValue(RuntimeValue.FunctionVal.FromTrampolined(pa =>
                {
                    var savedThis = pThis!.Value;
                    var savedScope = pInit.Scope;
                    pThis.Assign(inst);
                    // 父 init 在子实例作用域执行，但 base 用 wrapper 保护
                    if (pInit.Scope != null && inst.InstanceScope != null)
                    {
                        var wrapper = new Scope(inst.InstanceScope);
                        if (br.ParentMeta.Parent != null)
                            wrapper.Define("base", RuntimeType.Object,
                                new RuntimeValue.BaseRef(inst, br.ParentMeta.Parent));
                        else
                            wrapper.Define("base", RuntimeType.Object,
                                inst.InstanceScope.Lookup("base").Value);
                        pInit.Scope = wrapper;
                    }
                    var step = pInit.Trampolined != null ? pInit.Trampolined(pa) : new Done(pInit.Direct!(pa));
                    return Interpreter.Then(step, v => {
                        pThis.Assign(savedThis);
                        if (pInit.Scope != null) pInit.Scope = savedScope;
                        return Interpreter.D(v);
                    });
                }));
            }
            var cur = br.Instance is RuntimeValue.ObjectVal ov ? ov : throw new RuntimeException($"base.Instance is {br.Instance.GetType().Name}");
            while (cur != null)
            {
                if (cur.Fields.TryGetValue(name, out var f2)) return new BoxedValue(f2);
                cur = cur.Parent;
            }
        }


        if (Value is RuntimeValue.PropertyVal pv)
        {
            if (name == "get") return new BoxedValue(pv.Getter);
            if (name == "set") return new BoxedValue(pv.Setter);
        }

        if (Value is RuntimeValue.ModuleVal mv)
        {
            if (mv.ModuleScope.Contains(name))
            {
                var vr = mv.ModuleScope.Lookup(name);
                if (vr.HasAttr("unreadable"))
                    throw new RuntimeException($"Variable '{name}' is unreadable");
                if (vr.HasAttr("outdated"))
                    Console.Error.WriteLine($"[outdated] '{name}' is deprecated");
                if (vr.HasAttr("private") || vr.HasAttr("protected"))
                {
                    var cur = Interpreter.Current?.CurrentScope;
                    bool ok = false;
                    while (cur != null)
                    {
                        if (cur == mv.ModuleScope) { ok = true; break; }
                        cur = cur.Parent;
                    }
                    if (!ok)
                        throw new RuntimeException($"Variable '{name}' is { (vr.HasAttr("private")?"private":"protected") }");
                }
                if (vr.HasAttr("by"))
                {
                    var prop = vr.Value;
                    var getter = new BoxedValue(prop).GetMember("get").Value;
                    if (getter is RuntimeValue.FunctionVal gf)
                        return new BoxedValue(Step.Run(gf.Trampolined!(new RuntimeValue[] { RuntimeValue.VoidVal.Instance })));
                }
                return new BoxedValue(vr.Value);
            }
        }

        if (Value is RuntimeValue.ObjectVal obj)
        {
            // 构造器运行时 InstanceScope 比 Fields 更新鲜
            if (obj.InstanceScope != null && obj.InstanceScope.Contains(name))
            {
                var vr = obj.InstanceScope.Lookup(name);
                if (vr.HasAttr("unreadable"))
                    throw new RuntimeException($"Variable '{name}' is unreadable");
                if (vr.HasAttr("outdated"))
                    Console.Error.WriteLine($"[outdated] '{name}' is deprecated");
                // 访问控制
                if (vr.HasAttr("private") || vr.HasAttr("protected"))
                {
                    var cur = Interpreter.Current?.CurrentScope;
                    bool ok = false;
                    while (cur != null)
                    {
                        if (cur == obj.InstanceScope) { ok = true; break; }
                        cur = cur.Parent;
                    }
                    if (!ok)
                        throw new RuntimeException($"Variable '{name}' is { (vr.HasAttr("private")?"private":"protected") }");
                    }
                    if (vr.HasAttr("by"))
                    {
                        var prop = vr.Value;
                        var getter = new BoxedValue(prop).GetMember("get").Value;
                        if (getter is RuntimeValue.FunctionVal gf)
                            return new BoxedValue(Step.Run(gf.Trampolined!(new RuntimeValue[] { RuntimeValue.VoidVal.Instance })));
                    }
                    return new BoxedValue(vr.Value);
            }
            // 实例字段
            if (obj.Fields.TryGetValue(name, out var field))
                    return new BoxedValue(field);
            // 类定义字段（Meta 链）
            var m = obj.Meta;
            while (m != null)
            {
                if (m.ClassScope != null)
                {
                    foreach (var kv in m.ClassScope.Variables)
                        if (kv.Key == name)
                            return new BoxedValue(kv.Value.Value);
                }
                m = m.Parent;
            }
        }

        if(Value is RuntimeValue.FunctionVal fn && name=="name"){
            return new BoxedValue(fn.Name!=null ? new RuntimeValue.StringVal(fn.Name) : RuntimeValue.VoidVal.Instance);
        }
        if(Value is RuntimeValue.TypeVal tv && name=="name"){
            return new BoxedValue(new RuntimeValue.StringVal(tv.Value.Name));
        }

        var method = Value.Type.LookupMethod(name);
        var self = Value;
        var bound = RuntimeValue.FunctionVal.FromDirect(args => method(self, args));
        return new BoxedValue(bound);
    }

    public override string ToString() => Value.ToString();
}
