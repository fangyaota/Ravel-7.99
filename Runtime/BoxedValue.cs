namespace Ravel.Runtime;

public class BoxedValue(RuntimeValue value)
{
    public RuntimeValue Value { get; } = value;


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
                    throw new RuntimeException($"变量 '{name}' 不可读取");
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
                        throw new RuntimeException($"变量 '{name}' 是{(vr.HasAttr("private") ? "私有的" : "受保护的")}");
                }
                if (vr.HasAttr("by"))
                {
                    var prop = vr.Value;
                    var getter = new BoxedValue(prop).GetMember("get").Value;
                    if (getter is RuntimeValue.FunctionVal gf)
                        return new BoxedValue(Step.Run(gf.Trampolined([RuntimeValue.VoidVal.Instance])));
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
                    throw new RuntimeException($"变量 '{name}' 不可读取");
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
                        throw new RuntimeException($"变量 '{name}' 是{(vr.HasAttr("private") ? "私有的" : "受保护的")}");
                }
                if (vr.HasAttr("by"))
                {
                    var prop = vr.Value;
                    var getter = new BoxedValue(prop).GetMember("get").Value;
                    if (getter is RuntimeValue.FunctionVal gf)
                        return new BoxedValue(Step.Run(gf.Trampolined([RuntimeValue.VoidVal.Instance])));
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

        if (Value is RuntimeValue.FunctionVal fn && name == "name")
        {
            return new BoxedValue(fn.Name != null ? new RuntimeValue.StringVal(fn.Name) : RuntimeValue.VoidVal.Instance);
        }
        if (Value is RuntimeValue.TypeVal tv && name == "name")
        {
            return new BoxedValue(new RuntimeValue.StringVal(tv.Value.Name));
        }

        var method = Value.Type.LookupMethod(name);
        var self = Value;
        var bound = RuntimeValue.FunctionVal.FromDirect(args => method(self, args));
        return new BoxedValue(bound);
    }

    public override string ToString() => Value.ToString();
}
