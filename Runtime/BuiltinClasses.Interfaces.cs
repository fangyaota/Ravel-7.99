namespace Ravel.Runtime;

internal static partial class BuiltinClasses
{
    // `Interface`(接口的权威名,小写别名 `interface` 在 predefined.rav;parent 是 `type`,
    // 所以它也是类,而它造的类(接口)同样进类型树)和别的内置类一起声明在 BuiltinClasses.cs。

    internal const string InstanceMember = "instance";
    internal const string InstanceActiveMember = "instance$active";
    internal const string ImplActiveMember = "impl$active";
    internal const string ImplIdMember = "impl$id";
    internal const string TargetMember = "target";
    internal const string GenerationMember = "generation";
    internal const string UseRegMember = "use$impls";
    internal const string DisposeMember = "Dispose";

    private static void InstallInterfaceInit()
    {
        // ── ① 工厂的那份:挂在 `Interface` 的类体上,`interface …` 那下就是在实例化它 ──
        // 造一个新接口,三种写法:
        //     interface { … }          —— 不要求
        //     interface 某接口 { … }    —— **一个要求**
        //     interface [A B] { … }    —— 一串要求
        //
        // **接口之间没有"父"这一说了** —— 那一段语法连根去掉,一律要求(见 BuildInterface)。
        //
        // 这里**不需要**判"谁在造":走得进这个类体的只有 `interface …` 一种写法。
        // 顺序是载荷:**要求表在最前**,不然那张 List 会掉进"一个要求"那支。
        // 从前靠 `Alternate` 逐支试,现在看一眼参数直接选。
        // 顺序是载荷:**要求表在最前**,类(`Function` 的子类)排在代码块前面。
        BuiltinCtor(Interface, new NativeClosure("_", Object, (scope, param) => param switch
        {
            ListVal rs => FunctionVal.From(body => BuildInterface(scope, Requirements(rs), body)),

            ObjectVal { IsClass: true } of when of.Type.IsAssignableTo(Interface)
                => FunctionVal.From(body => BuildInterface(scope, [of], body)),

            // 是类但不是接口:说清该写成"某个接口 那个类 { … }",别掉进下面那条通用兜底
            ObjectVal { IsClass: true } of => throw new RuntimeException(
                $"`interface` 后面只能跟接口（一个要求：`interface 某接口 {{ … }}`）"
                + $"或一张要求表（`interface [A B] {{ … }}`）—— {of} 是个类；"
                + "要给某个类实现接口就写成 `某个接口 那个类 { … }`", ErrorKind.Type),

            FunctionVal => BuildInterface(scope, [], param),

            _ => throw new RuntimeException(
                "`interface` 后面要跟一个代码块（`interface { … }`）、一个接口（一个要求，"
                + "`interface 某接口 { … }`）或一张要求表（`interface [A B] { … }`），"
                + $"得到 {param.Type}", ErrorKind.Argument),
        }));

        // ── ② 接口那一支的那份:挂在 `BaseInterface` 的类体上,所有接口(它的子类)继承 ──
        // 干的事完全不同:**造实现**。`某接口 某个类 { … }` 是实例化那个接口,而"谁在被实例化"
        // 就是造出来的实现 —— 推 `ImplMake` 帧,把**实现块**跑在它身上。
        //
        // 接口那几层类体**不在这儿跑**:实例化那趟(`StepClassInit`)已经沿链跑进这个对象的
        // scope 了(普通类一个规矩)。这里只把**实现块**追加进去(先摆槽、再换槽),所以传下去的是
        // **那个对象**本身(不是它的类型):帧里要往它身上装 `target`/`generation`/`instance`。
        //
        // 同样不判"谁在造":走得进这个类体的,永远是某个**接口对象**被应用。
        // **第三个参数(实现体)声明成 `Block`** —— 控制帧从前按个数收、不查类型,这一格是
        // 头一处给它带上声明类型的(见 `ControlFunction.Declared`)。于是:
        //   * `impl (某个接口 某个类 { … })` —— 块本来就是 `Block`(`BlockVal.Type`);
        //   * `impl (某个接口 某个类 default)` —— `default` 的型是 `Every`(底类型),收得下,
        //     意思是"空体"(在 `StepImplMake` 里认);
        //   * 别的(`5`、一个 lambda…)当场拒收,报「参数 '实现体' 需要 Block，得到 Integer」。
        BuiltinCtor(BaseInterface, new NativeClosure("_", Object, (scope, param) => param switch
        {
            ObjectVal { IsClass: true } of => new ControlFunction(ControlKind.ImplMake, 3,
                    RList<RuntimeValue>.Empty.Add(scope.Lookup(ObjectVal.ThisMember).Value).Add(of))
                { Declared = [null, null, ("实现体", Block)] },

            _ => throw new RuntimeException(
                "造实现要写成 `某个接口 那个类 { … }`（接口后面跟一个**类对象**），"
                + $"得到 {param.Type}", ErrorKind.Argument),
        }));

        // `IValue` 是内置接口、**没走 `Install`**,所以类体不会从 `BaseInterface` 那儿
        // 继承过来(继承发生在 `Install` 那一趟)—— 直接借它那份。
        // 没有它,`impl (IValue int { … })` 会报「类型 IValue 不能作为构造器调用」。
        IValue.ClassBody = BaseInterface.ClassBody;

        // `Dispose` 不再在这儿标一条:`IValue` 是**内置接口**、不在 `BuildInterface` 那条路上走,
        // 而如今所有接口共用**一份**成员(`BaseInterface` 的成员工厂,
        // 见 `Runtime/Members/InterfaceMembers.cs`),`Install` 会把那份按引用传下来。
        // (`IValue` 那份要单独指过去 —— `Link` 只接类树,不接 `Members`。)
    }

