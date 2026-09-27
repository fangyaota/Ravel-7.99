namespace Ravel.Runtime;

/// <summary>接口(interface)与实现(use)—— 这一套机制全在这儿。
///
/// 形:
///
///     myTrait ::= interface { by a : int = default; by b : function = default }
///     myImplement := myTrait myClass {
///         by a = property (() => { instance.x; }) ((v: int) => { instance.x = v; })
///         by b = property (() => { () => { print "b override"; } }) ((v: function) => { (); })
///     }
///     use myImplement            # **只在这个作用域里**生效
///     u := myClass ()
///     u.a = 1                    # 走 myImplement 上那条槽
///     u.b ()
///     myImplement.Dispose ()     # 提前取消
///
/// 三条要点:
///
/// - **`interface` 是内置类对象,parent 是 `type`**(`interface is type`)。`interface { … }` 因此造出的
///   是**类对象** `myTrait` —— 它的**类体**就是接口的"形"(那批 `by … = default`)。
/// - **`myTrait myClass { … }` 造出实现对象**:接口的类体与实现块**依次跑在同一个 scope 里**
///   (先摆槽、再换槽),那个 scope 就是实现对象的成员表 —— **槽住在实现里**,建一次一直用。
/// - **读写 `x.a` 常规查找落空时**才问接口:沿**当前作用域**的词法链找生效中的实现,把实现
///   scope 里的 `instance` **换成 x**(那是唯一的可变量),这一次读/写就交给实现上那条槽。
///   于是效果**随作用域在/不在** —— 出了 `use` 那个作用域,`x.a` 又回到「类型 'myClass' 没有方法 'a'」。
///
/// `myClass` 这个名字**从头到尾没被动过**:不建子类、不换绑定、不往它身上加成员。
internal static partial class BuiltinClasses
{
    // `Interface`(接口的权威名,小写别名 `interface` 在 predefined.rav;parent 是 `type`,
    // 所以它也是类,而它造的类(接口)同样进类型树)和别的内置类一起声明在 BuiltinClasses.cs。

    /// <summary>实现 scope 里那个"当前在服务谁"的变量。接口声明的槽都能看见它,
    /// 每次要用的时候换一个实例 —— 槽本身不动。</summary>
    internal const string InstanceMember = "instance";
    /// <summary>实现对象身上的目标类(`myTrait myClass { … }` 的那个 `myClass`)。</summary>
    internal const string TargetMember = "target";
    /// <summary>实现的**代号**:每 `Dispose` 一次 +1,登记表里的每条都记着"登记时它几岁"。
    /// 这样 `Dispose` 只需把自己加一岁(O(1)、幂等、在哪个作用域调都一样)—— 已经登记过的
    /// 那些**统统作废**,而之后在哪 `use` 就在哪重新记一条新的(那个作用域于是又活了)。
    /// 不做成"从每个登记过的作用域里摘掉":那要记一份 (实现 × 作用域) 的账,
    /// 在循环里调一次带 `use` 的函数就会一直长,而 scope 一消失那笔账还没法销。</summary>
    internal const string GenerationMember = "generation";
    /// <summary>登记表挂在**作用域**上。名字里的 `$` 不在 Ravel 标识符字符集里
    /// (见 Lexer 的 ReadIdentifier:只认字母/数字/下划线),所以用户写不出这个名字,永远不会撞
    /// (`use` 这个名字本身已经是全局只读别名了,更不能拿来做键)。</summary>
    internal const string UseRegMember = "use$impls";
    /// <summary>实现对象上的取消方法名。</summary>
    internal const string DisposeMember = "Dispose";

