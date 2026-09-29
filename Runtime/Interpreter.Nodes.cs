namespace Ravel.Runtime;

/// <summary>节点状态机:每个 AST 节点一个 NodeFrame,按 Results.Count 分阶段推进(求子节点 → 汇总)。
/// 需要「调用函数拿结果」的节点把 sink 传 nf.Parent(结果直接流进父帧),不再回到本帧。</summary>
public partial class Interpreter
{
    /// <summary>隐式转换:失败返回 null,把失败原因从 out 带走。
    /// 原因要带——`x: int = bigint 9999999999999` 光说「无法将 BigInt 赋值给 Integer」,
    /// 看不出是「类型根本不支持转换」还是「转得动但这个值超出 int 范围」,后者才是真话。</summary>
    private static RuntimeValue? TryConvert(RuntimeValue val, ObjectVal target, out string? why)
    {
        try
        {
            why = null;
            return BuiltinClasses.ConvertDirect(target, val);
        }
        catch (RuntimeException ex)
        {
            why = ex.Message;
            return null;
        }
    }

    /// <summary>数字文字 → 值。整数按字面量文本定类型:装得下 int 就是 int,装不下退化成 bigint。
    /// 以前一律 (int) 硬转,2147483648 静默绕成 -2147483648,而且 double 中转还会丢精度。</summary>
    private static RuntimeValue MakeNumber(NumberLiteral nn)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (nn.IsFloat) return new FloatVal(double.Parse(nn.Lexeme, ci));
        return int.TryParse(nn.Lexeme, System.Globalization.NumberStyles.None, ci, out var i)
            ? new IntVal(i)
            : new BigIntVal(System.Numerics.BigInteger.Parse(nn.Lexeme, ci));
    }

    private void StepNode(NodeFrame nf)
    {
        switch (nf.Node)
        {
            case NumberLiteral nn: if (nf.Count == 0) Return(nf, MakeNumber(nn)); break;
            case StringLiteral ss: if (nf.Count == 0) Return(nf, new StringVal(ss.Value)); break;
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
            case SetLiteral sl: StepSet(nf, sl.Elements); break;
            case DictLiteral dl: StepDict(nf, dl.Entries); break;
            case BlockExpr b: if (nf.Count == 0) Return(nf, new BlockVal(b, nf.Scope)); break;
            case LambdaExpr lam: StepLambda(nf, lam); break;   // 分相推进(注解要先求值),别加 Count==0 的守卫
            case VarDefinition v: StepVarDef(nf, v); break;
            case Assignment a: StepAssign(nf, a.Name, a.Value); break;
            case ExpressionStatement es: if (nf.Count == 0) PushChild(nf, es.Expr); else Return(nf, nf.Result(0)); break;
            // 走到这里说明 AST 里有个节点类型没接上状态机——报出节点类型才查得下去
            default: throw new RuntimeException($"无法求值的节点类型: {nf.Node.GetType().Name}");
        }
    }

    private void StepIdent(NodeFrame nf, IdentifierExpr id)
    {
        if (nf.Count > 0) return;
        var v = nf.Scope.LookupVar(id.Name);
        if (v == null) throw new RuntimeException($"未定义的变量 '{id.Name}'");
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

        if (nf.Result(1) is not FunctionVal fn) throw new RuntimeException("<| 左边必须是函数");
        CallInto(nf.Parent!, fn, nf.Result(0));
    }

    /// <summary>一元负号。每种数值类型自己翻,不走 `0 - x`——Int 的 '-' 只特判了 Float,
    /// `0 - bigint` 会撞上「运算符 '-' 不支持 BigInt 操作数」。</summary>
    private static RuntimeValue Negate(RuntimeValue v) => v switch
    {
        // -int.MinValue 翻不过来(2147483648 装不下),别静默回绕成它自己
        IntVal i => BuiltinClasses.Narrow(-(long)i.Value, $"-({i.Value})"),
        FloatVal f => new FloatVal(-f.Value),
        BigIntVal b => new BigIntVal(-b.Value),
        FractionVal fr => new FractionVal(-fr.Num, fr.Den),
        BigFractionVal bf => new BigFractionVal(-bf.Num, bf.Den),
        _ => throw new RuntimeException($"一元 '-' 不支持 {v.Type}"),
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
            if (o is not BoolVal bn) throw new RuntimeException("! 需要 bool 操作数");
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

    /// <summary>lambda 的参数注解是个**表达式**,在建这个 lambda 的时候求值一次。
    /// 常见写法就是一个类型名(`int`),也可以是算出来的(`(pickType ())`)。</summary>
    private void StepLambda(NodeFrame nf, LambdaExpr lam)
    {
        if (nf.Count == 0)
        {
            PushChild(nf, lam.Param.Type);
            return;
        }

        var pt = AsClass(nf.Result(0), lam.Param.Name);
        Return(nf, new LambdaVal(lam.Param.Name, lam.Param.Type, pt, lam.Body) { CaptureScope = nf.Scope });
    }

    /// <summary>注解求出来的值得是个类对象。
    ///
    /// 注解是表达式,所以"名字打错"会在求值那一步就报(未定义的变量),到这里只剩
    /// "算出来的东西不是类型"这一种失败。</summary>
    private static ObjectVal AsClass(RuntimeValue v, string what)
        => v as ObjectVal is { IsClass: true } cls
            ? cls
            : throw new RuntimeException($"'{what}' 的类型注解要是个类型，得到 {v.Type}");

    private void StepVarDef(NodeFrame nf, VarDefinition v)
    {
        // 有注解就先求注解(它是个表达式,可能算出一个类),再求值 —— 阶段由 Count 推进
        var hasType = v.TypeAnnotation != null;
        if (hasType && nf.Count == 0)
        {
            PushChild(nf, v.TypeAnnotation!);
            return;
        }

        var valueAt = hasType ? 1 : 0;
        if (nf.Count == valueAt)
        {
            PushChild(nf, v.Value);
            return;
        }

        // `by` 声明的注解说的是**写进来的值**的类型,不是那个 PropertyVal 自己的类型 ——
        // 所以两边都不按普通字段那套办(见下面两处注释)。
        var isBy = v.Attrs?.Contains(Attr.By) ?? false;

        var val = nf.Result(valueAt);
        // `by a: T = default`:那个 `default` 是**属性的默认值**(一对什么都不做的
        // getter/setter),不是 int 的 0 —— 注解管的是写进来的值,和这一份无关。
        if (isBy) val = SlotValue(val);
        // 普通字段:注解管的是这一份值,当场比对/隐式转换。
        // `by`:值是个 PropertyVal,注解管的是**属性那个值** ——
        // 拿它去比的话,`by n: int = property …` 一实例化就报「无法将 Property 赋值给 Integer」,
        // 而写的人根本没写过 Property。没写注解就记 Any:写什么都行(不然后面写入会被
        // `Property` 这个约束全挡回去)。
        var dt = hasType ? AsClass(nf.Result(0), v.Name) : isBy ? BuiltinClasses.Any : val.Type;
        // 没写注解时 dt 就是值自己的类型,一定"可赋值" —— 不必再判一次注解在不在
        // (Accepts 里那半"接口也算数"正是 `x : myTrait = u` 需要的)
        if (!isBy && !Accepts(val, dt))
        {
            var cv = TryConvert(val, dt, out var why);
            if (cv != null) val = cv;
            // 用 `—` 而不是括号:why 自己常带括号(「超出 int 范围(大数用 bigint)」),
            // 套起来会变成双层括号
            else throw new RuntimeException($"类型不匹配: 无法将 {val.Type} 赋值给 {dt} — {why}");
        }

        // **`:=` 是定义,不是覆盖** —— 同一个作用域里同名再 `:=` 就报错(`Scope.Define` 本来
        // 就是这个规矩,从前这里用 `DefineOrReplace` 把它绕过去了)。
        //
        // 类体那一支尤其要紧:各层类体**平铺进同一个实例作用域**(见 `StepClassInit`),
        // 所以"子类的字段盖掉父类的字段""子类的 init 把父类的 init 顶掉(而父类 init 里的
        // 初始化静默不跑)"从前都是静默发生 —— 现在一律当场报出来。要覆盖就写 `=`:
        //     init = (…) => { … }        # 换掉继承来的那条(= 是赋值/覆盖)
        // 类的**实例成员**没有别的覆盖写法,重名就是错(换个名字,或者在 `=` 那侧写)。
        var vr = v.Preset
            ? nf.Scope.DefineOrReplace(v.Name, dt, val)   // 预设类体:每层覆盖上一层
            : nf.Scope.Define(v.Name, dt, val, v);
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

    /// <summary>`by a` / `by a.x` —— **取槽里的那份 property 本身**(不过 getter)。
    /// 和 `by a = X`(换槽)对称:写那边不过 setter,这边不过 getter。
    ///
    /// **不能把整条路径当普通表达式求** —— `by c.n` 求 `c.n` 就已经走 getter 了,
    /// 那正是要绕开的东西(而且白调一次 getter,它有副作用的话就出鬼了)。
    /// 所以:`by a` 连值都不求,直接查变量;`by c.n` 只求**接收者** `c`。
    ///
    /// 门禁和成员读一样走一遍(core 要 unsafe、private 要能走到那个对象)——
    /// 不然 `by` 就成了绕过封装读私有字段的后门。
    ///
    /// 0=求接收者(只有成员那种要) 1=查那个槽。</summary>
    private void StepSlot(NodeFrame nf, SlotExpr slot)
    {
        // `by a`:变量槽
        if (slot.Path is IdentifierExpr id)
        {
            var v = nf.Scope.LookupVar(id.Name) ?? throw new RuntimeException($"未定义的变量 '{id.Name}'");
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
            throw new RuntimeException($"`by` 只能取对象身上的属性，得到 {target.Type}");
        // 取槽本身(`by x.a`)也认接口实现那条:作用域里没有生效的实现时才是「没有字段」。
        // 这一支**不绑接收者**(只取 `.Slot`)—— `by` 取出来的就是实现 scope 里那条 property,
        // 之后再 `Get ()` / `Set v` 时已经没有"这次服务谁"可言,`instance` 会**明确报错**。
        var field = obj.Scope.LookupField(ma.Member)
                    ?? BuiltinClasses.TraitSlot(this, obj, ma.Member)?.Slot
                    ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'");
        BoxedValue.GateRead(field, ma.Member, this);
        if (!CheckFieldAccess(field, obj)) throw BoxedValue.AccessDenied(field, ma.Member);
        CheckSlot(field, ma.Member);
        Return(nf, field.Value);
    }

    /// <summary>这个变量是不是 by 槽。不是就别让它从这儿过 —— `by` 只对属性有意义。</summary>
    private static void CheckSlot(Variable v, string name)
    {
        if (!v.HasAttr(Attr.By))
            throw new RuntimeException($"'{name}' 不是 by 属性（`by` 取/换的是槽里的 property）");
    }

    /// <summary>要放进槽里的值:`default` 换成**属性的默认值**(一对什么都不做的 getter/setter,
    /// 见 <see cref="BuiltinClasses.DefaultProperty"/>),其余原样。
    ///
    /// 别的非 property 值不在这儿管 —— `by bad := 5` 那种第一次读/写时会报出来,
    /// 保持"当场报错、别静默"那条。**声明和换槽(四个分支)共用这一处**。</summary>
    private static RuntimeValue SlotValue(RuntimeValue v)
        => v is DefaultVal ? BuiltinClasses.DefaultProperty() : v;

    /// <summary>`by a = X` / `by a.x = X` —— **换掉槽里的那份 property**(不走旧 setter);
    /// `:=` 那种是**在那个对象上把槽建出来**(`by a.x := X`)。和 <see cref="StepSlot"/>(取槽)
    /// 对称:同一个"槽路径",一边读一边写。
    ///
    /// 三段(成员那种):0=求接收者 1=求新值 2=换。变量那种少第一段。
    /// 门禁和成员写一样(`core` 要 unsafe、`private` 要能走到那个对象);
    /// **不带 `:=` 时目标必须已经是 by 属性** —— 不然 `by obj.nope = …` 会悄悄多出个成员,
    /// 那正是"赋值要求字段已存在"这条规矩要挡的(`:=` 才是定义)。</summary>
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
            if (v == null)
            {
                if (!sa.Define) throw new RuntimeException($"未定义的变量 '{id.Name}'");
                val = SlotValue(val);
                v = nf.Scope.DefineOrReplace(id.Name, BuiltinClasses.Any, val);
                v.SetAttr(Attr.By);
                Return(nf, val);
                return;
            }

            if (v.HasAttr(Attr.Core) && !IsUnsafe)
                throw new RuntimeException($"字段 '{id.Name}' 是核心字段，需要 unsafe");
            CheckSlot(v, id.Name);
            v.ReplaceSlot(SlotValue(val));
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
            throw new RuntimeException($"`by` 只能换对象身上的属性，得到 {target.Type}");
        var val2 = nf.Result(1);
        if (sa.Define)
        {
            // `:=`:在那个对象上建槽(成员不存在也行,和 `obj.a := v` 一条规矩:定义不查门禁)
            val2 = SlotValue(val2);
            var made = obj.Scope.DefineOrReplace(ma.Member, BuiltinClasses.Any, val2);
            made.SetAttr(Attr.By);
            Return(nf, val2);
            return;
        }

        // 同 StepSlot:这里只**换槽**,不走 getter/setter,所以不绑接收者
        var field = obj.Scope.LookupField(ma.Member)
                    ?? BuiltinClasses.TraitSlot(this, obj, ma.Member)?.Slot
                    ?? throw new RuntimeException($"对象没有字段 '{ma.Member}'");
        CheckMemberAccess(field, obj, ma.Member);
        CheckSlot(field, ma.Member);
        field.ReplaceSlot(SlotValue(val2));
        Return(nf, val2);
    }

    /// <summary>变量赋值 `x = v`。语句位置(<see cref="Assignment"/> 节点)和表达式位置
    /// (BinaryExpr 的 `=`)都走这里 —— 别再各写一份:从前表达式那份**根本没赋值**
    /// (只把右值交出去),`print (x = 5)` 会打印 5 而 `x` 一点没变。
    ///
    /// 0=求右值 1=写。名字找不到就交给 <see cref="Scope.Assign"/>,由它报「未定义」。
    /// `by x = …` 那种(换槽)走的是另一条:<see cref="StepSlotAssign"/>。</summary>
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
                throw new RuntimeException($"字段 '{name}' 是核心字段，需要 unsafe");
            WriteVariable(nf, field, val);
            return;
        }

        nf.Scope.Assign(name, val, ViaTrait(val));
        Return(nf, val);
    }

    /// <summary>写一个变量/字段:`by` 属性要把值过一遍 setter(推 CallReturn 帧,setter 跑完
    /// 才算完),其余直接落。变量赋值(`x = v`)和成员赋值(`a.x = v`)共用这一条 ——
    /// 从前两处各写了一遍,连"setter 不是函数"的兜底都一模一样。
    ///
    /// 交出去的表达式值**一律是赋进去的那个值**:从前变量赋值给 `()`、成员赋值给新值、
    /// 而 `by` 属性那条又给新值 —— 同一个动作三个答案,现在只剩一个。
    ///
    /// `prop` 是这次要过的那份 property:接口那条槽得用**绑好这一次接收者**的副本
    /// (见 `BuiltinClasses.Activate`),所以由调用方递进来;不传就是槽自己那一份
    /// (裸变量、自己那层声明的 `by a := property …` 都是这种情况)。</summary>
    private void WriteVariable(NodeFrame nf, Variable field, RuntimeValue val, RuntimeValue? prop = null)
    {
        if (field.HasAttr(Attr.By))
        {
            // `by a: int = …` 的注解在这一侧执行 —— 和普通字段一样,约束的是**写进来的值**。
            // (读那一侧不管:Ravel 从不检查某个函数返回什么,getter 也一样。)
            // readonly 也在这侧:赋值走的是 setter,不经过 `Variable.Assign`,
            // 不问的话"只读"在属性上就是句空话。
            field.CheckWritable();
            field.CheckAssignable(val, ViaTrait(val));
            PushCallReturn(nf.Parent!, PropertySetter(prop ?? field.Value, field.Name), val, val);
            return;
        }

        field.Assign(val, ViaTrait(val));
        Return(nf, val);
    }

}