    private static List<ObjectVal> Requirements(ListVal list)
    {
        var reqs = new List<ObjectVal>();
        for (var i = 0; i < list.Elements.Count; i++)
        {
            if (list.Elements[i] is not ObjectVal { Type: var meta } o || !meta.IsAssignableTo(Interface))
                throw new RuntimeException($"要求表里得全是接口，第 {i + 1} 个是 {list.Elements[i].Type}", ErrorKind.Type);
            reqs.Add(o);
        }

        return reqs;
    }

    private static RuntimeValue BuildInterface(Scope scope, List<ObjectVal> requires, RuntimeValue body)
    {
        if (body is not BlockVal blk)
            throw new RuntimeException($"接口的体得是个代码块（`interface … {{ … }}`），得到 {body.Type}", ErrorKind.Argument);

        var trait = Install(scope, BaseInterface, blk);

        if (requires.Count > 0)
        {
            trait.Requires = requires;
            trait.Scope.DefineOrReplace(ObjectVal.RequiresMember, new ListVal([.. requires]))
                .SetAttr(Attr.Unreadable);
        }

        // `impl.Dispose ()` 挂在**接口**上:所有实现共用一份,不必每个实现塞一个闭包。
        // 那一份现在在 `BaseInterface` 的成员工厂里(`Runtime/Members/InterfaceMembers.cs`),
        // 接口由 `Install` 造成、`Members` 按引用从父接下 —— 所以这儿不必再挂。
        //
        // 它仍是 ISelfBinding 的内置方法:读成员时才绑接收者,于是 self 就是
        // **拿到的那个实现** —— `Copy ()` / `with` 出来的副本绑的是它自己,不会误伤原件。
        return trait;
    }

    internal static void FinishImplementation(Interpreter interp, ObjectVal impl, ObjectVal target)
    {
        // `Unreadable`:引擎内部那几格之一 —— **别转发**(见 `TraitSlot` 里那两道闸)
        impl.Scope.Define(TargetMember, target).SetAttr(Attr.Unreadable);
        impl.Scope.Define(GenerationMember, IntVal.Of(0)).SetAttr(Attr.Unreadable);
        // `this` 用 OrReplace:这个对象是**实例化那趟**造的(`StepClassInit` 已经绑过 `this`),
        // 实现帧只是在它身上继续装填。
        impl.Scope.DefineOrReplace(ObjectVal.ThisMember, impl);
        InstallInstance(interp, impl);
    }

