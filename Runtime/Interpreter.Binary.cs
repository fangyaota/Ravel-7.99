namespace Ravel.Runtime;

public partial class Interpreter
{
    // ======================== 二元 ========================

    private void StepBinary(NodeFrame nf, BinaryExpr bin)
    {
        // 成员赋值 a.x = v / 成员定义 a.x := v
        if (bin is { Left: MemberAccess ma } && bin.Op is "=" or ":=")
        {
            StepMemberAssign(nf, bin, ma);
            return;
        }

        // 变量赋值在**表达式位置**也成立(`print (x = 5)` / `f (x = 5)`):
        // 左边是名字,不必求它的值。从前这种形状落到 StepBinaryOp 的 `=` 分支 ——
        // 那里**只交出右值、根本不赋值**:写得出来、跑得过去、什么都没发生。
        if (bin is { Left: IdentifierExpr id } && bin.Op == "=")
        {
            StepAssign(nf, id.Name, bin.Right);
            return;
        }

        // 定义不在这里:变量的 `:=` 是**语句**(见 ParseDefinition),写成表达式就是说错了
        if (bin is { Left: IdentifierExpr } && bin.Op == ":=")
            throw new RuntimeException(Messages.T("runtime.eval.definition_used_as_expression"));

        // 成员复合赋值 a.x += v —— 要留住对象,不能走下面「先求 a.x 的值」那条路
        if ((bin.Op is "+=" or "-=" or "*=" or "/=" or "%=") && bin.Left is MemberAccess mac)
        {
            StepCompoundAssign(nf, bin, mac);
            return;
        }

        if (bin.Op is "&&" or "||")
        {
            StepShortCircuit(nf, bin);
            return;
        }

        if (nf.Count == 0)
        {
            PushChild(nf, bin.Left);
            return;
        }

        if (nf.Count == 1)
        {
            PushChild(nf, bin.Right);
            return;
        }

        StepBinaryOp(nf, bin);
    }

