namespace Ravel.Runtime;

public partial class Interpreter
{
    private static RuntimeValue MakeNumber(NumberLiteral nn)
    {
        if (nn.Parsed is { } cached) return cached;      // 纯函数:算过一次就够了(见 NumberLiteral.Parsed)
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var text = nn.Digits;

        RuntimeValue v = nn.Suffix switch
        {
            'f' => new RealVal(double.Parse(text, ci)),
            'n' when !nn.IsFloat => new BigIntVal(System.Numerics.BigInteger.Parse(text, ci)),
            'i' when !nn.IsFloat => int.TryParse(text, System.Globalization.NumberStyles.None, ci, out var i)
                ? IntVal.Of(i)
                : throw new RuntimeException($"'{text}i' 超出 int 范围（int 是 32 位，要这么大就写 {text}n）", ErrorKind.Value),
            '\0' => nn.IsFloat
                ? new RealVal(double.Parse(text, ci))
                : int.TryParse(text, System.Globalization.NumberStyles.None, ci, out var d)
                    ? IntVal.Of(d)
                    : new BigIntVal(System.Numerics.BigInteger.Parse(text, ci)),
            _ => throw new RuntimeException($"后缀 '{nn.Suffix}' 只能贴在整数上：{text} 是小数，而小数天生就是 real", ErrorKind.Value),
        };

        return nn.Parsed = v;
    }

    private void StepNode(NodeFrame nf)
    {
        switch (nf.Node)
        {
            case NumberLiteral nn: if (nf.Count == 0) Return(nf, MakeNumber(nn)); break;
            case StringLiteral ss: if (nf.Count == 0) Return(nf, ss.Packed ??= new StringVal(ss.Value)); break;
            case CharLiteral cc: if (nf.Count == 0) Return(nf, new CharVal(cc.Value)); break;
            case VoidLiteral: if (nf.Count == 0) Return(nf, VoidVal.Instance); break;
            case SlotExpr slot: StepSlot(nf, slot); break;
            case SlotAssign sa: StepSlotAssign(nf, sa); break;
            case LiteralExpr le: if (nf.Count == 0) Return(nf, le.Value); break;
            case IdentifierExpr id: StepIdent(nf, id); break;
            case BinaryExpr bin: StepBinary(nf, bin); break;
            case UnaryExpr un: StepUnary(nf, un); break;
            case CallExpr call: StepCall(nf, call); break;
            case MemberAccess ma: StepMemberAccess(nf, ma); break;
            case PipeExpr pipe: StepPipe(nf, pipe); break;
            case ListLiteral l: StepList(nf, l.Elements); break;
            case RangeExpr r: StepRange(nf, r); break;
            case SetLiteral sl: StepSet(nf, sl.Elements); break;
            case DictLiteral dl: StepDict(nf, dl.Entries); break;
            case BlockExpr b: if (nf.Count == 0) Return(nf, new BlockVal(b, nf.Scope)); break;
            case LambdaExpr lam: StepLambda(nf, lam); break;   // 分相推进(注解要先求值),别加 Count==0 的守卫
            case VarDefinition v: StepVarDef(nf, v); break;
            case Assignment a: StepAssign(nf, a.Name, a.Value); break;
            case ExpressionStatement es: if (nf.Count == 0) PushChild(nf, es.Expr); else Return(nf, nf.Result(0)); break;
            // 开关语句在解析那一趟就生效了,跑到这儿什么都不用做
            case DirectiveStatement: if (nf.Count == 0) Return(nf, VoidVal.Instance); break;
            // 走到这里说明 AST 里有个节点类型没接上状态机——报出节点类型才查得下去
            default: throw new RuntimeException($"无法求值的节点类型: {nf.Node.GetType().Name}");
        }
    }

    private void StepIdent(NodeFrame nf, IdentifierExpr id)
    {
        if (nf.Count > 0) return;
        var v = nf.Scope.LookupVar(id.Name);
        if (v == null) throw new RuntimeException($"未定义的变量 '{id.Name}'", ErrorKind.Name);
        BoxedValue.GateRead(v, id.Name, this);   // 和成员访问共用一套门禁,别各抄一份
        if (v.HasAttr(Attr.By))
        {
            CallInto(nf.Parent!, PropertyGetter(v.Value, id.Name), VoidVal.Instance);
            return;
        }

        Return(nf, v.Value);
    }