    private static void InstallInstance(Interpreter interp, ObjectVal impl)
    {
        if (impl.Scope.LookupField(InstanceMember) != null)
            throw new RuntimeException($"接口 {impl.Type.DisplayName} 里声明了名为 '{InstanceMember}' 的槽"
                + " —— 那是引擎自己的名字（它指的是「这一次在服务谁」），换一个", ErrorKind.Argument);

        var id = ImplId(impl);
        var getter = new NativeClosure(InstanceMember, Object, (scope, _) =>
            interp.ActiveInstance(scope, id.Value) ?? throw new RuntimeException(
                "`instance` 只在实现体的槽里有效：此刻没有正在被服务的实例"
                + "（它指的是「这一次调用在服务谁」，离开那次调用就没有了）", ErrorKind.Name));
        var setter = new NativeClosure(InstanceMember, Object, (_, _) => throw new RuntimeException(
            "`instance` 是只读的：它由引擎指到这一次服务的实例，不能赋值", ErrorKind.Access));

        var vr = impl.Scope.Define(InstanceMember, new PropertyVal(getter, setter));
        vr.SetAttr(Attr.By);
        vr.SetAttr(Attr.Readonly);
        // protected:它是**实现这一族**的东西 —— `im.instance` / `by im.instance` 从外面碰就报
        // 「变量 'instance' 是受保护的」,而实现自己的代码(槽体、辅助函数)照旧读得到
        // (`CheckFieldAccess` 那条"当前作用域链上有个 this 就是本族"的判据)。
        // **不是**拿它取代 `TraitSlot` 那道名字闸:`u.instance` 的意义是"u 身上没这个成员",
        // 那句话由闸给;访问控制管的是"实现身上有、但外面别碰"(两道各管一半)。
        vr.SetAttr(Attr.Protected);
    }

    private static int _implCount;

    private static IntVal ImplId(ObjectVal impl)
    {
        if (impl.Scope.LookupField(ImplIdMember)?.Value is IntVal had) return had;

        var id = IntVal.Of(++_implCount);
        impl.Scope.Define(ImplIdMember, id).SetAttr(Attr.Unreadable);
        return id;
    }

    internal static RuntimeValue Use(Interpreter interp, RuntimeValue v, Scope into, string caller)
    {
        if (v is not ObjectVal impl || impl.Scope.LookupField(TargetMember)?.Value is not ObjectVal)
            throw new RuntimeException($"{caller} 要的是「接口 类 实现」造出来的实现，得到 {v.Type.DisplayName}", ErrorKind.Type);
        // `use` 是**作用域级**的(登记进 `into` = 调用点当前作用域),`impl` 登记进全局 ——
        // 只有前者会让"当前作用域链"影响答案,于是只有它要关掉缓存
        if (!ReferenceEquals(into, interp.GlobalScope)) interp.SawScopedUse = true;
        RegisterUse(into, impl);
        return impl;
    }

    internal static void RegisterUse(Scope scope, ObjectVal impl)
    {
        if (scope.LookupField(UseRegMember)?.Value is not ListVal reg)
        {
            reg = new ListVal([]);
            scope.DefineOrReplace(UseRegMember, reg).SetAttr(Attr.Unreadable);
        }

        reg.Elements.RemoveAll(x => x is ListVal e && e.Elements.Count > 0 && ReferenceEquals(e.Elements[0], impl));
        reg.Elements.Add(new ListVal([impl, IntVal.Of(Generation(impl))]));

        BumpRegistry();              // 登记变了 → 缓存的答案可能就变了

        // `impl (IValue X …)` —— 顺手把 X 那格**物化索引**标上(见 `ClassVal.IsValueLike`)。
        // **如今这条基本走不到了**:`IValue` 封了(见 `SealBuiltins`),Ravel 侧写不出这句话,
        // 内置那 10 个直接落格、插件走 `PluginApi`。留着是保险 —— 真有谁还登记得进来,
        // 索引也得跟着一起对,不然 `CanBeKey` 和 `HasTrait` 会说两样话。
        // 为什么要索引:那句"能不能当字典的键"要跑在 `dict.At` 那些**同步 C#** 操作里,
        // 那儿拿不到解释器、问不了 `HasTrait`。
        if (ReferenceEquals(impl.Type, IValue)
            && impl.Scope.LookupField(TargetMember)?.Value is ClassVal target)
            target.IsValueLike = true;
    }