    /// <summary>`interface` 的 init。两分支,**`target: Type` 那支必须排在前面**:
    /// `ClassVal : FunctionVal` 且 `Type <: Function`,反过来 `(body: Function)` 会先把
    /// `myTrait myClass` 里的**类对象**吃掉(然后在 Install 里报「class 需要代码块参数」)。
    ///
    /// 第二支收满两个参数后推 <see cref="ControlKind.ImplMake"/> 控制帧:造实现要把**两段类体**
    /// 跑在实现 scope 里,而原生闭包的体是同步的,推不了帧。</summary>
    /// <summary>接口自己的 `init`(`Alternate(twoArg, oneArg)`)。**每个接口的类体里都会塞一份**
    /// ——见下面 `BakeInterfaceInit` 的说明。它是无状态的(只看 `this` 的类型和参数),
    /// 所以这一个值可以给所有接口共用。</summary>
    private static ControlFunction _interfaceInit = null!;

    private static void InstallInterfaceInit()
    {
        var twoArg = new NativeClosure("target", Type, (scope, target) =>
            new ControlFunction(ControlKind.ImplMake, 3, RList<RuntimeValue>.Empty
                // 接口本体 = `this` 的**类型**:`myTrait myClass …` 是实例化 `myTrait`,
                // 所以 `this` 是它的一个实例,它的 Type 才是接口本身那个类对象。
                .Add(((ObjectVal)scope.Lookup(ObjectVal.ThisMember).Value).Type)
                .Add(target)));
        var oneArg = new NativeClosure("body", Function, (scope, body) =>
        {
            var trait = Install(scope, Object, body);
            BakeInterfaceInit(trait, body);
            // `impl.Dispose ()` 挂在**接口**上,所有实现共用这一份(不必每个实现塞一个闭包)。
            // DefineMethod 存的是 ISelfBinding 的内置方法:读成员时才绑接收者,于是 self 就是
            // **拿到的那个实现** —— `Copy ()` / `with` 出来的副本绑的是它自己,不会误伤原件。
            trait.DefineMethod(DisposeMember, (self, _) => DisposeImplementation((ObjectVal)self));
            return trait;
        });
        _interfaceInit = Alternate(twoArg, oneArg);
        Interface.ClassBody = PresetCtor(_interfaceInit);
    }

    /// <summary>把接口自己的 `init` 塞进这个接口的**类体**(定义在用户写的那段前面)。
    ///
    /// 为什么非这样不可:接口对象的 parent 是 **`object`**(和 C# 一样 —— 接口不是"继承了一个叫
    /// Interface 的基类"),于是它继承不到 `Interface` 那个预设类体里的 `init`。而
    /// `myTrait myClass { … }` 是**实例化 myTrait**,得在它自己的类体里找得到这个 `init`。
    /// 塞进去之后:实例化时照跑(造实现),而 `StepImplMake` 拿它当"接口的形"跑那一步时,
    /// 也只是在实现 scope 里多出一个没人看的 `init` 成员(不是 `by` 槽,所以不会被接口槽的
    /// 查找接管)。
    ///
    /// 类体必须是新的 BlockExpr(带用户那份的 Source/行列):原样改 `block` 会动到用户写的
    /// 那个 BlockExpr,`Source` 也就丢了。</summary>
    private static void BakeInterfaceInit(ObjectVal trait, RuntimeValue body)
    {
        if (body is not BlockVal blk) return;
        var init = new VarDefinition(ObjectVal.InitMember, null, new LiteralExpr(_interfaceInit))
        {
            Line = blk.Block.Line,
            Column = blk.Block.Column,
        };
        trait.ClassBody = new BlockVal(
            new BlockExpr([init, .. blk.Block.Statements])
            {
                Line = blk.Block.Line,
                Column = blk.Block.Column,
                Source = blk.Block.Source,
            },
            blk.CaptureScope);
    }

    /// <summary>实现对象的最后一道装填:目标类、代号、`this`。
    /// 槽和 `instance` 的位子是前面两段类体跑出来的,这里不碰;`Dispose` 挂在接口那份上。</summary>
    internal static void FinishImplementation(ObjectVal impl, ObjectVal target)
    {
        impl.Scope.Define(TargetMember, Type, target);
        impl.Scope.Define(GenerationMember, Int, new IntVal(0)).SetAttr(Attr.Unreadable);
        impl.Scope.Define(ObjectVal.ThisMember, impl.Type, impl);
    }

