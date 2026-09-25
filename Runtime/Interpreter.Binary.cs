namespace Ravel.Runtime;

/// <summary>二元运算与赋值:算术/比较走类型方法表,`=` 分派给变量或成员写入。
/// 节点状态机见 Interpreter.Nodes.cs。</summary>
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
            if (left is not BoolVal lb) throw new RuntimeException(bin.Op + " 左边必须是 bool");
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
        if (right is not BoolVal) throw new RuntimeException(bin.Op + " 右边必须是 bool");
        Return(nf, right);
    }

    private void StepBinaryOp(NodeFrame nf, BinaryExpr bin)
    {
        var left = nf.Result(0);
        var right = nf.Result(1);
        if (bin.Op == "=")
        {
            Return(nf, right);
            return;
        }

        if (bin.Op is "+=" or "-=" or "*=" or "/=" or "%=")
        {
            var op = bin.Op[..1];
            var fn = left.Type.TryLookupMethod(op);
            if (fn == null) throw new RuntimeException($"类型 {left.Type} 不支持运算符 '{op}'");
            var bound = RuntimeType.BindMethod(fn, left);
            if (fn is not BuiltinMethodVal)
            {
                var target = bin.Left is IdentifierExpr id2 ? id2.Name : null;
                if (target == null) throw new RuntimeException("复合赋值目标必须是变量");
                PushCallAssign(nf, bound, right, target);
                return;
            }

            var r = bound.Body(right);
            if (bin.Left is IdentifierExpr id) nf.Scope.Assign(id.Name, r);
            else throw new RuntimeException("复合赋值目标必须是变量");
            Return(nf, r);
            return;
        }

        var builtin = left.Type.TryLookupMethod(bin.Op);
        if (builtin == null) throw new RuntimeException($"未知的二元运算符: {bin.Op}");
        var bound2 = RuntimeType.BindMethod(builtin, left);
        if (builtin is BuiltinMethodVal)
            Return(nf, bound2.Body(right));
        else
            CallInto(nf.Parent!, bound2, right);
    }

    /// <summary>成员复合赋值 `a.b += v`:先求对象(只求一次),再读成员、算、写回。
    /// 普通字段:0=求对象 1=求右值 2=读成员并算(内置可直接写回) 3=类运算符算完写回。
    /// by 属性多两步(读过 getter、写过 setter),见 StepByCompoundAssign。</summary>
    private void StepCompoundAssign(NodeFrame nf, BinaryExpr bin, MemberAccess ma)
    {
        var op = bin.Op[..1];
        if (nf.Count == 0) { PushChild(nf, ma.Object); return; }
        if (nf.Count == 1) { PushChild(nf, bin.Right); return; }

        if (nf.Result(0) is not ObjectVal ov) throw new RuntimeException("复合赋值的字段目标必须是对象");

        var field = ov.Scope.LookupField(ma.Member)
                    ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'");
        CheckMemberAccess(field, ov, ma.Member);

        if (field.HasAttr(Attr.By))
        {
            StepByCompoundAssign(nf, op, field, ma.Member);
            return;
        }

        if (nf.Count == 2)
        {
            var fn = field.Value.Type.TryLookupMethod(op)
                     ?? throw new RuntimeException($"类型 {field.Value.Type} 不支持运算符 '{op}'");
            var bound = RuntimeType.BindMethod(fn, field.Value);

            if (fn is BuiltinMethodVal)
            {
                var r = bound.Body(nf.Result(1));
                field.Assign(r);
                Return(nf, r);
                return;
            }

            CallInto(nf, bound, nf.Result(1));
            return;
        }

        // count 3:类运算符算完了 → 写回
        field.Assign(nf.Result(2));
        Return(nf, nf.Result(2));
    }

    /// <summary>by 属性的复合赋值:左值要过 getter、结果要过 setter,两个子求值各占一个阶段
    /// (都走 CallInto,内置和类运算符一视同仁,阶段数就固定了)。
    /// 2=推 getter 3=读回左值再算 4=把算好的值推给 setter 5=返回算好的值。</summary>
    private void StepByCompoundAssign(NodeFrame nf, string op, Variable field, string member)
    {
        var prop = field.Value;

        if (nf.Count == 2)
        {
            if (new BoxedValue(prop, this).GetMember("get").Value is not FunctionVal getter)
                throw new RuntimeException($"字段 '{member}' 的 getter 不是函数");
            CallInto(nf, getter, VoidVal.Instance);
            return;
        }

        if (nf.Count == 3)
        {
            var left = nf.Result(2);
            var fn = left.Type.TryLookupMethod(op)
                     ?? throw new RuntimeException($"类型 {left.Type} 不支持运算符 '{op}'");
            CallInto(nf, RuntimeType.BindMethod(fn, left), nf.Result(1));
            return;
        }

        if (nf.Count == 4)
        {
            if (new BoxedValue(prop, this).GetMember("set").Value is not FunctionVal setter)
                throw new RuntimeException($"字段 '{member}' 的 setter 不是函数");
            CallInto(nf, setter, nf.Result(3));
            return;
        }

        // count 5:setter 也跑完了 → 复合赋值表达式的值是新值
        Return(nf, nf.Result(3));
    }

    /// <summary>成员写入前的门禁:core 需要 unsafe,private/protected 看访问控制。</summary>
    private void CheckMemberAccess(Variable field, ObjectVal obj, string member)
    {
        if (field.HasAttr(Attr.Core) && UnsafeDepth == 0)
            throw new RuntimeException($"字段 '{member}' 是核心字段，需要 unsafe");
        if (!CheckFieldAccess(field, obj))
            throw new RuntimeException($"字段 '{member}' 是{(field.HasAttr(Attr.Private) ? "私有的" : "受保护的")}");
    }

    /// <summary>成员写入。`=` 是赋值(字段必须已存在),`:=` 是定义(不存在就新建、存在就整条替换)。</summary>
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
            if (obj is FunctionVal fn && ma.Member == "name")
            {
                PushChild(nf, bin.Right);
                return;
            }

            if (obj is not ObjectVal ov) throw new RuntimeException("无法给非对象设置字段");
            // `:=` 是定义:字段不存在也放行(到 count==2 时新建)。已存在的字段照样受 core/访问控制约束,
            // 否则 `:=` 就成了绕过封装的万能钥匙。
            var field = ov.Scope.LookupField(ma.Member);
            if (field == null)
            {
                if (!isDefine) throw new RuntimeException($"对象没有字段 '{ma.Member}'");
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
        if (obj2 is FunctionVal fn2 && ma.Member == "name")
        {
            fn2.Name = As<StringVal>(rv, "函数名").Value;
            Return(nf, rv);
            return;
        }

        var ov2 = (ObjectVal)obj2;

        // 定义:不存在就新建,已存在就整条替换(类型约束/属性随之更新,与变量层的 x := v 一致)
        if (isDefine)
        {
            ov2.Scope.DefineOrReplace(ma.Member, rv.Type, rv);
            Return(nf, rv);
            return;
        }

        // count==1 已查过字段存在,这里再兜一次:字段在右侧求值期间被删掉时不至于 NRE
        var field2 = ov2.Scope.LookupField(ma.Member)
                     ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'");
        if (field2.HasAttr(Attr.By))
        {
            var setter = new BoxedValue(field2.Value, this).GetMember("set").Value;
            if (setter is FunctionVal sf)
            {
                PushCallReturn(nf.Parent!, sf, rv, rv);
                return;
            }

            Return(nf, rv);
            return;
        }

        field2.Assign(rv);
        Return(nf, rv);
    }}