    internal static RuntimeValue DisposeImplementation(ObjectVal impl)
    {
        BumpRegistry();              // 作废一条 → 缓存的答案可能就变了

        // `IValue` 那条索引跟着撤掉 —— 作废了就不该再算值类型(那边 `IsValueLike` 有说明)
        if (ReferenceEquals(impl.Type, IValue)
            && impl.Scope.LookupField(TargetMember)?.Value is ClassVal target)
            target.IsValueLike = false;

        if (impl.Scope.LookupField(GenerationMember) is { } gen)
            gen.Assign(IntVal.Of(((IntVal)gen.Value).Value + 1));
        return VoidVal.Instance;
    }

    private static int Generation(ObjectVal impl)
        => impl.Scope.LookupField(GenerationMember)?.Value is IntVal g ? g.Value : 0;

    private static bool IsLiveEntry(RuntimeValue entry, out ObjectVal impl)
    {
        impl = null!;
        if (entry is not ListVal e || e.Elements.Count < 2) return false;
        if (e.Elements[0] is not ObjectVal o) return false;
        impl = o;
        return e.Elements[1] is IntVal g && g.Value == Generation(o);
    }

    private static IEnumerable<ObjectVal> LiveImplementations(Interpreter interp)
    {
        for (var s = interp.CurrentScope; s != null; s = s.Parent)
        {
            if (s.LookupField(UseRegMember)?.Value is not ListVal reg) continue;

            for (var i = reg.Elements.Count - 1; i >= 0; i--)
                if (IsLiveEntry(reg.Elements[i], out var impl))
                    yield return impl;
        }
    }

    internal static TraitHit? TraitSlot(Interpreter interp, RuntimeValue receiver, string name,
                                        bool allowPlain = false)
    {
        // `instance` 是机制自己那条槽,不进"`u.x` 能读到什么"(见 InstanceMember 的说明)。
        // 槽体里读的是**裸名字**,词法链直接命中实现 scope,不走这儿。
        if (name == InstanceMember) return null;
        // 常规查找先说话:就是"这个值能读到哪些成员"(`MemberScope`)——对象是"自己那层 +
        // 类型的成员工厂",**标量借的正是同一个视图**(它们不是 ObjectVal、没有自己那层,
        // `MemberScope` 对它们就是 `Type.Members`)。
        if (receiver.MemberScope.LookupField(name) != null) return null;

        foreach (var impl in LiveImplementations(interp))
        {
            if (impl.Scope.LookupField(TargetMember)?.Value is not ObjectVal target) continue;
            if (!receiver.Type.IsAssignableTo(target)) continue;

            var slot = impl.Scope.LookupField(name);
            if (slot == null) continue;                             // 这个实现没这个名字 → 试下一个

            // **`by` 槽**:得绑接收者(实现身上不存"当前实例",见 `Activate`)
            if (slot.HasAttr(Attr.By)) return new TraitHit(impl, receiver, slot);

            // **普通成员**(接口体里写的函数 / 字段)—— 2026-10-06 起也转发。
            // 但只在 `allowPlain` 那条路上:**成员访问**要它,而**运算符**那条不要 ——
            // 运算符的协议是"槽的值是个 property,getter/setter 两头",普通成员给不出
            // (它是一份现成的值,见 `lib/iterator.rav` 里 `by + := property …` 那个写法)。
            //
            // 两道闸拦**引擎自己装的**:`target` / `generation` / `impl$id` / `*$active`
            // 是"这次服务谁、是哪个实现"那一套,漏出去就是让用户拿内部状态当成员使。
            // 后三个早就标着 `Unreadable`("别读"的记号),`target` 这一笔补上;
            // `IsMethodName` 再排掉 `this` / `block` / `parent` 那几个。
            if (allowPlain && ObjectVal.IsMethodName(name) && !slot.HasAttr(Attr.Unreadable))
                return new TraitHit(impl, receiver, slot);
            continue;
        }

        return null;
    }