    /// <summary>把实现登记进 `into` 那个作用域:`use` 给的是**当前作用域**(随作用域在/不在),
    /// `impl` 给的是**全局作用域**(每个作用域链都到全局,于是处处生效)。
    /// `caller` 只用来拼报错文案(是 `use` 还是 `impl` 写错了)。返回实现本身。</summary>
    internal static RuntimeValue Use(Interpreter interp, RuntimeValue v, Scope into, string caller)
    {
        if (v is not ObjectVal impl || impl.Scope.LookupField(TargetMember)?.Value is not ObjectVal)
            throw new RuntimeException($"{caller} 要的是「接口 类 实现」造出来的实现，得到 {v.Type.DisplayName}");
        RegisterUse(into, impl);
        return impl;
    }

    /// <summary>登记一条:把 `[实现, 登记时代号]` 放进这个作用域的表里。
    /// 同一个实现在同一个作用域只留最新那一条(重 `use` 就是"在这儿重新登记一次",
    /// 于是被 `Dispose` 作废过的实现在这个作用域里又生效了,别的作用域不受影响)。</summary>
    internal static void RegisterUse(Scope scope, ObjectVal impl)
    {
        if (scope.LookupField(UseRegMember)?.Value is not ListVal reg)
        {
            reg = new ListVal([]);
            scope.DefineOrReplace(UseRegMember, List, reg).SetAttr(Attr.Unreadable);
        }

        reg.Elements.RemoveAll(x => x is ListVal e && e.Elements.Count > 0 && ReferenceEquals(e.Elements[0], impl));
        reg.Elements.Add(new ListVal([impl, new IntVal(Generation(impl))]));
    }

    /// <summary>`Dispose ()`:把实现加一岁,已经登记过的那些条目统统作废。O(1)、幂等、
    /// 在哪个作用域调都一样 —— 取消的是这个实现。</summary>
    private static RuntimeValue DisposeImplementation(ObjectVal impl)
    {
        if (impl.Scope.LookupField(GenerationMember) is { } gen)
            gen.Assign(new IntVal(((IntVal)gen.Value).Value + 1));
        return VoidVal.Instance;
    }

    private static int Generation(ObjectVal impl)
        => impl.Scope.LookupField(GenerationMember)?.Value is IntVal g ? g.Value : 0;

    /// <summary>这条登记还作数吗:实现还在、代号没被 `Dispose` 顶掉。</summary>
    private static bool IsLiveEntry(RuntimeValue entry, out ObjectVal impl)
    {
        impl = null!;
        if (entry is not ListVal e || e.Elements.Count < 2) return false;
        if (e.Elements[0] is not ObjectVal o) return false;
        impl = o;
        return e.Elements[1] is IntVal g && g.Value == Generation(o);
    }

    /// <summary>接口槽的兜底查找。**常规查找先说话**:自己那层 + 类链(还有类型那层的运算符)里
    /// 有的名字一律不接管 —— 接口只补"本来要报没有方法"的那些。
    ///
    /// 然后沿**当前作用域**的词法链由内到外找生效中的实现:目标类收得下 receiver、身上又有这个名字的
    /// 那一个。同一个 scope 里后 `use` 的先试(和"后来的覆盖先前的"一个规矩)。
    ///
    /// 找到就把实现 scope 里的 `instance` 换成 receiver(**槽住实现里,只有 instance 每次换**),
    /// 再把那条槽交出去 —— 读走 getter、写走 setter,和普通 `by` 成员完全同一条路。
    /// 找不到返回 null,由调用点报原来的「没有方法」。</summary>
    internal static Variable? TraitSlot(Interpreter interp, ObjectVal receiver, string name)
    {
        if (receiver.MemberScope.LookupField(name) != null) return null;
        if (OperatorSymbols.IsSymbol(name) && receiver.Type.MemberScope.LookupField(name) != null) return null;

        for (var s = interp.CurrentScope; s != null; s = s.Parent)
        {
            if (s.LookupField(UseRegMember)?.Value is not ListVal reg) continue;

            for (var i = reg.Elements.Count - 1; i >= 0; i--)
            {
                if (!IsLiveEntry(reg.Elements[i], out var impl)) continue;
                if (impl.Scope.LookupField(TargetMember)?.Value is not ObjectVal target) continue;
                if (!receiver.Type.IsAssignableTo(target)) continue;

                var slot = impl.Scope.LookupField(name);
                if (slot == null || !slot.HasAttr(Attr.By)) continue;   // 这个实现没这个名字 → 试下一个
                BindInstance(impl, target, receiver);
                return slot;
            }
        }

        return null;
    }