    private void StepShortCircuit(NodeFrame nf, BinaryExpr bin)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, bin.Left);
            return;
        }

        if (nf.Count == 1)
        {
            var left = nf.Result(0);
            if (left is not BoolVal lb) throw new RuntimeException(bin.Op + " 左边必须是 bool", ErrorKind.Type);
            if (bin.Op == "&&" && !lb.Value)
            {
                Return(nf, left);
                return;
            }

            if (bin.Op == "||" && lb.Value)
            {
                Return(nf, left);
                return;
            }

            PushChild(nf, bin.Right);
            return;
        }

        var right = nf.Result(1);
        if (right is not BoolVal) throw new RuntimeException(bin.Op + " 右边必须是 bool", ErrorKind.Type);
        Return(nf, right);
    }

    private (FunctionVal Fn, bool Builtin) BindOperator(RuntimeValue left, string op)
    {
        var fn = left.MemberScope.LookupField(op)?.Value as FunctionVal;
        if (fn == null && left is ObjectVal o && HasTraitOperator(o, op))
            return (new BoundTraitOp(o, op), false);      // 槽运算符:两级,交给 TraitOp 帧

        if (fn == null)
            throw new RuntimeException(Messages.T("runtime.operator.unsupported", left.Type, op), ErrorKind.Type);

        // **这儿不绑接收者**:内置那台(`1 + 2`)可以直接 `Impl(left, right)` 算掉,
        // 绑一次就得多分配一个闭包 —— 二元运算是解释器里最热的一格,不白花。
        // 真要绑(推帧那条路)的时候再调 `BindSelf`。
        return (fn, fn is BuiltinMethodVal);
    }

    private static FunctionVal BindSelf(FunctionVal fn, RuntimeValue self)
        => fn is ISelfBinding ? ObjectVal.BindMethod(fn, self) : fn;

    private bool HasTraitOperator(ObjectVal self, string op)
        => (self.Scope.LookupField(op) is { } own && own.HasAttr(Attr.By))
           || BuiltinClasses.TraitSlot(this, self, op) != null;

    private void StepRange(NodeFrame nf, RangeExpr rng)
    {
        var wantLo = rng.Lo is not null;
        var wantHi = rng.Hi is not null;
        var need = (wantLo ? 1 : 0) + (wantHi ? 1 : 0);      // 要求值的端点数(省掉的不算)

        if (nf.Count < need)
        {
            if (wantLo && nf.Count == 0) PushChild(nf, rng.Lo!);
            else PushChild(nf, rng.Hi!);
            return;
        }

        // 求过的那几头按顺序落在 Result(0..) 上;省了起点就只剩一个。
        // 省的是**哪一头**就补**那一头**的无穷:`[1..]` 上界省了 = 到 +∞(`[..1]` 反过来)
        var lo = wantLo ? nf.Result(0) : NegativeInfinity;
        var hi = wantHi ? nf.Result(wantLo ? 1 : 0) : PositiveInfinity;

        // 端点收**任何数值**(int / bigint / real / fraction / bigfraction,混着也行)——
        // 元素是"区间里的整数",所以端点带小数照样能枚举(见 RangeVal)
        if (!BuiltinClasses.TryAsDouble(lo, out _) || !BuiltinClasses.TryAsDouble(hi, out _))
            throw new RuntimeException(
                Messages.T("runtime.eval.range_endpoint_not_numeric", lo.Type, hi.Type), ErrorKind.Type);

        Return(nf, new RangeVal(lo, hi, rng.StartClosed, rng.EndClosed));
    }

    private static readonly RealVal PositiveInfinity = new(double.PositiveInfinity);
    private static readonly RealVal NegativeInfinity = new(double.NegativeInfinity);

    private void StepBinaryOp(NodeFrame nf, BinaryExpr bin)
    {
        var left = nf.Result(0);
        var right = nf.Result(1);
        // `=` 左边不是变量也不是字段(字面量、调用结果…) —— 赋值没地方落。
        // 从前这里是 `Return(nf, right)`:交出右值、什么都不写,静默得最彻底的一个。
        if (bin.Op == "=")
            throw new RuntimeException(Messages.T("runtime.eval.assignment_target_invalid"));

        if (bin.Op is "+=" or "-=" or "*=" or "**=" or "/=" or "%=")
        {
            StepCompoundAssignVar(nf, bin, left, right);
            return;
        }

        var (fn, builtin) = BindOperator(left, bin.Op);

        // 判定类运算符的接口兜底:实现住在**当前作用域**里,而内置运算符的体是纯 C#(拿不到解释器),
        // 所以这一半只能挂在这儿 —— 判据本身在 BuiltinClasses.HasTrait,和实现那条查找共用一份。
        // 只接**内置**那一支:类里写过 `<: := f` 的照旧走它自己的实现(实例 scope 里那枚)。
        // `:` 看**值**,`<:` / `:>` 看**类型**(两边都得是类型对象)。
        //
        // `:` 不给自定义(不在 `OperatorSymbols.All` 里),所以这一支只有内置这一种来路 ——
        // 要取反就写 `!(x: T)`(从前有个专门的 `isnot`)。
        if (builtin && bin.Op is "<:" or ":>" && right is ObjectVal rt
            && left is ObjectVal { IsClass: true } lt && rt.IsClass)
        {
            var (x, y) = bin.Op == "<:" ? (lt, rt) : (rt, lt);
            Return(nf, new BoolVal(BuiltinClasses.MatchesType(this, x, y)));
            return;
        }

        // 内置那台直接算(见 BindOperator):`Invoke(interp, left, right)` 就是绑完再调的结果。
        // **解释器要传进去** —— 少数运算符(`:`)的体要问"当前作用域里有没有生效的实现"。
        if (fn is BuiltinMethodVal bm) Return(nf, bm.Invoke(this, left, right));
        else CallInto(nf.Parent!, BindSelf(fn, left), right);
    }

    private static string BaseOp(string compound)
        => compound == "**=" ? "**" : compound[..1];

    private void StepCompoundAssignVar(NodeFrame nf, BinaryExpr bin, RuntimeValue left, RuntimeValue right)
    {
        if (bin.Left is not IdentifierExpr target)
            throw new RuntimeException(Messages.T("runtime.eval.compound_assignment_target_invalid"));
        var (fn, builtin) = BindOperator(left, BaseOp(bin.Op));
        if (fn is not BuiltinMethodVal bm)
        {
            PushCallAssign(nf, BindSelf(fn, left), right, target.Name);
            return;
        }

        var r = bm.Invoke(this, left, right);
        nf.Scope.Assign(target.Name, r);
        Return(nf, r);
    }

    private void StepCompoundAssign(NodeFrame nf, BinaryExpr bin, MemberAccess ma)
    {
        var op = BaseOp(bin.Op);
        if (nf.Count == 0) { PushChild(nf, ma.Object); return; }
        if (nf.Count == 1) { PushChild(nf, bin.Right); return; }

        if (!nf.Result(0).HasOwnMembers)
            throw new RuntimeException(Messages.T("runtime.eval.compound_assignment_no_own_members", nf.Result(0).Type), ErrorKind.Type);
        var ov = (ObjectVal)nf.Result(0);

        var own = ov.Scope.LookupField(ma.Member) ?? BuiltinClasses.ClassBySlot(ov, ma.Member);
        var hit = own == null ? BuiltinClasses.TraitSlot(this, ov, ma.Member, allowPlain: true) : null;
        var field = own ?? hit?.Slot ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'", ErrorKind.Attribute);
        CheckMemberAccess(field, ov, ma.Member);

        if (field.HasAttr(Attr.By))
        {
            // 接口那条槽要绑到这一次的接收者上(实现身上不再存"当前实例")
            StepByCompoundAssign(nf, op, field, PropOf(field, hit, ov), ma.Member);
            return;
        }

        if (nf.Count == 2)
        {
            var (fn, _) = BindOperator(field.Value, op);
            if (fn is BuiltinMethodVal bm)
            {
                var r = bm.Invoke(this, field.Value, nf.Result(1));
                field.Assign(r);
                Return(nf, r);
                return;
            }

            CallInto(nf, BindSelf(fn, field.Value), nf.Result(1));
            return;
        }

        // count 3:类运算符算完了 → 写回
        field.Assign(nf.Result(2));
        Return(nf, nf.Result(2));
    }

    private RuntimeValue PropOf(Variable field, TraitHit? hit, RuntimeValue self)
        => hit is { } h && field.HasAttr(Attr.By) ? BuiltinClasses.Activate(this, h)
                                                  : BuiltinClasses.BindProperty(field.Value, self);

    private void StepByCompoundAssign(NodeFrame nf, string op, Variable field, RuntimeValue prop, string member)
    {
        if (nf.Count == 2)
        {
            CallInto(nf, PropertyGetter(prop, member), VoidVal.Instance);
            return;
        }

        if (nf.Count == 3)
        {
            var (fn, _) = BindOperator(nf.Result(2), op);
            CallInto(nf, BindSelf(fn, nf.Result(2)), nf.Result(1));
            return;
        }

        if (nf.Count == 4)
        {
            // 算好的新值只过 readonly —— 类型不再查(赋值一律不查)
            // (和普通赋值一条路,见 WriteVariable)
            field.CheckWritable();
            CallInto(nf, PropertySetter(prop, member), nf.Result(3));
            return;
        }

        // count 5:setter 也跑完了 → 复合赋值表达式的值是新值
        Return(nf, nf.Result(3));
    }

    private void CheckMemberAccess(Variable field, ObjectVal obj, string member)
    {
        if (field.HasAttr(Attr.Core) && !IsUnsafe)
            throw new RuntimeException(Messages.T("runtime.member.core_field_requires_unsafe", member), ErrorKind.Access);
        if (field.HasAttr(Attr.Internal))
            throw new RuntimeException(Messages.T("runtime.member.internal_field_not_writable", member, member), ErrorKind.Access);
        if (!CheckFieldAccess(field, obj))
            throw new RuntimeException(Messages.T("runtime.member.field_not_accessible", member, (field.HasAttr(Attr.Private) ? "私有的" : "受保护的")), ErrorKind.Access);
    }

    private void StepMemberAssign(NodeFrame nf, BinaryExpr bin, MemberAccess ma)
    {
        var isDefine = bin.Op == ":=";
        if (nf.Count == 0)
        {
            PushChild(nf, ma.Object);
            return;
        }

        if (nf.Count == 1)
        {
            var obj = nf.Result(0);
            if (obj is FunctionVal fn && ma.Member == ObjectVal.NameMember)
            {
                PushChild(nf, bin.Right);
                return;
            }

            // 判据是"有没有**自己**的成员表",不是"是不是 ObjectVal":
            // 原子值借的是类那层的表,往里写等于改掉整个类型,所以按只读挡回去。
            if (!obj.HasOwnMembers)
                throw new RuntimeException(Messages.T("runtime.member.value_type_has_no_members", obj.Type), ErrorKind.Type);
            var ov = (ObjectVal)obj;
            // `:=` 是定义:字段不存在也放行(到 count==2 时新建)。已存在的字段照样受 core/访问控制约束,
            // 否则 `:=` 就成了绕过封装的万能钥匙。
            // 本层没有就问接口实现:作用域里有生效的实现时,`u.a = 1` 落在实现那条槽上。
            // 这一阶段只要那格变量(存在性 + 门禁),**不建激活格** —— 真写的时候(count==2)才绑接收者。
            var ownF = ov.Scope.LookupField(ma.Member) ?? BuiltinClasses.ClassBySlot(ov, ma.Member);
            var field = ownF ?? (ownF == null ? BuiltinClasses.TraitSlot(this, ov, ma.Member, allowPlain: true)?.Slot : null);
            if (field == null)
            {
                if (!isDefine) throw new RuntimeException(Messages.T("runtime.member.unknown_field", ma.Member), ErrorKind.Attribute);
            }
            else
            {
                CheckMemberAccess(field, ov, ma.Member);
            }

            PushChild(nf, bin.Right);
            return;
        }

        var obj2 = nf.Result(0);
        var rv = nf.Result(1);
        if (obj2 is FunctionVal fn2 && ma.Member == ObjectVal.NameMember)
        {
            fn2.Name = As<StringVal>(rv, "函数名").Value;
            Return(nf, rv);
            return;
        }

        var ov2 = (ObjectVal)obj2;

        // 定义:不存在就新建,已存在就整条替换(类型约束/属性随之更新,与变量层的 x := v 一致)
        if (isDefine)
        {
            ov2.Scope.DefineOrReplace(ma.Member, rv);
            Return(nf, rv);
            return;
        }

        // count==1 已查过字段存在,这里再兜一次:字段在右侧求值期间被删掉时不至于 NRE
        // (接口实现那条也再兜一次:两次都只算"当前作用域里有没有生效的实现",结果一致;
        //  接收者就是 nf.Result(0) 那个,所以两次各建一份激活格是等价的)
        var own2 = ov2.Scope.LookupField(ma.Member) ?? BuiltinClasses.ClassBySlot(ov2, ma.Member);
        var hit2 = own2 == null ? BuiltinClasses.TraitSlot(this, ov2, ma.Member, allowPlain: true) : null;
        var field2 = own2 ?? hit2?.Slot ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'", ErrorKind.Attribute);
        WriteVariable(nf, field2, rv, PropOf(field2, hit2, ov2));
    }
}