    internal static PropertyVal Activate(Interpreter interp, in TraitHit hit)
    {
        // 不是 property(`by v := 5` 那种用错)就在这一句上报出来,措辞和普通 by 成员一条路
        var prop = interp.SlotProperty(hit.Slot.Value, hit.Slot.Name);

        // getter 和 setter 通常写在同一个 `property g s` 里、捕的是同一个作用域 —— 那就共用一层激活格;
        // 不是同一个就各开一层,各自保住自己的名字解析链(只多出激活格那两层)
        var getterChain = CaptureOf(prop.Getter);
        var shared = getterChain != null && ReferenceEquals(getterChain, CaptureOf(prop.Setter))
            ? Activation(getterChain, hit)
            : null;

        return prop with
        {
            Getter = ActivateFn(prop.Getter, shared, hit),
            Setter = ActivateFn(prop.Setter, shared, hit),
            Var = null,                                  // 副本不再挂在哪个变量上(`Attrs ()` 用不到了)
        };
    }

    internal static RuntimeValue BindPlain(in TraitHit hit)
        => hit.Slot.Value is FunctionVal f ? ActivateFn(f, null, hit) : hit.Slot.Value;

    private static FunctionVal ActivateFn(FunctionVal f, Scope? shared, in TraitHit hit)
    {
        var chain = CaptureOf(f);
        if (chain == null) return f;

        var act = shared ?? Activation(chain, hit);
        return f switch
        {
            LambdaVal lam => lam with { CaptureScope = act },
            BlockVal blk => blk with { CaptureScope = act },
            _ => f,
        };
    }

    private static Scope Activation(Scope chain, in TraitHit hit)
    {
        var act = new Scope(chain);
        act.Define(InstanceActiveMember, hit.Receiver);
        act.Define(ImplActiveMember, ImplId(hit.Impl)).SetAttr(Attr.Unreadable);
        return act;
    }

    private static Scope? CaptureOf(FunctionVal f) => f switch
    {
        LambdaVal lam => lam.CaptureScope,
        BlockVal blk => blk.CaptureScope,
        _ => null,
    };

    internal static RuntimeValue Implements(Interpreter interp, ObjectVal cls)
    {
        var found = new List<RuntimeValue>();

        // `IValue` 不走**登记表** —— 它在 `ClassVal.IsValueLike` 那格**索引**里
        // (见 `BuiltinClasses.SealBuiltins`:它封了,内置那 10 条 `impl` 撤了)。
        // 不问它一句的话,`int.GetImplements ()` 会把 IValue 漏掉,和 `HasTrait` 说的对不上。
        if (cls is ClassVal self && self.IsValueLike)
            found.AddRange(InterfaceClosure(IValue));

        foreach (var impl in LiveImplementations(interp))
        {
            if (impl.Scope.LookupField(TargetMember)?.Value is not ObjectVal target) continue;
            if (!cls.IsAssignableTo(target)) continue;

            // 实现了子接口就等于实现了它的父接口(照 C#):把闭包里的接口都列上
            foreach (var face in InterfaceClosure(impl.Type))
                if (!found.Any(x => ReferenceEquals(x, face)))
                    found.Add(face);
        }

        return new ListVal(found);
    }

    private static List<ObjectVal> InterfaceClosure(ObjectVal type)
    {
        var found = new List<ObjectVal>();
        for (var t = type; t != null; t = t.Parent)
        {
            if (t.Type.IsAssignableTo(Interface) && !found.Any(x => ReferenceEquals(x, t)))
                found.Add(t);
            if (t.Parent == t) break;
        }

        return found;
    }

    internal static RuntimeValue Implementors(Interpreter interp, ObjectVal trait)
    {
        var found = new List<RuntimeValue>();

        // `IValue` 这一支同样走**索引**(和上面 `Implements` 那条对称)。判据用
        // `IValue.IsAssignableTo (trait)` 而不是 `trait == IValue`:`IValue` 那个实现的 trait
        // 是 IValue,而它的**闭包**里有 `BaseInterface` —— 照登记表那条路,问
        // `BaseInterface.GetImplementors ()`(和 `object`)本来也问得出它们,别让索引这一支漏。
        if (IValue.IsAssignableTo(trait))
            foreach (var t in AllTypes)
                if (t.IsValueLike && !found.Any(x => ReferenceEquals(x, t)))
                    found.Add(t);

        foreach (var impl in LiveImplementations(interp))
        {
            // 实现子接口的也算这个接口的实现者(照 C#:`IEnumerable` 的实现者里有谁实现了子接口)
            if (!impl.Type.IsAssignableTo(trait)) continue;
            if (impl.Scope.LookupField(TargetMember)?.Value is not ObjectVal target) continue;
            if (found.Any(x => ReferenceEquals(x, target))) continue;
            found.Add(target);
        }

        return new ListVal(found);
    }

