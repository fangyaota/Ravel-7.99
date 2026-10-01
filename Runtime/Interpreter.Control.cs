namespace Ravel.Runtime;

/// <summary>控制帧状态机:每个控制内建一个 ControlFrame,Args 存已收集的块/参数,
/// Results 与 State 记录推进进度(约定:Args 全程不变,进度看 Count/State)。</summary>
public partial class Interpreter
{
    private void StepControl(ControlFrame cf)
    {
        switch (cf.Kind)
        {
            // if/while/foreach 不在这里:它们由 predefined.rav 用 Ravel 写(可调用的 true/false + callcc)
            case ControlKind.With: StepWith(cf); break;
            case ControlKind.CallCC: StepCallCC(cf); break;
            case ControlKind.Using: StepUsing(cf); break;
            case ControlKind.Eval: StepEval(cf); break;
            case ControlKind.Alternate: StepAlternate(cf); break;
            case ControlKind.ClassInit: StepClassInit(cf); break;
            case ControlKind.ImplMake: StepImplMake(cf); break;
            case ControlKind.SeqOp: StepSeqOp(cf); break;
            case ControlKind.Compose: StepCompose(cf); break;
            case ControlKind.ClassOp: StepClassOp(cf); break;
            case ControlKind.TraitOp: StepTraitOp(cf); break;
            case ControlKind.CallAssign: StepCallAssign(cf); break;
            case ControlKind.CallReturn: StepCallReturn(cf); break;
            case ControlKind.CtorApply: StepCtorApply(cf); break;
        }
    }

    /// <summary>`with (对象: 块)` —— **进到那个对象的成员表里跑一段**,不拷任何东西。
    ///
    /// 从前它是"先 `Copy ()` 一份、再在副本上跑",值也交回副本 —— 那是个**隐形的拷贝**:
    /// `with c { v = 10; }` 改的是副本,`c` 不动。两个毛病:每用一次整份拷一次(方法闭包、
    /// attrs 全在里面),而且"改副本、原件不动"这件事 `Copy ()` 说得更明白 ——
    /// `with` 本来该是**作用域**的事。
    ///
    /// 现在:块跑在原对象成员表上推出来的那一层里。于是
    /// <list type="bullet">
    /// <item>`v = 10` 这种**赋值**沿链落回原对象的字段 —— 改的就是它;</item>
    /// <item>`x := 1` 这种**定义**落在推出来的那一层,块一结束就没了(和块里新开的局部变量
    /// 一个待遇);</item>
    /// <item>交回的是那个对象本身。</item>
    /// </list>
    ///
    /// 一句话:`with` 只做一件事 —— 换一下"接下来这段代码算谁的成员"。</summary>
    private void StepWith(ControlFrame cf)
    {
        var obj = cf.Arg<RuntimeValue>(0, "with");
        var body = cf.Arg<BlockVal>(1, "with");
        if (cf.Count == 0)
        {
            // **不拷**:块跑在那个对象成员表上推出来的那一层里。
            // 有自己成员表的值(`HasOwnTable`:数据对象 / 容器 / Json)才这么走;
            // 其余的(原子值、函数、类对象、模块、属性、作用域值)块跑在自己的捕获作用域里
            // —— 判据不能写 `is ObjectVal`:函数也是 ObjectVal,那样会把块的作用域换成
            // 函数自己的成员表。
            var bodyScope = BuiltinClasses.HasOwnTable(obj)
                ? ((ObjectVal)obj).Scope.Push()
                : body.CaptureScope.Push();
            _top = new BlockExecFrame(body.Block) { Parent = cf, Scope = bodyScope };
            return;
        }

        Return(cf, obj);
    }