    private void StepCall(NodeFrame nf, CallExpr call)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, call.Function);
            return;
        }

        if (nf.Count == 1)
        {
            // 块参数就地包上**调用点**的作用域,不必再走一个子帧
            // (块要晚到被调用时才跑,所以作用域得在这里定下来)
            if (call.Argument is BlockExpr b) CallInto(nf.Parent!, nf.Result(0), new BlockVal(b, nf.Scope));
            else PushChild(nf, call.Argument);
            return;
        }

        // count 2:callee + 参数都好了
        CallInto(nf.Parent!, nf.Result(0), nf.Result(1));
    }

    private void StepMemberAccess(NodeFrame nf, MemberAccess ma)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, ma.Object);
            return;
        }

        var obj = nf.Result(0);
        var byGetter = BoxedValue.TryGetByGetter(this, obj, ma.Member);
        if (byGetter != null)
        {
            CallInto(nf.Parent!, byGetter, VoidVal.Instance);
            return;
        }

        Return(nf, new BoxedValue(obj, this).GetMember(ma.Member).Value);
    }

    private void StepPipe(NodeFrame nf, PipeExpr pipe)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, pipe.Right);
            return;
        }

        if (nf.Count == 1)
        {
            PushChild(nf, pipe.Left);
            return;
        }

        if (nf.Result(1) is not FunctionVal fn) throw new RuntimeException("<| 左边必须是函数", ErrorKind.Type);
        CallInto(nf.Parent!, fn, nf.Result(0));
    }

    private static RuntimeValue Negate(RuntimeValue v) => v switch
    {
        // -int.MinValue 翻不过来(2147483648 装不下),别静默回绕成它自己
        IntVal i => BuiltinClasses.Narrow(-(long)i.Value, $"-({i.Value})"),
        RealVal f => new RealVal(-f.Value),
        BigIntVal b => new BigIntVal(-b.Value),
        FractionVal fr => new FractionVal(-fr.Num, fr.Den),
        BigFractionVal bf => new BigFractionVal(-bf.Num, bf.Den),
        _ => throw new RuntimeException($"一元 '-' 不支持 {v.Type}", ErrorKind.Type),
    };

    private void StepUnary(NodeFrame nf, UnaryExpr un)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, un.Operand);
            return;
        }

        var o = nf.Result(0);
        if (un.Op == "!")
        {
            if (o is not BoolVal bn) throw new RuntimeException("! 需要 bool 操作数", ErrorKind.Type);
            Return(nf, new BoolVal(!bn.Value));
        }
        else if (un.Op == "-")
        {
            Return(nf, Negate(o));
        }
        else
        {
            throw new RuntimeException("未知的一元运算符: " + un.Op);
        }
    }

    private void StepList(NodeFrame nf, List<Expression> es)
    {
        if (nf.Count < es.Count)
        {
            PushChild(nf, es[nf.Count]);
            return;
        }

        var elems = new List<RuntimeValue>();
        for (int i = 0; i < nf.Count; i++) elems.Add(nf.Result(i));
        Return(nf, new ListVal(elems));
    }

    private void StepSet(NodeFrame nf, List<Expression> es)
    {
        if (nf.Count < es.Count)
        {
            PushChild(nf, es[nf.Count]);
            return;
        }

        var set = new HashSet<RuntimeValue>();
        for (int i = 0; i < nf.Count; i++) set.Add(nf.Result(i));
        Return(nf, new SetVal(set));
    }

    private void StepDict(NodeFrame nf, List<DictEntry> entries)
    {
        // 每条目**两个**子节点:先键后值(键也是表达式了 —— `{"a": 1}` 里的 `"a"`)
        if (nf.Count < entries.Count * 2)
        {
            var e = entries[nf.Count / 2];
            PushChild(nf, nf.Count % 2 == 0 ? e.Key : e.Value);
            return;
        }

        var dict = new Dictionary<RuntimeValue, RuntimeValue>();
        for (int i = 0; i < entries.Count; i++)
            dict[BuiltinClasses.KeyArg(nf.Result(2 * i), "字典的键")] = nf.Result(2 * i + 1);
        Return(nf, new DictVal(dict));
    }

    private void StepLambda(NodeFrame nf, LambdaExpr lam)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, lam.Param.Type);
            return;
        }

        var pt = AsClass(nf.Result(0), lam.Param.Name);
        Return(nf, new LambdaVal(lam.Param.Name, lam.Param.Type, pt, lam.Body)
        {
            CaptureScope = nf.Scope,
            ParamPattern = lam.Param.Pattern,      // 只为打印:签名印回人写的样子
            ParamAttrs = lam.Param.Attrs,          // 调用时按变量那一套装到绑定上
        });
    }

    private static ObjectVal AsClass(RuntimeValue v, string what)
        => v as ObjectVal is { IsClass: true } cls
            ? cls
            : throw new RuntimeException($"'{what}' 的类型注解要是个类型，得到 {v.Type}", ErrorKind.Type);

    private void StepVarDef(NodeFrame nf, VarDefinition v)
    {
        // 注解不在这儿:`x : T = v` 在 lowering 里就变成"值过一次 `x : T` 才绑名"
        // (见 `Syntax/Lowering.cs` 的 `Typed`/`Checked`),到这里只剩"求值、定义"。
        if (nf.Count == 0)
        {
            PushChild(nf, v.Value);
            return;
        }

        // `by` 声明的注解说的是**写进来的值**的类型,不是那个 PropertyVal 自己的类型 ——
        // 所以两边都不按普通字段那套办(见下面两处注释)。
        var isBy = v.Attrs?.Contains(Attr.By) ?? false;

        var val = nf.Result(0);
        // 注解不在这儿判:`x : T = v` 在 lowering 里已经拆成 `x := v` 加一句 `x : T` 的守卫
        // (见 `Syntax/Lowering.cs` 的 `Typed`),到这一步只剩普通定义。
        // `by a: T = _`:槽里那一份本来就是**属性**,`_` 在这儿的意思是"什么都不做的空属性对"。

        var vr = v.Preset
            ? nf.Scope.DefineOrReplace(v.Name, val)   // 预设类体:每层覆盖上一层
            : nf.Scope.Define(v.Name, val, v);
        if (v.Attrs != null)
            foreach (var a in v.Attrs)
                vr.SetAttr(a);
        // `::=` 同时给函数和**类对象**命名:两者的 Name 都是成员表里的 `name` 成员
        if (v.Named && val is ObjectVal named)
        {
            named.Name = v.Name;
        }

        // 运算符没有别名可言:符号本身就是变量名(`+ := f` 定义的就是 `+`)
        Return(nf, VoidVal.Instance);
    }

    private void StepSlot(NodeFrame nf, SlotExpr slot)
    {
        // `by a`:变量槽
        if (slot.Path is IdentifierExpr id)
        {
            var v = nf.Scope.LookupVar(id.Name) ?? throw new RuntimeException($"未定义的变量 '{id.Name}'", ErrorKind.Name);
            CheckSlot(v, id.Name);
            Return(nf, v.Value);
            return;
        }

        // `by a.x`:成员槽 —— 只求接收者
        var ma = (MemberAccess)slot.Path;
        if (nf.Count == 0)
        {
            PushChild(nf, ma.Object);
            return;
        }

        var target = nf.Result(0);
        if (target is not ObjectVal obj)
            throw new RuntimeException($"`by` 只能取对象身上的属性，得到 {target.Type}", ErrorKind.Type);
        // 取槽本身(`by x.a`)也认接口实现那条:作用域里没有生效的实现时才是「没有字段」。
        // 这一支**不绑接收者**(只取 `.Slot`)—— `by` 取出来的就是实现 scope 里那条 property,
        // 之后再 `Get ()` / `Set v` 时已经没有"这次服务谁"可言,`instance` 会**明确报错**。
        var field = obj.Scope.LookupField(ma.Member)
                    ?? BuiltinClasses.TraitSlot(this, obj, ma.Member)?.Slot
                    ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'", ErrorKind.Attribute);
        BoxedValue.GateRead(field, ma.Member, this);
        if (!CheckFieldAccess(field, obj)) throw BoxedValue.AccessDenied(field, ma.Member);
        CheckSlot(field, ma.Member);
        Return(nf, field.Value);
    }

    private static void CheckSlot(Variable v, string name)
    {
        if (!v.HasAttr(Attr.By))
            throw new RuntimeException($"'{name}' 不是 by 属性（`by` 取/换的是槽里的 property）", ErrorKind.Type);
    }

    private void StepSlotAssign(NodeFrame nf, SlotAssign sa)
    {
        // 变量槽:`by a = X`
        if (sa.Path is IdentifierExpr id)
        {
            if (nf.Count == 0)
            {
                PushChild(nf, sa.Value);
                return;
            }

            var val = nf.Result(0);
            var v = nf.Scope.LookupVar(id.Name);
            // `by X := …` 是**定义**:名字只在外层有(比如全局那个 `Any` / `list` 别名)时,
            // 要在这层建一个新槽把外层遮住 —— 不是去"换"外层那个变量里的 property
            // (`=` 那种没 `:=`,才必须已有槽可换 —— 那正是"赋值要求字段已存在"要挡的)。
            // 本层有的(父类平铺进来的槽、接口体上一趟建的槽)照旧走换槽那条。
            if (sa.Define && v != null && !nf.Scope.Contains(id.Name)) v = null;
            if (v == null)
            {
                if (!sa.Define) throw new RuntimeException($"未定义的变量 '{id.Name}'", ErrorKind.Name);
                v = nf.Scope.DefineOrReplace(id.Name, val);
                v.SetAttr(Attr.By);
                Return(nf, val);
                return;
            }

            if (v.HasAttr(Attr.Core) && !IsUnsafe)
                throw new RuntimeException($"字段 '{id.Name}' 是核心字段，需要 unsafe", ErrorKind.Access);
            CheckSlot(v, id.Name);
            v.ReplaceSlot(val);
            Return(nf, val);
            return;
        }

        // 成员槽:`by a.x = X` —— 先求接收者,再求新值
        var ma = (MemberAccess)sa.Path;
        if (nf.Count == 0)
        {
            PushChild(nf, ma.Object);
            return;
        }

        if (nf.Count == 1)
        {
            PushChild(nf, sa.Value);
            return;
        }

        var target = nf.Result(0);
        if (target is not ObjectVal obj)
            throw new RuntimeException($"`by` 只能换对象身上的属性，得到 {target.Type}", ErrorKind.Type);
        var val2 = nf.Result(1);
        if (sa.Define)
        {
            // `:=`:在那个对象上建槽(成员不存在也行,和 `obj.a := v` 一条规矩:定义不查门禁)
            var made = obj.Scope.DefineOrReplace(ma.Member, val2);
            made.SetAttr(Attr.By);
            Return(nf, val2);
            return;
        }

        // 同 StepSlot:这里只**换槽**,不走 getter/setter,所以不绑接收者
        // **只看实例自己那层**:`LookupField` 现在会兜底到类注册表那格,那是全类共用的,
        // 换掉它等于换掉这个类的槽(别的实例一起变)—— 换槽要落到实例自己身上。
        var field = obj.Scope.OwnField(ma.Member)
                    ?? BuiltinClasses.TraitSlot(this, obj, ma.Member)?.Slot;

        // **引擎挂在类上的那些槽**（`[ClassProperty]` —— 生成出来的属性/数据成员）:
        // 读、写那两条路都够得着它（`ClassBySlot`），换槽这一条从前只看实例自己那层，
        // 于是报的是「对象没有字段 'Length'」（实测）—— 那句话还把方向指错了。
        //
        // 换槽**不能改类那张表**:那是整个类共用的，动一下所有实例跟着变。
        // 所以换到**这个实例自己那层**去（遮住类那格）—— 和 `by a.x := …` 的落点一致。
        // 只读的那些（值类型的数据成员，成员集里造出来就带 `Readonly`）在 `CheckWritable`
        // 那一句挡下 —— 这也是"值类型改不了"该有的那句话。
        if (field == null && BuiltinClasses.ClassBySlot(obj, ma.Member) is { } clsSlot)
        {
            CheckMemberAccess(clsSlot, obj, ma.Member);
            clsSlot.CheckWritable();
            var made = obj.Scope.DefineOrReplace(ma.Member, val2);
            made.SetAttr(Attr.By);
            Return(nf, val2);
            return;
        }

        if (field == null) throw new RuntimeException($"对象没有字段 '{ma.Member}'", ErrorKind.Attribute);
        CheckMemberAccess(field, obj, ma.Member);
        CheckSlot(field, ma.Member);
        field.ReplaceSlot(val2);
        Return(nf, val2);
    }

    private void StepAssign(NodeFrame nf, string name, Expression value)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, value);
            return;
        }

        var val = nf.Result(0);
        var field = nf.Scope.LookupVar(name);
        if (field != null)
        {
            if (field.HasAttr(Attr.Core) && !IsUnsafe)
                throw new RuntimeException($"字段 '{name}' 是核心字段，需要 unsafe", ErrorKind.Access);
            WriteVariable(nf, field, val);
            return;
        }

        nf.Scope.Assign(name, val);
        Return(nf, val);
    }

    private void WriteVariable(NodeFrame nf, Variable field, RuntimeValue val, RuntimeValue? prop = null)
    {
        // **写入的每一条路都过这一道**:`default` 按**声明类型**落地(`x: int` 上写 `default`
        // 给 0)。和定义那边(`VarDefinition`)一个口径 —— 从前这边只查不转,而 `default` 的型
        // 是底类型 `Every`、那一查拦不住它,于是变量里存进一个真的 `default`。
        if (field.HasAttr(Attr.By))
        {
            // `by a: int = …` 的注解在这一侧执行 —— 和普通字段一样,约束的是**写进来的值**。
            // (读那一侧不管:Ravel 从不检查某个函数返回什么,getter 也一样。)
            // readonly 也在这侧:赋值走的是 setter,不经过 `Variable.Assign`,
            // 不问的话"只读"在属性上就是句空话。
            field.CheckWritable();
            PushCallReturn(nf.Parent!, PropertySetter(prop ?? field.Value, field.Name), val, val);
            return;
        }

        field.Assign(val);
        Return(nf, val);
    }

}