    internal static bool HasTrait(Interpreter interp, ObjectVal cls, ObjectVal trait)
    {
        // `IValue` 是**内置接口**,它的"谁实现了"就是 `ClassVal.IsValueLike` 那格**索引** ——
        // 写那一格的有两处:引擎给内置那 10 个值类型(`BuiltinClasses.SealBuiltins`),
        // 和插件的 `[RavelClass(Implements = "IValue")]`(见 `PluginApi`)。
        // **Ravel 侧那条口子没了** —— IValue 封了,用户 `impl` 不进来(那正是"名单引擎说了算")。
        // 走索引有两个好处:O(1),而且 `dict.At` 那条**没有解释器**的路问得了同一个答案(见 `CanBeKey`)。
        if (ReferenceEquals(trait, IValue)) return cls is ClassVal c && c.IsValueLike;

        // **只在链上没有作用域级登记时才敢缓存** —— 那时答案只由全局那批 `impl` 决定,
        // 而 `impl` 是稳定的(改不了,除非再来一条,那会动版本号)。
        // 有过一次 `use` 就整个关掉(见 `Interpreter.SawScopedUse`):`use` 的可见性随作用域走,
        // 要缓存它得先能知道"作用域何时离开",那要动推/弹作用域那条路 —— 不值当。
        //
        // 缓存和那个标记都**挂在解释器上**,不是静态的:一个进程里跑多个解释器是常态
        // (每个用例一个),而 `use` 登记进的是**那个解释器**的作用域 —— 静态的话
        // A 的答案会漏给 B。
        if (interp.SawScopedUse) return HasTraitSlow(interp, cls, trait);

        var v = _registryVersion;
        if (interp.HasTraitMemoVersion != v)
        {
            interp.HasTraitMemo.Clear();
            interp.HasTraitMemoVersion = v;
        }

        if (interp.HasTraitMemo.TryGetValue((cls, trait), out var hit)) return hit;

        var r = HasTraitSlow(interp, cls, trait);
        interp.HasTraitMemo[(cls, trait)] = r;
        return r;
    }

    private static bool HasTraitSlow(Interpreter interp, ObjectVal cls, ObjectVal trait)
    {
        foreach (var impl in LiveImplementations(interp))
            // **精确匹配那个接口**,不走 `IsAssignableTo`。
            //
            // 从前走 `IsAssignableTo` 是对的:接口那时靠**父**继承,子接口不但 `<: 父`,
            // 连父的**槽**也一起沿链跑出来 —— 所以"实现了子接口"确实等于"实现了父接口",
            // 类型和成员一次给全。
            //
            // 现在接口之间只剩**要求**:`<:` 照着要求走(`IList <: IEnumerable` 成立),
            // 但**槽不并** —— 实现者得自己再登记一条 `impl (IEnumerable X { … })`,
            // 成员才在。这一格问的正是"**有没有那条实现**"(造实现的前置条件、
            // `x : IEnumerable` 收不收得下都看它),所以只能是精确匹配:
            // 松成 `<:` 的话,只写了 `impl (IList X {})` 也会被当成"实现了 IEnumerable",
            // 而 `x.GetEnumerator` **没有** —— 报错会挪到调用处,说不清是哪儿缺的。
            if (impl.Type == trait
                && impl.Scope.LookupField(TargetMember)?.Value is ObjectVal target
                && cls.IsAssignableTo(target))
                return true;

        return false;
    }

    private static int _registryVersion;

    private static void BumpRegistry() => _registryVersion++;
}

internal readonly record struct TraitHit(ObjectVal Impl, RuntimeValue Receiver, Variable Slot);
