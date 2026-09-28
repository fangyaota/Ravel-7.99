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
/// - **读写 `x.a` 常规查找落空时**才问接口:沿**当前作用域**的词法链找生效中的实现,把那条槽
///   **绑到这一次的接收者 x** 上(见 <see cref="Activate"/>)再交出去,这一次读/写就用它。
///   于是效果**随作用域在/不在** —— 出了 `use` 那个作用域,`x.a` 又回到「类型 'myClass' 没有方法 'a'」。
/// - **`instance` 是"这一次调用"的事,不是实现身上的一格**:它是一条 `by` 槽,读的时候现场解析
///   "此刻在服务谁"(见 <see cref="InstallInstance"/> 与 `Interpreter.ActiveInstance`)——
///   值随那次访问的作用域走,所以留存下来的闭包永远指它自己那个实例。
///
/// `myClass` 这个名字**从头到尾没被动过**:不建子类、不换绑定、不往它身上加成员。
internal static partial class BuiltinClasses
{
    // `Interface`(接口的权威名,小写别名 `interface` 在 predefined.rav;parent 是 `type`,
    // 所以它也是类,而它造的类(接口)同样进类型树)和别的内置类一起声明在 BuiltinClasses.cs。

    /// <summary>实现 scope 里那条 `instance` **槽**(`by` 槽,值是 property)。
    ///
    /// 它由 <see cref="InstallInstance"/> 装上,getter 每次读的时候现场解析"这一次调用在服务谁"。
    /// 从前它是实现身上的一格**变量**,每次查找被换成接收者 —— 那一格是所有访问共用的,
    /// 于是留存下来的东西(闭包、`by u.a` 取到的 property)会跟着后一次访问改意思。
    /// 它**不参与**"`u.x` 能读到什么":<see cref="TraitSlot"/> 见到这个名字就退回去,否则
    /// `u.instance` / `(5).instance`(全局 INumber 实现的目标类就是 int)会变成能读的新成员。</summary>
    internal const string InstanceMember = "instance";
    /// <summary>这一次分发的**激活格**:临场开一层作用域,里面放"这次服务谁"(`instance$active`)
    /// 和"这次是哪个实现在服务"(`impl$active`,拿它认身份证),槽的 getter/setter 绑到那层上跑
    /// (见 <see cref="Activate"/>)。读 `instance` 就是沿当前调用链找最近一层激活格。
    ///
    /// 名字里的 `$` 不在 Ravel 标识符字符集里,用户写不出来(同 `use$impls`),永远不会撞。</summary>
    internal const string InstanceActiveMember = "instance$active";
    internal const string ImplActiveMember = "impl$active";
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
    private static FunctionVal _interfaceInit = null!;

    /// <summary>`interface` 的 init,按参数类型分流(和 `type` 那套一个路子):
    ///
    ///     interface { … }                  代码块 —— 不继承、不要求
    ///     interface 某接口 { … }            接口 —— 继承一个父
    ///     interface 某接口 [A B] { … }      接口,后面再跟一张**要求**表
    ///     myTrait 某个类 { … }              类 —— 造实现(本体不是 `interface` 时)
    ///
    /// 「要求」和「父」是两回事:父是**继承**(槽并进来、`<:` 成立),要求是**前置条件** ——
    /// 实现这个接口的类必须**已经**有那些接口的实现(槽不并、`<:` 不成立,只查在不在;
    /// 查在造实现那一步,见 `StepImplMake`)。光有要求、没写父的 `interface [A B] { … }` **不收**。
    ///
    /// **顺序是载荷**:接口和类是同一支(都声明 `Type`,进到分支体里靠"谁在造"再分),
    /// 得排在代码块那支前面 —— `ClassVal : FunctionVal`,反过来类对象和接口都会被它吃掉。
    ///
    /// 第二三个参数是**可选**的,所以接口那支交出去的又是**一个小分流器**(要求表 / 代码块)
    /// —— 那是先后两次应用,不是嵌套;那条"不能嵌套"管的是同一个帧里试分支。</summary>
    private static void InstallInterfaceInit()
    {
        // 接口 / 类:一个类型,接着等代码块(接口还允许先来一张要求表)
        var ofArg = new NativeClosure("of", Type, (scope, of) =>
        {
            // 本体(`this` 的类型)就是"谁在造":`interface …` 时它是 `Interface` 自己,
            // `某接口 某个类 { … }` 时它是那个接口 —— 靠它把"接口继承"和"类实现"分开。
            var driver = ((ObjectVal)scope.Lookup(ObjectVal.ThisMember).Value).Type;

            if (of is ObjectVal { Type: var meta } o && meta.IsAssignableTo(Interface))
                return Alternate(
                    new NativeClosure("requires", List, (_, rs) =>
                        FunctionVal.From(body => BuildInterface(scope, o, Requirements((ListVal)rs), body))),
                    new NativeClosure("body", Function, (_, body) => BuildInterface(scope, o, [], body)));

            if (driver == Interface)
                throw new RuntimeException($"`interface` 只能继承接口（{of} 是个类）；"
                    + "要给某个类实现接口就写成 `某个接口 那个类 { … }`");

            return new ControlFunction(ControlKind.ImplMake, 3, RList<RuntimeValue>.Empty.Add(driver).Add(of));
        });

        // 代码块:不继承、不要求
        var bodyArg = new NativeClosure("body", Function, (scope, body) => BuildInterface(scope, null, [], body));

        // 光有要求、没写父 —— 不收
        var listArg = new NativeClosure("requires", List, (_, _) => throw new RuntimeException(
            "要求得跟在父接口后面：`interface 那个接口 [A B] { … }`"));

        // 别的:把能写什么说全(不然只会得到「| 的 N 个分支都不收这个参数」)
        var junkArg = new NativeClosure("_", Any, (_, v) => throw new RuntimeException(
            "`interface` 后面要跟一个代码块（`interface { … }`）或一个接口（`interface 某接口 { … }`），"
            + $"得到 {v.Type}"));

        _interfaceInit = Alternate(ofArg, bodyArg, listArg, junkArg);
        Interface.ClassBody = PresetCtor(_interfaceInit);
    }

