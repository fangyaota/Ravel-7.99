namespace Ravel.Runtime;

/// <summary>成员访问包装器:把「取成员」的门禁(unreadable / outdated / core / private-protected)
/// 集中在这里。门禁要看当前动态作用域和 unsafe 深度,所以每个实例都持有解释器。
///
/// 是**结构体**:它的用法一律是"造一个、调一次、扔掉"(`new BoxedValue(obj, this).GetMember(name)`),
/// 没人拿它当身份、也没人存它 —— 取成员是最热的一条路,每走一次多分配一个对象不值当。</summary>
public readonly struct BoxedValue(RuntimeValue value, Interpreter interp)
{
    public RuntimeValue Value { get; } = value;

    /// <summary>若 value 的 name 成员是 by 属性,返回其 getter(已做访问控制);否则返回 null。调用方走 CallInto 派发。
    ///
    /// 模块和对象查的是同一张表(模块的成员表就是它的作用域),但**门禁不一样** ——
    /// 对象那条是字段的 private/protected 访问控制,模块要的是"当前作用域能不能走到这个模块"
    /// (CheckModuleReadAccess)。所以取值可以合并,门禁必须分开。</summary>
    public static FunctionVal? TryGetByGetter(Interpreter interp, RuntimeValue value, string name)
    {
        // 模块走 `Contains` + `Lookup`:前者只看**本层**,不让查找顺着作用域链漏到外层去。
        // **标量**(数 / 字符串 / 字符…)没有自己的成员表,但它照样能实现接口
        // (`impl (IEnumerable string { … })`)—— 所以这里不再拿 `is ObjectVal` 一刀切,
        // 只是"本层"那一段对它来说恒为空,直接去问接口表。
        var vr = value switch
        {
            ModuleVal mv => mv.Scope.Contains(name) ? mv.Scope.Lookup(name) : null,
            ObjectVal obj => obj.Scope.LookupField(name),
            _ => null,
        };
        // 本层没有这个成员 → 问当前作用域里生效的接口实现(模块不参与:接口实现是给实例用的)。
        // 接口那条槽要**绑到这一次的接收者**上才交出去(实现身上不再存"当前实例",见 Activate)。
        var prop = vr?.Value;
        if (vr == null && value is not ModuleVal && BuiltinClasses.TraitSlot(interp, value, name) is { } hit)
        {
            vr = hit.Slot;
            prop = BuiltinClasses.Activate(interp, hit);
        }

        if (vr == null || !vr.HasAttr(Attr.By)) return null;

        var boxed = new BoxedValue(value, interp);
        // 标量没有字段可言,门禁那套(字段的 private/protected)对它们不适用
        if (value is ModuleVal m) boxed.CheckModuleReadAccess(m, vr, name);
        else if (value is ObjectVal obj) boxed.CheckObjectReadAccess(obj, vr, name);
        return interp.PropertyGetter(prop!, name);
    }

    public BoxedValue GetMember(string name)
    {
        // 运算符访问：`1.+` / `"a".==` → 交回那个运算符函数,和 `BindOperator` 查的是**同一处**
        // (`MemberScope`:值自己那层 → 沿类的 parent 链的实例表)。
        if (OperatorSymbols.IsSymbol(name) && Value.MemberScope.LookupField(name)?.Value is FunctionVal opMethod)
            return new BoxedValue(
                // 引擎挂的要绑接收者;类体里写的那份是普通 lambda(收的是右操作数,见 BindOperator)
                opMethod is ISelfBinding ? ObjectVal.BindMethod(opMethod, Value) : opMethod, interp);

        // `p.Get` / `p.Set` —— 属性值上那两个函数是**特判**出来的,不在任何作用域里
        if (Value is PropertyVal pv)
        {
            if (name == ObjectVal.GetterMember) return new BoxedValue(pv.Getter, interp);
            if (name == ObjectVal.SetterMember) return new BoxedValue(pv.Setter, interp);
        }

        if (Value is ModuleVal mv && mv.Scope.Contains(name))
        {
            var vr = mv.Scope.Lookup(name);
            CheckModuleReadAccess(mv, vr, name);
            return new BoxedValue(vr.Value, interp);
        }

        // 类对象 / 函数的 `name` 是伪成员,排在字段查找前面 —— 类对象的 `name` 成员存的是空串
        // (`C := class {...}` 没有名字),对外要显示 DisplayName(空名字退化成 "class")。
        // 读写的不对称和以前一致:读显示名、写落成员(见 StepMemberAssign 的 name 特判)。
        if (name == ObjectVal.NameMember)
        {
            if (Value is ObjectVal { IsClass: true } cls) return new BoxedValue(new StringVal(cls.DisplayName), interp);
            if (Value is FunctionVal fn) return new BoxedValue(new StringVal(fn.Name ?? ""), interp);
        }

        // **唯一一次查找**:`MemberScope` 自己知道该怎么找 ——
        // 对象是"自己那层(扁平) → 沿类链兜底",原子值是"只有沿类链那半"(那层是算出来的,
        // 不落地成字段,所以值相等保得住)。机制名的过滤在 MemberView 里,不在这。
        var member = Value.MemberScope.LookupField(name);
        if (member == null)
            throw new RuntimeException($"{Value.KindName} 没有方法 '{name}'", ErrorKind.Attribute);

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
        if (v.HasAttr(Attr.Unreadable)) throw new RuntimeException($"变量 '{name}' 不可读取", ErrorKind.Access);
        if (v.HasAttr(Attr.Core) && !interp.IsUnsafe)
            throw new RuntimeException($"字段 '{name}' 是核心字段，需要 unsafe", ErrorKind.Access);
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
            if (cur == mv.Scope) return true;
        return false;
    }

    internal static RuntimeException AccessDenied(Variable vr, string name)
        => new($"变量 '{name}' 是{(vr.HasAttr(Attr.Private) ? "私有的" : "受保护的")}");

    public override string ToString() => Value.ToString();
}
