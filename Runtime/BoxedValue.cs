namespace Ravel.Runtime;

public class BoxedValue(RuntimeValue value)
{
    public RuntimeValue Value { get; } = value;

    /// <summary>若 value 的 name 成员是 by 属性,返回其 getter(已做访问控制);否则返回 null。调用方走 CallInto 派发</summary>
    public static FunctionVal? TryGetByGetter(RuntimeValue value, string name)
    {
        if (value is ObjectVal obj)
        {
            var vr = obj.Scope.LookupField(name);
            if (vr == null || !vr.HasAttr("by")) return null;
            CheckObjectReadAccess(obj, vr, name);
            return new BoxedValue(vr.Value).GetMember("get").Value as FunctionVal;
        }

        if (value is ModuleVal mv)
        {
            if (!mv.ModuleScope.Contains(name)) return null;
            var vr = mv.ModuleScope.Lookup(name);
            if (!vr.HasAttr("by")) return null;
            CheckModuleReadAccess(mv, vr, name);
            return new BoxedValue(vr.Value).GetMember("get").Value as FunctionVal;
        }

        return null;
    }

    public BoxedValue GetMember(string name)
    {
        // 内置类型运算符访问：1.operator+ → 返回 (rhs) => 1 + rhs
        if (name.StartsWith("operator"))
        {
            var op = name[8..];
            var opMethod = Value.Type.TryLookupMethod(op);
            if (opMethod != null)
                return new BoxedValue(RuntimeType.BindMethod(opMethod, Value));
        }

        if (Value is PropertyVal pv)
        {
            if (name == "get") return new BoxedValue(pv.Getter);
            if (name == "set") return new BoxedValue(pv.Setter);
        }

        if (Value is ModuleVal mv)
        {
            if (mv.ModuleScope.Contains(name))
            {
                var vr = mv.ModuleScope.Lookup(name);
                CheckModuleReadAccess(mv, vr, name);
                return new BoxedValue(vr.Value);
            }
        }

        if (Value is ObjectVal obj)
        {
            var vr = obj.Scope.LookupField(name);
            if (vr != null)
            {
                CheckObjectReadAccess(obj, vr, name);
                return new BoxedValue(vr.Value);
            }
        }

        if (Value is TypeVal tv && name == "name")
        {
            return new BoxedValue(new StringVal(tv.Value.Name));
        }

        if (Value is FunctionVal fn && name == "name")
        {
            return new BoxedValue(new StringVal(fn.Name ?? ""));
        }

        var method = Value.Type.LookupMethod(name);
        return new BoxedValue(RuntimeType.BindMethod(method, Value));
    }

    private static void CheckModuleReadAccess(ModuleVal mv, Variable vr, string name)
    {
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
                if (cur == mv.ModuleScope)
                {
                    ok = true;
                    break;
                }

                cur = cur.Parent;
            }

            if (!ok)
                throw new RuntimeException($"变量 '{name}' 是{(vr.HasAttr("private") ? "私有的" : "受保护的")}");
        }
    }

    private static void CheckObjectReadAccess(ObjectVal obj, Variable vr, string name)
    {
        if (vr.HasAttr("unreadable"))
            throw new RuntimeException($"变量 '{name}' 不可读取");
        if (vr.HasAttr("core") && Interpreter.Current!.UnsafeDepth == 0)
            throw new RuntimeException($"变量 '{name}' 是核心字段，需要 unsafe");
        if (vr.HasAttr("outdated"))
            Console.Error.WriteLine($"[outdated] '{name}' is deprecated");
        if (!Interpreter.CheckFieldAccess(vr, obj))
            throw new RuntimeException($"变量 '{name}' 是{(vr.HasAttr("private") ? "私有的" : "受保护的")}");
    }

    public override string ToString() => Value.ToString();
}