    /// <summary>要求表(`[A B]`)的每一项都得是个**接口**;空表就是不要求。</summary>
    private static List<ObjectVal> Requirements(ListVal list)
    {
        var reqs = new List<ObjectVal>();
        for (var i = 0; i < list.Elements.Count; i++)
        {
            if (list.Elements[i] is not ObjectVal { Type: var meta } o || !meta.IsAssignableTo(Interface))
                throw new RuntimeException($"要求表里得全是接口，第 {i + 1} 个是 {list.Elements[i].Type}");
            reqs.Add(o);
        }

        return reqs;
    }

    /// <summary>造一个接口:`interface { … }`(无父)、`interface 某接口 { … }`(一个父)、
    /// 或者再加一串要求(`interface 某接口 [A B] { … }`)。
    ///
    /// `parent` 就是链上那个(没有 = `object`)—— 类型树、`Subtypes ()`、成员查找、实例化
    /// 全照旧靠它;`requires` 记在 <see cref="ObjectVal.RequiresMember"/> 里,**只**给
    /// `StepImplMake` 查前置条件用(不进槽、不进 `<:`)。
    ///
    /// 类体把父的**声明**接在自己前面一起烤(见 <see cref="BakeInterfaceInit"/>)——
    /// 于是这个接口的"形"是自足的:`myTrait myClass { … }` 推它一把就拿到了全部槽。</summary>
    private static RuntimeValue BuildInterface(Scope scope, ObjectVal? parent, List<ObjectVal> requires, RuntimeValue body)
    {
        if (body is not BlockVal blk)
            throw new RuntimeException($"接口的体得是个代码块（`interface … {{ … }}`），得到 {body.Type}");

        var trait = Install(scope, parent ?? Object, blk);

        if (requires.Count > 0)
            trait.Scope.DefineOrReplace(ObjectVal.RequiresMember, List, new ListVal([.. requires]))
                .SetAttr(Attr.Unreadable);

        BakeInterfaceInit(trait, blk, parent);

        // `impl.Dispose ()` 挂在**接口**上,所有实现共用这一份(不必每个实现塞一个闭包)。
        // DefineMethod 存的是 ISelfBinding 的内置方法:读成员时才绑接收者,于是 self 就是
        // **拿到的那个实现** —— `Copy ()` / `with` 出来的副本绑的是它自己,不会误伤原件。
        trait.DefineMethod(DisposeMember, (self, _) => DisposeImplementation((ObjectVal)self));
        return trait;
    }

