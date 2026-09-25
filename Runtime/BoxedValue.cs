namespace Ravel.Runtime;

/// <summary>成员访问包装器:把「取成员」的门禁(unreadable / outdated / core / private-protected)
/// 集中在这里。门禁要看当前动态作用域和 unsafe 深度,所以每个实例都持有解释器。</summary>
public class BoxedValue(RuntimeValue value, Interpreter interp)
{
    public RuntimeValue Value { get; } = value;

    /// <summary>若 value 的 name 成员是 by 属性,返回其 getter(已做访问控制);否则返回 null。调用方走 CallInto 派发</summary>
    public static FunctionVal? TryGetByGetter(Interpreter interp, RuntimeValue value, string name)
    {
        if (value is ObjectVal obj)
        {
            var vr = obj.Scope.LookupField(name);
            if (vr == null || !vr.HasAttr(Attr.By)) return null;
            new BoxedValue(obj, interp).CheckObjectReadAccess(obj, vr, name);
            return new BoxedValue(vr.Value, interp).GetMember("get").Value as FunctionVal;
        }

        if (value is ModuleVal mv)
        {
            if (!mv.ModuleScope.Contains(name)) return null;
            var vr = mv.ModuleScope.Lookup(name);
            if (!vr.HasAttr(Attr.By)) return null;
            new BoxedValue(mv, interp).CheckModuleReadAccess(mv, vr, name);
            return new BoxedValue(vr.Value, interp).GetMember("get").Value as FunctionVal;
        }

        return null;
    }

    public BoxedValue GetMember(string name)
    {
        // 运算符访问：`1.+` / `"a".==` → 返回绑好 self 的函数。类型层注册的(内置或类运算符)优先。
        if (OperatorSymbols.IsSymbol(name) && Value.Type.TryLookupMethod(name) is { } opMethod)
            return new BoxedValue(RuntimeType.BindMethod(opMethod, Value), interp);

        if (Value is PropertyVal pv)
        {
            if (name == "get") return new BoxedValue(pv.Getter, interp);
            if (name == "set") return new BoxedValue(pv.Setter, interp);
        }

        if (Value is ModuleVal mv && mv.ModuleScope.Contains(name))
        {
            var vr = mv.ModuleScope.Lookup(name);
            CheckModuleReadAccess(mv, vr, name);
            return new BoxedValue(vr.Value, interp);
        }

        if (Value is ObjectVal obj)
        {
            var vr = obj.Scope.LookupField(name);
            if (vr != null)
            {
                CheckObjectReadAccess(obj, vr, name);
                return new BoxedValue(vr.Value, interp);
            }
        }

        // 类型名 / 函数名:字段之外的两个伪成员
        if (name == "name")
        {
            if (Value is TypeVal tv) return new BoxedValue(new StringVal(tv.Value.DisplayName), interp);
            if (Value is FunctionVal fn) return new BoxedValue(new StringVal(fn.Name ?? ""), interp);
        }

        var method = Value.Type.LookupMethod(name);
        return new BoxedValue(RuntimeType.BindMethod(method, Value), interp);
    }

    /// <summary>模块成员:private/protected 要求当前作用域链能走到该模块</summary>
    private void CheckModuleReadAccess(ModuleVal mv, Variable vr, string name)
    {
        CheckUnreadable(vr, name);
        WarnIfOutdated(vr, name);
        if ((vr.HasAttr(Attr.Private) || vr.HasAttr(Attr.Protected)) && !IsInsideModule(mv))
            throw AccessDenied(vr, name);
    }

    private void CheckObjectReadAccess(ObjectVal obj, Variable vr, string name)
    {
        CheckUnreadable(vr, name);
        if (vr.HasAttr(Attr.Core) && interp.UnsafeDepth == 0)
            throw new RuntimeException($"变量 '{name}' 是核心字段，需要 unsafe");
        WarnIfOutdated(vr, name);
        if (!interp.CheckFieldAccess(vr, obj))
            throw AccessDenied(vr, name);
    }

    private bool IsInsideModule(ModuleVal mv)
    {
        for (var cur = interp.CurrentScope; cur != null; cur = cur.Parent)
            if (cur == mv.ModuleScope) return true;
        return false;
    }

    private static void CheckUnreadable(Variable vr, string name)
    {
        if (vr.HasAttr(Attr.Unreadable))
            throw new RuntimeException($"变量 '{name}' 不可读取");
    }

    private static void WarnIfOutdated(Variable vr, string name)
    {
        if (vr.HasAttr(Attr.Outdated))
            Console.Error.WriteLine($"[outdated] '{name}' is deprecated");
    }

    private static RuntimeException AccessDenied(Variable vr, string name)
        => new($"变量 '{name}' 是{(vr.HasAttr(Attr.Private) ? "私有的" : "受保护的")}");

    public override string ToString() => Value.ToString();
}