    /// <summary>callcc:把「捕获点之后要算的东西」作为续延交给 lambda。
    /// 续延被调用时由 CallInto 负责切回捕获点(见 ContinuationVal 分支)。</summary>
    private void StepCallCC(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            // 控制状态的拍 / 还原由**库**决定(见 predefined.rav 里 callcc 的包装),引擎不掺和
            CallInto(cf, cf.Arg<RuntimeValue>(0, "callcc"), new ContinuationVal(cf));
            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>using/ravel 的模块加载。模块体执行期间要一直把它记在 `_loading` 里,
    /// 循环引用才检测得到——所以 push 在这里、pop 在块跑完那一步(State 存路径,顺带当已 push 的凭据)。
    /// pop 依赖帧的正常收尾,而 callcc 逃生会把这条链整个丢掉 —— 所以 `_loading` 也在
    /// `System.ControlState` 那份快照里,由库里的 `callcc` 包装在续延被调时还原
    /// (见 `Interpreter.SnapshotControl` / `RestoreControl`)。</summary>
    private void StepUsing(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var path = cf.Arg<StringVal>(0, "using").Value;
            var ast = LoadModuleAst(path);
            if (ast == null)
            {
                Return(cf, VoidVal.Instance);
                return;
            }

            var full = ResolveModulePath(path)!;   // 上面刚解析成功过
            _loading.Push(full);
            _top = new BlockExecFrame(ast) { Parent = cf with { State = new StringVal(full) }, Scope = cf.Scope };
            return;
        }

        if (cf.State is StringVal s) _loading.Pop();
        Return(cf, VoidVal.Instance);
    }

