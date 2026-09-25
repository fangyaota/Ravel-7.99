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
        if (OperatorSymbols.IsSymbol(name) && Value.Type.MemberScope.LookupField(name)?.Value is FunctionVal opMethod)
            return new BoxedValue(ObjectVal.BindMethod(opMethod, Value), interp);

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

        // 类对象 / 函数的 `name` 是伪成员,排在字段查找前面 —— 类对象的 `name` 成员存的是空串
        // (`C := class {...}` 没有名字),对外要显示 DisplayName(空名字退化成 "class")。
        // 读写的不对称和以前一致:读显示名、写落成员(见 StepMemberAssign 的 name 特判)。
        if (name == "name")
        {
            if (Value is ObjectVal { IsClass: true } cls) return new BoxedValue(new StringVal(cls.DisplayName), interp);
            if (Value is FunctionVal fn) return new BoxedValue(new StringVal(fn.Name ?? ""), interp);
        }

        // **唯一一次查找**:`MemberScope` 自己知道该怎么找 ——
        // 对象是"自己那层(扁平) → 沿类链兜底",原子值是"只有沿类链那半"(那层是算出来的,
        // 不落地成字段,所以值相等保得住)。机制名的过滤在 MemberView 里,不在这。
        var member = Value.MemberScope.LookupField(name);
        if (member == null)
            throw new RuntimeException($"类型 '{Value.Type.DisplayName}' 没有方法 '{name}'");

        // 门禁只对对象做:借来的类成员表里放的是内置方法,没有 core/private 可言
        if (Value is ObjectVal obj) CheckObjectReadAccess(obj, member, name);
        // 内置方法存的是"self → 结果",读出来要先绑接收者;
        // 实例 scope 里的是已经捕获好作用域的 lambda,原样返回。
        // 判据是 ISelfBinding 而**不是** `is BuiltinMethodVal`:类运算符工厂也要绑,
        // 但绑完是 `BoundClassOp`(推 ClassOp 帧的标记)而不是能直接算的值。
        var v = member.Value;
        return new BoxedValue(
            v is ISelfBinding && v is FunctionVal sf ? ObjectVal.BindMethod(sf, Value) : v,
            interp);
    }

    /// <summary>读变量的通用门禁:unreadable / core / outdated。
    ///
    /// 裸标识符(`Interpreter.StepIdent`)和成员/模块访问必须走同一套,否则门禁是漏的——
    /// 曾经 `c.secret` 被 core 拦住、而类体里直接写 `secret` 却读得到,
    /// 因为 StepIdent 自己抄了一份门禁、漏抄了 core 那条。
    ///
    /// private/protected 不在这里判:那要看「当前作用域能否走到该对象/模块」,得先有 owner。</summary>
    internal static void GateRead(Variable v, string name, Interpreter interp)
    {
        if (v.HasAttr(Attr.Unreadable)) throw new RuntimeException($"变量 '{name}' 不可读取");
        if (v.HasAttr(Attr.Core) && !interp.IsUnsafe)
            throw new RuntimeException($"字段 '{name}' 是核心字段，需要 unsafe");
        if (v.HasAttr(Attr.Outdated)) Console.Error.WriteLine($"[outdated] '{name}' is deprecated");
    }

    /// <summary>模块成员:private/protected 要求当前作用域链能走到该模块</summary>
    private void CheckModuleReadAccess(ModuleVal mv, Variable vr, string name)
    {
        GateRead(vr, name, interp);
        if ((vr.HasAttr(Attr.Private) || vr.HasAttr(Attr.Protected)) && !IsInsideModule(mv))
            throw AccessDenied(vr, name);
    }

    private void CheckObjectReadAccess(ObjectVal obj, Variable vr, string name)
    {
        GateRead(vr, name, interp);
        if (!interp.CheckFieldAccess(vr, obj))
            throw AccessDenied(vr, name);
    }

    private bool IsInsideModule(ModuleVal mv)
    {
        for (var cur = interp.CurrentScope; cur != null; cur = cur.Parent)
            if (cur == mv.ModuleScope) return true;
        return false;
    }

    private static RuntimeException AccessDenied(Variable vr, string name)
        => new($"变量 '{name}' 是{(vr.HasAttr(Attr.Private) ? "私有的" : "受保护的")}");

    public override string ToString() => Value.ToString();
}