    /// <summary>把接口自己的 `init`(以及父的**声明**)烤进这个接口的**类体**。
    ///
    /// 为什么非烤 `init`:接口对象的 parent 是 **`object`**(和 C# 一样 —— 接口不是"继承了一个
    /// 叫 Interface 的基类"),于是它继承不到 `Interface` 那个预设类体里的 `init`。而
    /// `myTrait myClass { … }` 是**实例化 myTrait**,得在它自己的类体里找得到这个 `init`。
    /// 塞进去之后:实例化时照跑(造实现),而 `StepImplMake` 拿它当"接口的形"跑那一步时,
    /// 也只是在实现 scope 里多出一个没人看的 `init` 成员(不是 `by` 槽,所以不会被接口槽的
    /// 查找接管)。
    ///
    /// 为什么把父的声明也接上:槽要**并集**(父的那个是它"形"的一部分)—— 把父的那些
    /// `by … = default` 抄进来最省事,而且**父自己早就把它的父抄进来了**,所以一层不落。
    /// 抄的时候跳过父烤在最前面的那个 `init`(每个接口的类体都以它开头)。
    /// 父的在前、自己的在后:同名槽由后写的说了算(`DefineOrReplace`)。
    ///
    /// 类体必须是新的 BlockExpr(带用户那份的 Source/行列):原样改 `block` 会动到用户写的
    /// 那个 BlockExpr,`Source` 也就丢了。</summary>
    private static void BakeInterfaceInit(ObjectVal trait, BlockVal blk, ObjectVal? parent)
    {
        var statements = new List<Statement>();
        if (parent?.ClassBody is { } pb)
            statements.AddRange(pb.Block.Statements.Skip(1));   // 跳过父烤的那个 init

        statements.AddRange(blk.Block.Statements);

        var init = new VarDefinition(ObjectVal.InitMember, null, new LiteralExpr(_interfaceInit))
        {
            Line = blk.Block.Line,
            Column = blk.Block.Column,
        };

        trait.ClassBody = new BlockVal(
            new BlockExpr([init, .. statements])
            {
                Line = blk.Block.Line,
                Column = blk.Block.Column,
                Source = blk.Block.Source,
            },
            blk.CaptureScope);
    }

    /// <summary>实现对象的最后一道装填:目标类、代号、`this`,以及那条 `instance` 槽
    /// (见 <see cref="InstallInstance"/>);`Dispose` 挂在接口那份上。</summary>
    internal static void FinishImplementation(Interpreter interp, ObjectVal impl, ObjectVal target)
    {
        impl.Scope.Define(TargetMember, Type, target);
        impl.Scope.Define(GenerationMember, Int, new IntVal(0)).SetAttr(Attr.Unreadable);
        impl.Scope.Define(ObjectVal.ThisMember, impl.Type, impl);
        InstallInstance(interp, impl);
    }

    /// <summary>装上 `instance`:一条 `by` 槽(`Attr.By`),值是 property。
    ///
    /// getter 是 C# 侧的原生闭包 —— 它的体按**调用点作用域**跑(`Interpreter.Call.cs` 的
    /// `NativeClosure` 那一臂),所以它正好落"读 `instance` 的那一帧"上,再问求值器
    /// "这一次调用在服务谁"(<see cref="Interpreter.ActiveInstance"/>)。
    /// setter 只报错:正常写被 `Attr.Readonly` 先挡在半路(`WriteVariable` 的 `CheckWritable`),
    /// 只有 `(by x.instance).Set v` 那种绕开 `Variable` 的路会撞上它 —— 让它出声。
    ///
    /// **前提**:一个实现只被它那个解释器服务 —— 实现是运行时由 `StepImplMake` 造的,
    /// `lib/*.rav` 里那几条 `impl` 每个解释器各建一遍,所以 getter 拎着 interp 是安全的。</summary>
    private static void InstallInstance(Interpreter interp, ObjectVal impl)
    {
        if (impl.Scope.LookupField(InstanceMember) != null)
            throw new RuntimeException($"接口 {impl.Type.DisplayName} 里声明了名为 '{InstanceMember}' 的槽"
                + " —— 那是引擎自己的名字（它指的是「这一次在服务谁」），换一个");

        var getter = new NativeClosure(InstanceMember, Any, (scope, _) =>
            interp.ActiveInstance(scope, impl) ?? throw new RuntimeException(
                "`instance` 只在实现体的槽里有效：此刻没有正在被服务的实例"
                + "（它指的是「这一次调用在服务谁」，离开那次调用就没有了）"));
        var setter = new NativeClosure(InstanceMember, Any, (_, _) => throw new RuntimeException(
            "`instance` 是只读的：它由引擎指到这一次服务的实例，不能赋值"));

        var vr = impl.Scope.Define(InstanceMember, Any, new PropertyVal(getter, setter));
        vr.SetAttr(Attr.By);
        vr.SetAttr(Attr.Readonly);
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
    /// 找到就交出**命中**(哪条槽、哪个实现、这次服务谁)—— 真正要用它的时候由调用点走
    /// <see cref="Activate"/> 绑上这一次的接收者。**这里不建激活格**:有一半调用点只是
    /// "问一句有没有"(`HasTraitOperator` 每次类型表落空的二元运算都会问),在那儿白建一层作用域
    /// 是纯浪费。找不到返回 null,由调用点报原来的「没有方法」。</summary>
    internal static TraitHit? TraitSlot(Interpreter interp, ObjectVal receiver, string name)
    {
        // `instance` 是机制自己那条槽,不进"`u.x` 能读到什么"(见 InstanceMember 的说明)。
        // 槽体里读的是**裸名字**,词法链直接命中实现 scope,不走这儿。
        if (name == InstanceMember) return null;
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
                return new TraitHit(impl, receiver, slot);
            }
        }