    private void StepEval(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var code = cf.Arg<StringVal>(0, "eval").Value;
            // 给个合成文件名:不带的话语法错误只能报 `--> 3:1`,不知道那是 eval 出来的
            _top = new BlockExecFrame(Parser.ParseBlock(code, "<eval>")) { Parent = cf, Scope = cf.Scope };
            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>函数交替 `f | g | h`:按顺序试,第一个收得下这个参数的分支胜出。
    ///
    /// 分支在 `|` 那边就摊平成一个列表(见 `BuiltinClasses.Operators` 的 Function.|),
    /// 所以这里一个帧顺序试完即可。**不能嵌套着试**:`CallInto` 只推帧、不当场调用,
    /// 内层交替的 TypeMismatchException 是在外层这个 try 之外才抛的,
    /// 外层 catch 早返回了 —— 那样第三个分支永远试不到。
    /// `Args = [分支…, 参数]`。</summary>
    private void StepAlternate(ControlFrame cf)
    {
        if (cf.Count > 0)
        {
            Return(cf, cf.Result(0));
            return;
        }

        var arg = cf.Args.Last;
        var n = cf.Args.Count - 1;          // 前面全是候选分支
        TypeMismatchException? last = null;

        for (var i = 0; i < n; i++)
        {
            try
            {
                // 参数类型对不上会在 CallInto 里**同步**抛出,于是就地接着试下一支
                CallInto(cf, cf.Args.At(i), arg);
                return;
            }
            catch (TypeMismatchException ex)
            {
                last = ex;
            }
        }

        throw new TypeMismatchException($"| 的 {n} 个分支都不收这个参数（最后试的：{last?.Message}）");
    }

    /// <summary>跑**这一层**类体时用哪个作用域。
    ///
    /// 类的每层用 <see cref="BodyScope"/>(词法父 = 这一层写在哪),各层的定义转发回同一个
    /// 实例表;而**接口**那半照旧直接跑在实例表上 —— 接口体是和实现块共环境的(实现写在哪,
    /// 接口体就在哪解析,`use` 出去的那些实现才对它可见),由 `StepImplMake` 那次
    /// `Reparent` 负责把它接到实现处。</summary>
    private static Scope LayerScope(ClassVal type, Scope lexical, Scope instanceScope)
        => type.IsAssignableTo(BuiltinClasses.BaseInterface)
            ? instanceScope
            : new BodyScope(lexical, instanceScope);

    /// <summary>类实例化:沿祖先链(顶祖先→自身)依次跑每层类体,**全平铺在同一个 instanceScope**
    /// (所以同名成员是"后写盖先写",没有分层的成员表)。跑完之后只做一件事:在**那一个** scope 里
    /// 按名字取 `init` 来调 —— 拿到的自然是最后写的那条(最具体那个类声明的);没写过的落到
    /// `Object` 那条默认构造。不是"每层各调一次 init",也不是另走一遍链去找。
    /// ObjectVal 与 this 在第一个类体执行前就绑好(ClassType = 最终子类)。
    /// 阶段由 Count 推进:0=建 scope/绑 this/推第一层,(0,N)=推第 Count 层,N=调 init,&gt;N=返回对象。
    /// 只允许 CallTypeInto 构造本帧(它保证 Count==0、State==VoidVal),别处复用会破坏 State 形状假设。
    ///
    /// **类的每一层跑在自己的 <see cref="BodyScope"/> 上**(词法父 = 那一层**写在哪**):
    /// 各层的定义照旧落回同一个实例表(平铺),而自由名字各按各的写法处解析 —— 从前只按
    /// "最具体那个类"的(父类写在另一个作用域时,它的类体就看不见自己那儿的名字)。
    ///
    /// **接口那半不这么走**:接口体是"给实现用的"(槽要落在实现身上),它和实现块共用一个
    /// 环境 —— 实现写在哪,接口体就在哪解析。所以它还留着"先建一个 scope、实现帧再
    /// `Reparent` 到实现处"那条老路(见 `StepImplMake` 与 `Scope.Reparent`)。</summary>
    private void StepClassInit(ControlFrame cf)
    {
        var type = cf.Arg<ClassVal>(0, "class");
        var arg = cf.Arg<RuntimeValue>(1, "class");
        // 顶祖先 → 自身,≥ 1 步。一步 = 一块;`body.Append { … }` 拼上来的块各自算一步,
        // 排在同一层主块的后面(见 `BuiltinClasses.CollectBodies`)。
        var bodies = BuiltinClasses.CollectBodies(type);

        if (cf.Count == 0)
        {
            var instanceScope = new Scope(type.ClassBody!.CaptureScope);
            // 造出来的东西是类还是实例,取决于**被实例化的那个类**是不是 `type` 的子类
            // (走 `parent` 原型链,不是"元类"那条):
            //   `type { body }` → `type <: type` 自反 → 造出来的就是类对象(ClassVal)
            //   `MyMeta := class type {…}` → MyMeta <: type → 同上
            //   `C := class {…}` 的实例 → C 的 parent 链是 `C → object`,不含 type → 普通实例
            // 注意这只定下**中间对象**的类型:构造最终交出的是 init 的返回值(见下面 Return),
            // 所以只有"init 以 this 收尾"的用户类/元类才真把这个对象交出去。
            ObjectVal obj = type.IsAssignableTo(BuiltinClasses.Type)
                ? new ClassVal(type, instanceScope)
                : new ObjectVal(type, instanceScope);
            instanceScope.Define(ObjectVal.ThisMember, type, obj);
            _top = new BlockExecFrame(bodies[0].Block.Block)
            {
                Parent = cf with { State = obj },
                Scope = LayerScope(type, bodies[0].Lexical, instanceScope)
            };
            return;
        }

        var inst = (ObjectVal)cf.State; // Count > 0 起 State 恒为 ObjectVal

        // 第 Count 层刚跑完 → 推下一层(定义照旧落回同一个实例表,名字走这一层自己的写法处)
        if (cf.Count < bodies.Count)
        {
            _top = new BlockExecFrame(bodies[cf.Count].Block.Block)
            {
                Parent = cf,
                Scope = LayerScope(type, bodies[cf.Count].Lexical, inst.Scope)
            };
            return;
        }

        if (cf.Count == bodies.Count)
        {
            // 构造器就是实例作用域里名为 init 的那个:各层平铺在同一 scope,
            // 子类的 init 覆盖父类的,所以直接找名字 = 只调最具体层声明的那个
            var init = inst.Scope.LookupField(ObjectVal.InitMember)?.Value as FunctionVal
                       ?? throw new RuntimeException($"类型 {type.DisplayName} 没有构造器（init）", ErrorKind.Type);
            // 调用点用**实例作用域**:init 要能看见 `this` 和各层类体落的成员。
            // 内置类的默认建类函数(NativeClosure)正是靠这个把 parent/block 装到 self 上。
            //
            // **必须改在帧自己身上**(`cf` 就是 `_top`),不能写成 `CallInto(cf with {…})`:
            // 那种写法只换了 sink 的 scope,而 `CallInto` 里取调用点作用域的两条路走的是
            // **`_top.Scope`**(NativeClosure 与类运算符),不是 sink 的 —— 于是直接拿
            // NativeClosure 当 init 的类(比如 object 那个默认构造器)会报「未定义的变量 'this'」。
            // (从前 `type` 的 init 没露馅只是碰巧:它被 `Alternate` 包着,中间那个控制帧
            //  是拿 `sink.Scope` 建的,scope 因此蒙对了。)
            cf.Scope = inst.Scope;
            CallInto(cf, init, arg);
            return;
        }

        // Count 超过层数:init 已返回(用 > 而非 == :callcc 续延重入可能把 Count 顶过头)。
        // Results 里前面几项是各层类体的返回值,最后一项才是 init 的——所以取 Last 而不是 Result(0)。
        // init 若还返回函数(多参构造器只喂了一部分)就交出半成品,让它继续收参数,和普通函数一样柯里化
        // **构造交出 init 的返回值** —— 不再无条件交出实例。
        // init 约定以 `this` 收尾,所以普通类拿到的还是实例;
        // 而元类的 init 可以建出一个类再交出来(或返回别的什么)。
        // init 若还返回函数(多参构造器只喂了一部分)就交出半成品,和普通函数一样柯里化。
        Return(cf, HalfCtor(inst, cf.Last));
    }

    /// <summary>init 交回的东西是"还没收齐参数的半成品构造器"吗 —— 是就包成
    /// <see cref="PartialCtor"/> 让它接着收参数,不是就原样交出去。
    ///
    /// 判据是 `IsClosure`(「是个闭包形状的函数」)再排掉**可调用、但调用起来不是
    /// "接着收构造参数"**的那几种:`bool`(收两个块)、类对象(实例化它)已经在
    /// `IsClosure` 那边排掉了,续延也得排 —— 调续延是**跳转**,不是喂参数。
    /// 排不掉的话 `Continuation f` 一造出来就被包成半成品,`typeof` 立刻看不出它是续延。</summary>
    private static RuntimeValue HalfCtor(ObjectVal target, RuntimeValue returned)
        => returned is FunctionVal rest && rest.IsClosure && returned is not ContinuationVal
            ? new PartialCtor(target, rest)
            : returned;

    /// <summary>把**实现块**装进实现对象(`myTrait myClass { 实现体 }`)。阶段由 Count 推进:
    /// 0=推实现块,1=收尾并交出去。
    ///
    /// **两类东西两处跑**(和普通类一个规矩):
    /// - 接口那几层类体(顶祖先 → 自身)由**实例化那趟**跑(`StepClassInit` 沿 `CollectBodies`
    ///   依次跑进它建的那个对象的 scope)—— 这些体是"配方",每造一个实现跑一遍;
    /// - **实现块**在这儿追加:它先把 `by a : int = default` 那些槽摆好(在类体那趟里),
    ///   实现块再用 `by a = property …` 换掉 —— 所以实现体里写的是"换"(`=`),不是"建"(`:=`)。
    ///
    /// 于是接口体那些副作用**只跑一次**(从前实例化那趟跑一遍、这里又跑一遍)。
    ///
    /// 收尾时 <see cref="BuiltinClasses.FinishImplementation"/> 往这个对象上装
    /// `target`/`generation`/`instance`(`instance` 读的时候现场解析"这一次调用在服务谁",
    /// 见 `BuiltinClasses.Activate`);实现块里创建的 lambda 捕获的正是这个 scope,所以它们
    /// 看得见 `instance`。</summary>
    private void StepImplMake(ControlFrame cf)
    {
        var impl = cf.Arg<ObjectVal>(0, "ImplMake");
        var target = cf.Arg<ObjectVal>(1, "ImplMake");
        var body = cf.Arg<BlockVal>(2, "ImplMake");

        if (cf.Count == 0)
        {
            // 「要求」是**前置条件**:这个类得**已经**有那几个接口的实现(查当下作用域里有没有
            // 生效中的)。放在造实现这一步,是因为"已经实现"是作用域里的事(实现登记在 scope 上),
            // 而这里手里正好有求值器。
            var trait = impl.Type;   // 接口对象(这个实现的类)—— 要求表挂在它那张
            if (trait.Scope.LookupField(ObjectVal.RequiresMember)?.Value is ListVal reqs)
                foreach (var r in reqs.Elements)
                    if (r is ObjectVal rt && !BuiltinClasses.HasTrait(this, target, rt))
                        throw new RuntimeException($"`{trait.DisplayName}` 要求 {target.DisplayName} "
                            + $"已经实现了 {rt.DisplayName}（先给它 impl/use 一条）", ErrorKind.Type);

            // 接口那几层类体已经跑过了(实例化那趟跑的,跑的就是这个对象的 scope)——
            // 这里只追加**实现块**。但这张表得换一个词法父:它由那趟建出来时接的是
            // **接口定义**处,而实现块的自由名字该在**实现写在哪**解析。
            impl.Scope.Reparent(body.CaptureScope);
            _top = new BlockExecFrame(body.Block) { Parent = cf, Scope = impl.Scope };
            return;
        }

        BuiltinClasses.FinishImplementation(this, impl, target);
        Return(cf, impl);
    }

    /// <summary>容器上"逐个跑一遍"的那批方法:Each / Map / Where / Fold / All / Any / Find / SortBy
    /// (成员值是 <see cref="SeqMethod"/>,读出来绑好接收者就交出这个帧)。
    ///
    /// 一步一个元素,每步调一次用户函数,收回来的结果接着走 —— 所以它们只能是控制帧:
    /// 原生闭包**调不了 Ravel 函数**(那是帧栈的活)。
    ///
    /// `Args` = [容器, 模式, 函数(+ Fold 的初值)];`State` = 一个 ListVal:
    /// [0] = 元素表(进来时取一次就不再多问容器 —— 否则每步都重新枚举一遍),
    /// [1] = 攒的东西(各模式:Map/Where/SortBy 攒一个 list、Fold 攒累积值、All 攒 bool、
    /// Find 攒"找到的那个元素"、Each 不用)。`Count` = 已经调过几次函数。
    ///
    /// 顺序就是 `ElementsOf` 给的顺序(将来 `IEnumerable` 出来,这里改成问枚举器要)。
    /// 提前收工:All 撞上 false、Any 撞上 true、Find 找到第一个 —— 剩下的元素不再调函数。</summary>
    private void StepSeqOp(ControlFrame cf)
    {
        var self = cf.Arg<RuntimeValue>(0, "SeqOp");
        var mode = (SeqMode)cf.Arg<IntVal>(1, "SeqOp").Value;
        var label = SeqMethod.Label(mode);

        // 头一步:备好 [元素表, 累加器]。`State` 是 init-only,所以只能整帧换一个 ——
        // `_top` 也要跟着换:后面 CallInto 的 sink 得收这个新的,不然结果会落到那个
        // 还揣着 VoidVal 的旧帧上。
        if (cf.State is not ListVal state)
        {
            cf = cf with { State = NewSeqState(mode, label, self, cf) };
            _top = cf;
            state = (ListVal)cf.State;
        }

        var elems = ((ListVal)state.Elements[0]).Elements;
        var acc = state.Elements[1];

        // 函数在第几个参数上:Fold 收两个(初值在前、函数在后),别的都只收函数
        var fnAt = mode == SeqMode.Fold ? 3 : 2;

        // `Any` 两种用法共用一个名字(C# 的 `Any()` / `Any(pred)` 也是这样):给个 `()` 就是
        // "有没有元素",给函数就是"有没有满足的"。别的模式没有无参形式,给别的就报错。
        var arg = cf.Arg<RuntimeValue>(fnAt, label);
        if (arg is not FunctionVal fn)
        {
            if (mode == SeqMode.Any && arg is VoidVal)
            {
                Return(cf, new BoolVal(elems.Count > 0));
                return;
            }

            throw new RuntimeException($"{label} 需要一个函数参数，得到 {arg.Type}", ErrorKind.Argument);
        }

        var done = cf.Count;                     // 已经调过几次

        if (mode == SeqMode.Fold)
        {
            // 每个元素两步:先 `f 累积值`(拿回一个还在等元素的函数),再喂这个元素
            if (done > 0 && done % 2 == 0) state.Elements[1] = acc = cf.Last;
            if (done % 2 == 1)
            {
                CallInto(cf, cf.Last, elems[done / 2]);
                return;
            }

            if (done / 2 >= elems.Count)
            {
                Return(cf, acc);
                return;
            }

            CallInto(cf, fn, acc);
            return;
        }

        // 其余模式:一个元素一步。先把上一步的结果收下
        if (done > 0)
        {
            var got = cf.Last;
            var elem = elems[done - 1];
            switch (mode)
            {
                case SeqMode.Each:
                    break;
                case SeqMode.Map:
                    ((ListVal)acc).Elements.Add(got);
                    break;
                case SeqMode.SortBy:
                    // 攒「键 + 元素」一对,最后按键排(键留着,元素才是要交出去的)
                    ((ListVal)acc).Elements.Add(new ListVal([got, elem]));
                    break;
                case SeqMode.Where:
                    if (Yes(got, label)) ((ListVal)acc).Elements.Add(elem);
                    break;
                case SeqMode.All:
                    if (!Yes(got, label))
                    {
                        Return(cf, new BoolVal(false));
                        return;
                    }

                    break;
                case SeqMode.Any:
                    if (Yes(got, label))
                    {
                        Return(cf, new BoolVal(true));
                        return;
                    }

                    break;
                case SeqMode.Find:
                    if (Yes(got, label))
                    {
                        Return(cf, elem);
                        return;
                    }

                    break;
            }
        }

        if (done < elems.Count)
        {
            CallInto(cf, fn, elems[done]);
            return;
        }

        Return(cf, SeqResult(mode, acc));
    }

    /// <summary>各模式开局的累加器(Fold 的初值是它的第 4 个参数)。</summary>
    private static ListVal NewSeqState(SeqMode mode, string label, RuntimeValue self, ControlFrame cf)
        => new([
            new ListVal(BuiltinClasses.ElementsOf(self, label)),
            mode switch
            {
                SeqMode.Map or SeqMode.Where or SeqMode.SortBy => new ListVal([]),
                SeqMode.All => new BoolVal(true),
                SeqMode.Any => new BoolVal(false),
                SeqMode.Fold => cf.Arg<RuntimeValue>(2, "Fold"),   // `Fold 初值 函数`:初值在前
                _ => VoidVal.Instance,
            },
        ]);

    /// <summary>谓词要交回 bool —— 不是就当场说清楚(别把 `()` 当假)。</summary>
    private static bool Yes(RuntimeValue got, string label)
        => got is BoolVal b
            ? b.Value
            : throw new RuntimeException($"{label} 的函数要交回 bool，得到 {got.Type}", ErrorKind.Type);

    /// <summary>收工时交出去的东西。Find 没找到是**错误**(「没有满足的」和「找到一个是 ()」
    /// 不该长得一样);SortBy 这时候按键排(稳定)。</summary>
    private static RuntimeValue SeqResult(SeqMode mode, RuntimeValue acc) => mode switch
    {
        SeqMode.Each => VoidVal.Instance,
        SeqMode.All => new BoolVal(true),
        SeqMode.Any => new BoolVal(false),
        SeqMode.Find => throw new RuntimeException("Find: 没有满足条件的元素", ErrorKind.Value),
        // SortBy 攒的是「键 + 元素」对:按键排(稳定),交出去的是**元素**
        SeqMode.SortBy => new ListVal([.. BuiltinClasses
            .SortByKey(((ListVal)acc).Elements, p => ((ListVal)p).Elements[0]).Elements
            .Select(p => ((ListVal)p).Elements[1])]),
        _ => acc,
    };

    /// <summary>半成品构造器继续收参数:喂给 init 的剩余部分,应用完才交出对象</summary>
    private void StepCtorApply(ControlFrame cf)
    {
        var target = cf.Arg<ObjectVal>(2, "CtorApply");
        if (cf.Count == 0)
        {
            CallInto(cf, cf.Arg<FunctionVal>(0, "CtorApply"), cf.Arg<RuntimeValue>(1, "CtorApply"));
            return;
        }

        // 和 StepClassInit 一个规则:交出 init 的返回值(半成品构造器继续柯里化)
        Return(cf, HalfCtor(target, cf.Result(0)));
    }

    /// <summary>prepend/append 合成:先跑块再调原函数,或先调原函数再跑块</summary>
    private void StepCompose(ControlFrame cf)
    {
        var original = cf.Arg<RuntimeValue>(0, "Compose");
        var block = cf.Arg<BlockVal>(1, "Compose");
        var arg = cf.Arg<RuntimeValue>(2, "Compose");
        var isPrepend = cf.Arg<BoolVal>(3, "Compose").Value;
        if (isPrepend)
        {
            if (cf.Count == 0)
            {
                _top = new BlockExecFrame(block.Block) { Parent = cf, Scope = block.CaptureScope.Push() };
                return;
            }

            if (cf.Count == 1)
            {
                CallInto(cf, original, arg);
                return;
            }

            Return(cf, cf.Result(1));
            return;
        }

        // append
        if (cf.Count == 0)
        {
            CallInto(cf, original, arg);
            return;
        }

        if (cf.Count == 1)
        {
            _top = new BlockExecFrame(block.Block) { Parent = cf, Scope = block.CaptureScope.Push() };
            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>类运算符:在实例作用域里找同名的成员(符号就是成员名),调它</summary>
    private void StepClassOp(ControlFrame cf)
    {
        var self = cf.Arg<ObjectVal>(0, "ClassOp");
        var arg = cf.Arg<RuntimeValue>(1, "ClassOp");
        var op = cf.Arg<StringVal>(2, "ClassOp").Value;
        if (cf.Count == 0)
        {
            var impl = FindClassOperator(self.Scope, op)
                       ?? throw new RuntimeException($"对象没有运算符 '{op}'", ErrorKind.Attribute);
            CallInto(cf, impl, arg);
            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>**槽运算符**(`by + := property g s`):`a + b` 走到这儿。
    ///
    /// 和类运算符的区别:那一格的值是 **property** —— 读它走 getter,getter 交回的是运算符函数,
    /// 再拿那个函数收右操作数。所以两级,一个帧里走完(阶段由 Count 推):
    /// 0 = 找槽、推 getter;1 = 拿上一步的函数收右操作数;2 = 交出去。
    ///
    /// 槽在哪儿:**自己那层先说话**(类体里写的 `by + := …`),没有才问生效中的接口实现
    /// (`interface { by + := … }`,实现体跑一遍就落在实现身上)。两处都只看 `by` 槽。
    /// `Args = [接收者, 右操作数, 符号]`。</summary>
    private void StepTraitOp(ControlFrame cf)
    {
        var self = cf.Arg<ObjectVal>(0, "TraitOp");
        var arg = cf.Arg<RuntimeValue>(1, "TraitOp");
        var op = cf.Arg<StringVal>(2, "TraitOp").Value;

        if (cf.Count == 0)
        {
            // 自己那层声明的槽读的是 `this`(类体里的 `by * := property …`),**不能**绑接收者;
            // 接口实现那条读的是 `instance`,要绑到这一次的接收者上才交出去
            RuntimeValue prop;
            if (self.Scope.LookupField(op) is { } own && own.HasAttr(Attr.By))
                prop = own.Value;
            else if (BuiltinClasses.TraitSlot(this, self, op) is { } hit)
                prop = BuiltinClasses.Activate(this, hit);
            else
                throw new RuntimeException($"对象没有运算符 '{op}'", ErrorKind.Attribute);

            CallInto(cf, PropertyGetter(prop, op), VoidVal.Instance);   // 读槽 → 运算符函数
            return;
        }

        if (cf.Count == 1)
        {
            CallInto(cf, cf.Last, arg);                                      // 用它收右操作数
            return;
        }

        Return(cf, cf.Last);
    }

    private void StepCallAssign(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            CallInto(cf, cf.Arg<FunctionVal>(0, "CallAssign"), cf.Arg<RuntimeValue>(1, "CallAssign"));
            return;
        }

        cf.Scope.Assign(cf.Arg<StringVal>(2, "CallAssign").Value, cf.Result(0), ViaTrait(cf.Result(0)));
        Return(cf, cf.Result(0));
    }

    private void StepCallReturn(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            CallInto(cf, cf.Arg<FunctionVal>(0, "CallReturn"), cf.Arg<RuntimeValue>(1, "CallReturn"));
            return;
        }

        Return(cf, cf.Arg<RuntimeValue>(2, "CallReturn"));
    }

    /// <summary>找实例里这个符号的运算符实现。符号就是成员名(`+ := f` 定义的就是 `+`),
    /// 各层类体平铺在同一个实例 scope、子类覆盖父类,所以只有一个,查一层就够。</summary>
    private static FunctionVal? FindClassOperator(Scope scope, string symbol)
        => scope.LookupField(symbol)?.Value as FunctionVal;
}