    /// <summary>把实现的 `instance` 换成这一次要服务的实例。槽里的 getter/setter 读的就是它,
    /// 所以"换 instance"这一下就是接口的全部动态性。</summary>
    private static void BindInstance(ObjectVal impl, ObjectVal target, ObjectVal instance)
    {
        var vr = impl.Scope.LookupField(InstanceMember);
        if (vr == null) impl.Scope.Define(InstanceMember, target, instance);
        else vr.Assign(instance);
    }

    /// <summary>`u is myTrait` / `myClass <: myTrait` 的兜底:当前作用域里有没有一个生效中的实现,
    /// 目标类收得下 `cls`、而且它就是为那个接口的。传的是**类型**(`u.Type` / 左边那个类型)——
    /// 判据是"这个类的实例在这个作用域里都算那个接口"。作用域外为假 —— 和"出去后释放"一致。</summary>
    /// <summary>`T.GetImplements ()`:这个类型**现在**实现了哪些接口 —— 沿当前作用域找生效中的
    /// 实现,目标类收得下 `cls` 就把那个接口记一条(判据和 `x is I` 同一个)。
    ///
    /// 同一个接口有好几个实现只列一次;顺序和查找一样(由内到外、后 `use` 的先),
    /// 所以列在最前的是**当下生效**的那个。作用域外、或 `Dispose` 之后就列不出来了 ——
    /// 接口是"在这个作用域里生效"的东西,不是烙在类型上的标记。
    ///
    /// 去重走线性扫:接口个数本来就是个位数,而 `ObjectVal` 的相等是身份、哈希却还是 record
    /// 那套(会顺着成员表往下递归),不值当为它开 HashSet。
    ///
    /// 入口在 `CallInto` 的 `BoundImplementsQuery` 一格 —— 这活儿要当前作用域,只有求值器有。</summary>
    internal static RuntimeValue Implements(Interpreter interp, ObjectVal cls)
    {
        var found = new List<RuntimeValue>();
        for (var s = interp.CurrentScope; s != null; s = s.Parent)
        {
            if (s.LookupField(UseRegMember)?.Value is not ListVal reg) continue;

            for (var i = reg.Elements.Count - 1; i >= 0; i--)
            {
                if (!IsLiveEntry(reg.Elements[i], out var impl)) continue;
                if (impl.Scope.LookupField(TargetMember)?.Value is not ObjectVal target) continue;
                if (!cls.IsAssignableTo(target)) continue;
                if (found.Any(x => ReferenceEquals(x, impl.Type))) continue;
                found.Add(impl.Type);
            }
        }

        return new ListVal(found);
    }

    internal static bool HasTrait(Interpreter interp, ObjectVal cls, ObjectVal trait)
    {
        for (var s = interp.CurrentScope; s != null; s = s.Parent)
        {
            if (s.LookupField(UseRegMember)?.Value is not ListVal reg) continue;

            foreach (var e in reg.Elements)
                if (IsLiveEntry(e, out var impl) && impl.Type == trait
                    && impl.Scope.LookupField(TargetMember)?.Value is ObjectVal target
                    && cls.IsAssignableTo(target))
                    return true;
        }

        return false;
    }
}
