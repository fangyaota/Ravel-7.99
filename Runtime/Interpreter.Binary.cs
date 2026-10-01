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
            throw new RuntimeException("变量的定义（':='）是语句，不能当表达式用（赋值才是：'='）");

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

    /// <summary>取左值类型上这个运算符的实现,并绑好接收者。`Builtin` 为真表示它是内置同步方法
    /// ——绑完能当场算,不必推帧;类运算符(`BuiltinClasses.DefineClassOperator`)和
    /// by 属性的 getter/setter 不是,得走 `CallInto`。
    ///
    /// 查的是 `left.MemberScope` —— **和方法调用同一个入口**(值自己那层 → 沿类的 `parent`
    /// 链读实例表)。所以类体里写的那份 `+ := f`(落在每个实例的 scope 里)也找得到,
    /// `ClassOp` 帧只在"类那张表上的工厂"被找到时才走。接收者的类型不同,找到的就是不同那张:
    /// `1 + 2` 找 `Integer` 的、`C1 == C2` 找 `Type` 的、`c1 == c2` 找 `C` 的。
    ///
    /// 找不到只可能是**左边的类型**没定义这个运算符:运算符本身总是先过词法/语法的。
    /// 从前报「未知的二元运算符: *」,读起来像语法写错了,其实该说的是这个类型不支持。</summary>
    private (FunctionVal Bound, bool Builtin) BindOperator(RuntimeValue left, string op)
    {
        var fn = left.MemberScope.LookupField(op)?.Value as FunctionVal;
        if (fn == null && left is ObjectVal o && HasTraitOperator(o, op))
            return (new BoundTraitOp(o, op), false);      // 槽运算符:两级,交给 TraitOp 帧

        if (fn == null)
            throw new RuntimeException($"类型 {left.Type} 不支持运算符 '{op}'");

        // 引擎挂的成员(self → 剩下)要先绑接收者;**用户写在类体里的那份是普通 lambda**
        // ——它收的是右操作数,接收者靠捕获的作用域(`ClassOp` 帧里 `CallInto(cf, impl, arg)`
        // 也是这么调的),原样交出去。`BindMethod` 会去读它的 `Body`,而 `LambdaVal.Body`
        // 是占位(见 LambdaVal 那段:那是"求值器漏了一个 case"的哨兵)。
        var bound = fn is ISelfBinding ? ObjectVal.BindMethod(fn, left) : fn;
        return (bound, fn is BuiltinMethodVal);
    }

    /// <summary>这个对象身上有没有**槽运算符**(`by + := property g s`)。两处:自己那层
    /// (类体/实现体里写的),以及生效中的接口实现(`interface { by + := … }` 落在实现身上)。
    /// 只认 `by` 槽 —— 随手定义的普通 `+ := f` 仍归类运算符那条路(由 `Install` 静态扫出来
    /// 装成 `ClassOperatorFactory`),这里不抢。</summary>
    private bool HasTraitOperator(ObjectVal self, string op)
        => (self.Scope.LookupField(op) is { } own && own.HasAttr(Attr.By))
           || BuiltinClasses.TraitSlot(this, self, op) != null;

    /// <summary>区间 `[1..3]` / `(3..5)` …:两个端点各推一帧(照 <see cref="StepBinary"/>
    /// 那套),收的时候造一个 <see cref="RangeVal"/>。两端都得是 int —— 别的类型当场报错,
    /// 不悄悄转换(`[1.5..3]` 那种想要小数区间的人得自己换一段逻辑)。
    ///
    /// **开闭由语法决定**:括号各带一半的意思,解析器已经把它记在节点上了。</summary>
    private void StepRange(NodeFrame nf, RangeExpr rng)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, rng.Lo);
            return;
        }

        if (nf.Count == 1)
        {
            PushChild(nf, rng.Hi);
            return;
        }

        if (nf.Result(0) is not IntVal lo || nf.Result(1) is not IntVal hi)
            throw new RuntimeException(
                $"Range 的两端需要 int，得到 {nf.Result(0).Type} 与 {nf.Result(1).Type}");

        Return(nf, new RangeVal(lo.Value, hi.Value, rng.StartClosed, rng.EndClosed));
    }

    private void StepBinaryOp(NodeFrame nf, BinaryExpr bin)
    {
        var left = nf.Result(0);
        var right = nf.Result(1);
        // `=` 左边不是变量也不是字段(字面量、调用结果…) —— 赋值没地方落。
        // 从前这里是 `Return(nf, right)`:交出右值、什么都不写,静默得最彻底的一个。
        if (bin.Op == "=")
            throw new RuntimeException("赋值的左边得是个变量名或字段，不能是别的表达式");

        if (bin.Op is "+=" or "-=" or "*=" or "/=" or "%=")
        {
            StepCompoundAssignVar(nf, bin, left, right);
            return;
        }

        var (bound, builtin) = BindOperator(left, bin.Op);

        // 判定类运算符的接口兜底:实现住在**当前作用域**里,而内置运算符的体是纯 C#(拿不到解释器),
        // 所以这一半只能挂在这儿 —— 判据本身在 BuiltinClasses.HasTrait,和实现那条查找共用一份。
        // 只接**内置**那一支:类里写过 `is := f` / `<: := f` 的照旧走它自己的实现(ClassOp 帧)。
        // `is` / `isnot` 看**值**,`<:` / `:>` 看**类型**(两边都得是类型对象)。
        if (builtin && right is ObjectVal rt)
        {
            if (bin.Op is "is" or "isnot")
            {
                var hit = left.Type.IsAssignableTo(rt) || BuiltinClasses.HasTrait(this, left.Type, rt);
                Return(nf, new BoolVal(bin.Op == "is" ? hit : !hit));
                return;
            }

            if (bin.Op is "<:" or ":>" && left is ObjectVal { IsClass: true } lt && rt.IsClass)
            {
                var (x, y) = bin.Op == "<:" ? (lt, rt) : (rt, lt);
                Return(nf, new BoolVal(x.IsAssignableTo(y) || BuiltinClasses.HasTrait(this, x, y)));
                return;
            }
        }

        if (builtin) Return(nf, bound.Body(right));
        else CallInto(nf.Parent!, bound, right);
    }

    /// <summary>变量的复合赋值 `a += v`(字段那种见 <see cref="StepCompoundAssign"/>)。
    /// 目标必须是**变量名**,不是任意表达式 —— 要写回去,得知道写给谁。
    ///
    /// 内置同步方法当场算、当场写;类运算符得推 CallAssign 帧(算完由那个帧写回)。</summary>
    private void StepCompoundAssignVar(NodeFrame nf, BinaryExpr bin, RuntimeValue left, RuntimeValue right)
    {
        if (bin.Left is not IdentifierExpr target)
            throw new RuntimeException("复合赋值目标必须是变量");
        var (bound, builtin) = BindOperator(left, bin.Op[..1]);
        if (!builtin)
        {
            PushCallAssign(nf, bound, right, target.Name);
            return;
        }

        var r = bound.Body(right);
        nf.Scope.Assign(target.Name, r, ViaTrait(r));
        Return(nf, r);
    }

    /// <summary>成员复合赋值 `a.b += v`:先求对象(只求一次),再读成员、算、写回。
    /// 普通字段:0=求对象 1=求右值 2=读成员并算(内置可直接写回) 3=类运算符算完写回。
    /// by 属性多两步(读过 getter、写过 setter),见 StepByCompoundAssign。</summary>
    private void StepCompoundAssign(NodeFrame nf, BinaryExpr bin, MemberAccess ma)
    {
        var op = bin.Op[..1];
        if (nf.Count == 0) { PushChild(nf, ma.Object); return; }
        if (nf.Count == 1) { PushChild(nf, bin.Right); return; }

        if (!nf.Result(0).HasOwnMembers)
            throw new RuntimeException($"复合赋值的字段目标需要有自己的成员，{nf.Result(0).Type} 没有");
        var ov = (ObjectVal)nf.Result(0);

        var own = ov.Scope.LookupField(ma.Member);
        var hit = own == null ? BuiltinClasses.TraitSlot(this, ov, ma.Member) : null;
        var field = own ?? hit?.Slot ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'");
        CheckMemberAccess(field, ov, ma.Member);

        if (field.HasAttr(Attr.By))
        {
            // 接口那条槽要绑到这一次的接收者上(实现身上不再存"当前实例")
            StepByCompoundAssign(nf, op, field, PropOf(field, hit), ma.Member);
            return;
        }

        if (nf.Count == 2)
        {
            var (bound, builtin) = BindOperator(field.Value, op);
            if (builtin)
            {
                var r = bound.Body(nf.Result(1));
                field.Assign(r, ViaTrait(r));
                Return(nf, r);
                return;
            }

            CallInto(nf, bound, nf.Result(1));
            return;
        }

        // count 3:类运算符算完了 → 写回
        field.Assign(nf.Result(2), ViaTrait(nf.Result(2)));
        Return(nf, nf.Result(2));
    }

    /// <summary>取这次要用的那份 property:接口那条槽要**绑到这一次的接收者**上
    /// ([`Activate`]),自己那层声明的原样。非接口的 `by` 槽(`by a := property …`)走的是
    /// `field.Value` —— 它读的是 `this`,没有 `instance` 那回事。</summary>
    private RuntimeValue PropOf(Variable field, TraitHit? hit)
        => hit is { } h ? BuiltinClasses.Activate(this, h) : field.Value;

    /// <summary>by 属性的复合赋值:左值要过 getter、结果要过 setter,两个子求值各占一个阶段
    /// (都走 CallInto,内置和类运算符一视同仁,阶段数就固定了)。
    /// 2=推 getter 3=读回左值再算 4=把算好的值推给 setter 5=返回算好的值。
    ///
    /// `prop` 是这次要用的那份 property(接口槽已经绑好接收者),`field` 是槽本身 ——
    /// 类型约束和 readonly 在它身上(`CheckAssignable` / `CheckWritable`)。</summary>
    private void StepByCompoundAssign(NodeFrame nf, string op, Variable field, RuntimeValue prop, string member)
    {
        if (nf.Count == 2)
        {
            CallInto(nf, PropertyGetter(prop, member), VoidVal.Instance);
            return;
        }

        if (nf.Count == 3)
        {
            var (bound, _) = BindOperator(nf.Result(2), op);
            CallInto(nf, bound, nf.Result(1));
            return;
        }

        if (nf.Count == 4)
        {
            // 算好的新值也要过 `by a: int` 那道约束、以及 readonly
            // (和普通赋值一条路,见 WriteVariable)
            field.CheckWritable();
            field.CheckAssignable(nf.Result(3), ViaTrait(nf.Result(3)));
            CallInto(nf, PropertySetter(prop, member), nf.Result(3));
            return;
        }

        // count 5:setter 也跑完了 → 复合赋值表达式的值是新值
        Return(nf, nf.Result(3));
    }

    /// <summary>成员写入前的门禁:core 需要 unsafe,private/protected 看访问控制。</summary>
    private void CheckMemberAccess(Variable field, ObjectVal obj, string member)
    {
        if (field.HasAttr(Attr.Core) && !IsUnsafe)
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
            if (obj is FunctionVal fn && ma.Member == ObjectVal.NameMember)
            {
                PushChild(nf, bin.Right);
                return;
            }

            // 判据是"有没有**自己**的成员表",不是"是不是 ObjectVal":
            // 原子值借的是类那层的表,往里写等于改掉整个类型,所以按只读挡回去。
            if (!obj.HasOwnMembers)
                throw new RuntimeException($"无法给 {obj.Type} 的值设置字段（值类型没有自己的成员）");
            var ov = (ObjectVal)obj;
            // `:=` 是定义:字段不存在也放行(到 count==2 时新建)。已存在的字段照样受 core/访问控制约束,
            // 否则 `:=` 就成了绕过封装的万能钥匙。
            // 本层没有就问接口实现:作用域里有生效的实现时,`u.a = 1` 落在实现那条槽上。
            // 这一阶段只要那格变量(存在性 + 门禁),**不建激活格** —— 真写的时候(count==2)才绑接收者。
            var ownF = ov.Scope.LookupField(ma.Member);
            var field = ownF ?? (ownF == null ? BuiltinClasses.TraitSlot(this, ov, ma.Member)?.Slot : null);
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
            ov2.Scope.DefineOrReplace(ma.Member, rv.Type, rv);
            Return(nf, rv);
            return;
        }

        // count==1 已查过字段存在,这里再兜一次:字段在右侧求值期间被删掉时不至于 NRE
        // (接口实现那条也再兜一次:两次都只算"当前作用域里有没有生效的实现",结果一致;
        //  接收者就是 nf.Result(0) 那个,所以两次各建一份激活格是等价的)
        var own2 = ov2.Scope.LookupField(ma.Member);
        var hit2 = own2 == null ? BuiltinClasses.TraitSlot(this, ov2, ma.Member) : null;
        var field2 = own2 ?? hit2?.Slot ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'");
        WriteVariable(nf, field2, rv, PropOf(field2, hit2));
    }
}