        return null;
    }

    /// <summary>把一条接口槽**绑到这一次的接收者**上:临场开一层作用域,里面放着"这次服务谁"
    /// (`instance$active`)和"这次是哪个实现在服务"(`impl$active`),再把槽的 getter/setter
    /// 换到那层上跑。
    ///
    /// **值本身不落在实现身上** —— 这是这套机制的关键:槽体里造的闭包捕获的是创建处的作用域
    /// (`Interpreter.Nodes.cs` 的 `StepLambda`),于是留存下来的闭包各自拎着自己那一层,
    /// 永远指它自己那个实例;同一个实现也就能同时服务多个实例。
    ///
    /// 动的是**交出去的那一份副本**:实现 scope 里那条槽一个字没改,所以 `by u.a` 取到的还是
    /// 原来那条 property,`Fields ()` / `Copy ()` 也照旧。</summary>
    internal static PropertyVal Activate(Interpreter interp, in TraitHit hit)
    {
        // 不是 property(`by v := 5` 那种用错)就在这一句上报出来,措辞和普通 by 成员一条路
        var prop = interp.SlotProperty(hit.Slot.Value, hit.Slot.Name);

        var act = new Scope(prop.Getter.CaptureScope);   // 保住原来的名字解析链,只多两层
        act.Define(InstanceActiveMember, Any, hit.Receiver);
        act.Define(ImplActiveMember, Type, hit.Impl).SetAttr(Attr.Unreadable);

        return prop with
        {
            Getter = RebindFn(prop.Getter, act),
            Setter = RebindFn(prop.Setter, act),
            Var = null,                                  // 副本不再挂在哪个变量上(`Attrs ()` 用不到了)
        };
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

                // 实现了子接口就等于实现了它的父接口(照 C#):把闭包里的接口都列上
                foreach (var face in InterfaceClosure(impl.Type))
                    if (!found.Any(x => ReferenceEquals(x, face)))
                        found.Add(face);
            }
        }

        return new ListVal(found);
    }

    /// <summary>一个接口**自己以及它继承的所有接口**,按"先自己、再往上"排。
    ///
    /// 沿链走(**要求**不算 —— 要求是前置条件,不是继承:一个类实现它得自己另外 impl 一条,
    /// 那条本来就登记着,查询自然会看到)。走到 `object` 那头不是接口,滤掉,
    /// 所以普通类拿到的是空表。</summary>
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

    /// <summary>`I.GetImplementors ()`:`GetImplements ()` 的反面 —— **现在有哪些类型**实现了这个接口
    /// (目标类组成的 list)。同样是一份"当下"的快照、同样只认生效中的实现。
    ///
    /// 普通类问它永远是空的:接口槽是"实现"挂上去的,而实现的 trait 只能是接口。
    /// 顺序和 `GetImplements ()` 一个规矩(由内到外、后 `use` 的先),同一个目标只列一次。</summary>
    internal static RuntimeValue Implementors(Interpreter interp, ObjectVal trait)
    {
        var found = new List<RuntimeValue>();
        for (var s = interp.CurrentScope; s != null; s = s.Parent)
        {
            if (s.LookupField(UseRegMember)?.Value is not ListVal reg) continue;

            for (var i = reg.Elements.Count - 1; i >= 0; i--)
            {
                if (!IsLiveEntry(reg.Elements[i], out var impl)) continue;
                // 实现子接口的也算这个接口的实现者(照 C#:`IEnumerable` 的实现者里有谁实现了子接口)
                if (!impl.Type.IsAssignableTo(trait)) continue;
                if (impl.Scope.LookupField(TargetMember)?.Value is not ObjectVal target) continue;
                if (found.Any(x => ReferenceEquals(x, target))) continue;
                found.Add(target);
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
                // `IsAssignableTo` 而不是 `==`:实现了子接口也就实现了父接口(照 C#)
                if (IsLiveEntry(e, out var impl) && impl.Type.IsAssignableTo(trait)
                    && impl.Scope.LookupField(TargetMember)?.Value is ObjectVal target
                    && cls.IsAssignableTo(target))
                    return true;
        }

        return false;
    }
}

/// <summary>一次接口分发的命中:哪条槽、是**哪个实现**身上那条、这一次服务谁。
///
/// 它只是一份"找着了"的记录,还**没有**绑接收者 —— 要真用它(读/写/用它的运算符)时由调用点走
/// <see cref="BuiltinClasses.Activate"/> 拿"绑好这一次"的那一份。分两步是因为半数的调用点
/// 只是问一句"有没有"(`HasTraitOperator`),在那些地方建激活格纯属浪费。</summary>
internal readonly record struct TraitHit(ObjectVal Impl, ObjectVal Receiver, Variable Slot);
