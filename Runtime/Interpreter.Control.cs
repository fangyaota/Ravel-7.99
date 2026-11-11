namespace Ravel.Runtime;

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
            case ControlKind.Then: StepThen(cf); break;
            case ControlKind.TraitOp: StepTraitOp(cf); break;
            case ControlKind.CallAssign: StepCallAssign(cf); break;
            case ControlKind.CallReturn: StepCallReturn(cf); break;
            case ControlKind.CtorApply: StepCtorApply(cf); break;
            case ControlKind.RejectStop: StepRejectStop(cf); break;
        }
    }

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
            // 有自己的成员表就直接跑在它**本身**里(不推)—— 定义落回对象。
            // 没有的(函数 / 类对象 / 模块 / 属性 / 作用域值)只能退到块自己的捕获作用域,
            // 那儿还是推一层:那些东西没有"能落定义的成员表",不推的话定义会漏到外层去。
            var bodyScope = BuiltinClasses.HasOwnTable(obj)
                ? ((ObjectVal)obj).Scope
                : body.CaptureScope.Push();
            _top = new BlockExecFrame(body.Block) { Parent = cf, Scope = bodyScope };
            return;
        }

        Return(cf, obj);
    }

    private void StepCallCC(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            // **引擎自己那份**控制状态(模块加载栈)在这儿拍下来、跳转时自己盖回去 ——
            // 它是引擎的家事(见 `ContinuationVal.Loading`)。**handler 栈仍旧不掺和**:
            // 那是库的状态,库自己拍自己还原(`lib/predefined.rav` 的 `callcc`)。
            // **捕获 = 冻结**:这一趟之后,链上每一帧都不许再原地改(见 `Frame.Captured`)——
            // 续延是**多发射**的,同一枚模板要能用很多次,谁都不能把它弄脏。
            // 沿链一路标到**根**:恢复是从捕获点往上走的,那些祖先也会被读到。
            for (var f = (Frame?)cf; f != null; f = f.Parent) f.Captured = true;
            CallInto(cf, cf.Arg<RuntimeValue>(0, "callcc"),
                     new ContinuationVal(cf) { Loading = SnapshotLoading() });
            return;
        }

        Return(cf, cf.Result(0));
    }

    private void StepUsing(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var path = cf.Arg<StringVal>(0, "using").Value;
            // **就在这一刻记进"当前正在跑的那个模块"** —— 记的是 `using` 后面写的那一串。
            // (要在 `LoadModuleAst` **之前**:那条路上可能递归跑别的代码,而"当前模块"
            //  只该是发起这一句的那一个 —— `_loading` 的栈顶就是它。)
            RecordReference(path);
            var ast = LoadModuleAst(path);
            if (ast == null)
            {
                Return(cf, VoidVal.Instance);
                return;
            }

            var full = ResolveModulePath(path)!;   // 上面刚解析成功过
            _refsByPath.TryAdd(full, []);          // 这个文件攒 `using` 的地方(第一次加载时开)
            _loading.Push(full);
            _top = new BlockExecFrame(ast) { Parent = cf with { State = new StringVal(full) }, Scope = cf.Scope };
            return;
        }

        if (cf.State is StringVal s)
        {
            _loading.Pop();
            // 这个文件跑完**没建出模块**(`enum.rav` 这种:它落的是全局的名字)——
            // 它写着的那几条 `using` 就归全局那一枚,不然没人接得住。
            if (!_moduleByPath.ContainsKey(s.Value)
                && _refsByPath.TryGetValue(s.Value, out var refs) && refs.Count > 0)
                GlobalModule().ReferencePaths.AddRange(refs);
        }
        Return(cf, VoidVal.Instance);
    }

    private void StepEval(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var code = cf.Arg<StringVal>(0, "eval").Value;
            // 给个合成文件名:不带的话语法错误只能报 `--> 3:1`,不知道那是 eval 出来的
            var block = Parser.ParseBlock(code, "<eval>", MoreControlFlow || Parser.DeclaresMoreControlFlow(code));
            // eval 出来的片段也算**一份要编译的东西** —— 编译期那几条它一样有份
            if (WarnForgotCall) ReportCompileWarnings(block.Statements, block.Source);
            _top = new BlockExecFrame(block) { Parent = cf, Scope = cf.Scope };
            return;
        }

        Return(cf, cf.Result(0));
    }

    private void StepAlternate(ControlFrame cf)
    {
        if (cf.Count > 0)
        {
            Return(cf, cf.Result(0));
            return;
        }

        var arg = cf.Args.Last;
        var n = cf.Args.Count - 1;          // 前面全是候选分支
        // **从第几支接着试**:参数类型对不上是**同步**抛的,一趟 for 就试完了;
        // 但**守卫**(`(v == T) => …`)是在**体里**拒收的,那是异步的 —— 错误冒泡时
        // `StepOnce` 把 `_top` 挪回这一帧(见 ResumeAlternate),于是从下一支接着走。
        // 走到第几支记在 `State` 上:帧是不可变的,推分支时顺手带上(见下面那个 `next`)。
        var start = cf.State is IntVal iv ? iv.Value : 0;
        string? last = _rejectMessage;
        _rejectMessage = null;

        for (var i = start; i < n; i++)
        {
            var next = cf with { State = IntVal.Of(i + 1) };
            try
            {
                // 参数类型对不上会在 CallInto 里**同步**抛出,于是就地接着试下一支
                CallInto(next, next.Args.At(i), arg);
                return;
            }
            catch (TypeMismatchException ex)
            {
                last = ex.Message;
                if (Environment.GetEnvironmentVariable("RAVEL_DBG_ALT") == "1")
                    Console.Error.WriteLine("[DBG-ALT-CATCH] branch " + i + ": " + ex.Message);
            }
            catch (RejectedException ex)
            {
                last = ex.Message;
            }
        }

        throw new TypeMismatchException($"| 的 {n} 个分支都不收这个参数（最后试的：{last ?? "（没有可说的）"}）");
    }

    private static Scope LayerScope(ClassVal type, Scope lexical, Scope instanceScope)
        => type.IsAssignableTo(BuiltinClasses.BaseInterface)
            ? instanceScope
            : new BodyScope(lexical, instanceScope);

    private void StepClassInit(ControlFrame cf)
    {
        var type = cf.Arg<ClassVal>(0, "class");
        var arg = cf.Arg<RuntimeValue>(1, "class");
        // 顶祖先 → 自身,≥ 1 步。一步 = 一块;`body.Append { … }` 拼上来的块各自算一步,
        // 排在同一层主块的后面(见 `BuiltinClasses.CollectBodies`)。
        var bodies = BuiltinClasses.CollectBodies(type);

        if (cf.Count == 0)
        {
            // 造出来的东西是类还是实例,取决于**被实例化的那个类**是不是 `type` 的子类
            // (走 `parent` 原型链,不是"元类"那条):
            //   `type { body }` → `type <: type` 自反 → 造出来的就是类对象(ClassVal)
            //   `MyMeta := class type {…}` → MyMeta <: type → 同上
            //   `C := class {…}` 的实例 → C 的 parent 链是 `C → object`,不含 type → 普通实例
            // 注意这只定下**中间对象**的类型:构造最终交出的是 init 的返回值(见下面 Return),
            // 所以只有"init 以 this 收尾"的用户类/元类才真把这个对象交出去。
            ObjectVal obj = type.IsAssignableTo(BuiltinClasses.Type)
                ? new ClassVal(type, null)
                : new ObjectVal(type, null);
            // 对象自己的成员表就是实例作用域:类体各层的定义都落进这张表(从前是先另建一个
            // `new Scope(捕获处)` 再让各层往里写;现在那张表本身干这件事)。
            // 词法父接"类体写在哪" —— 类体里的自由名字要在那儿解析,而不是在成员表链上。
            var instanceScope = obj.Scope;
            instanceScope.Reparent(type.ClassBody!.CaptureScope);
            instanceScope.Define(ObjectVal.ThisMember, obj);
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

    private static RuntimeValue HalfCtor(ObjectVal target, RuntimeValue returned)
        => returned is FunctionVal rest && rest.IsClosure && returned is not ContinuationVal
            ? new PartialCtor(target, rest)
            : returned;

    private void StepImplMake(ControlFrame cf)
    {
        var impl = cf.Arg<ObjectVal>(0, "ImplMake");
        var target = cf.Arg<ObjectVal>(1, "ImplMake");
        // **身体不是非得是块** —— `impl (某个接口 某个类 default)` 那种空体也收(见下)。
        var body = cf.Arg<RuntimeValue>(2, "ImplMake");

        if (cf.Count == 0)
        {
            // 「要求」是**前置条件**:这个类得**已经**有那几个接口的实现(查当下作用域里有没有
            // 生效中的)。放在造实现这一步,是因为"已经实现"是作用域里的事(实现登记在 scope 上),
            // 而这里手里正好有求值器。
            var trait = impl.Type;   // 接口对象(这个实现的类)—— 要求表挂在它那张

            // **密封的接口不能再被实现** —— 判据和建类那条同一个(`BuiltinClasses.SealedUp`):
            // 自己或任一祖先密封了都挡。走在**要求表**前面:那是前置条件,这是硬门。
            if (BuiltinClasses.SealedUp(trait))
                throw new RuntimeException(
                    $"`{trait.DisplayName}` 被 seal 了 —— 不能再被实现", ErrorKind.Access);

            if (trait.Requires is { Count: > 0 } reqs)
                foreach (var rt in reqs)
                    if (!BuiltinClasses.HasTrait(this, target, rt))
                        throw new RuntimeException($"`{trait.DisplayName}` 要求 {target.DisplayName} "
                            + $"已经实现了 {rt.DisplayName}（先给它 impl/use 一条）", ErrorKind.Type);

            // 接口那几层类体已经跑过了(实例化那趟跑的,跑的就是这个对象的 scope)——
            // 这里只追加**实现块**。
            //
            // **`default` 就是"空体"** —— `impl (某个接口 某个类 default)`:没槽要换,
            // 类体那趟摆好的 `by … = default` 原样留着,直接收尾。
            //
            // 到得了这儿的只可能是**块**或者 **`default`** —— 这一格在 `CallInto` 里声明成了
            // `Block`(见 `ControlFunction.Declared` / `InstallInterfaceInit`),别的值
            // 早就被拒收了(`参数 '实现体' 需要 Block，得到 Integer`)。所以这儿只剩两种:
            // 块要跑,`default` 直接收尾。
            if (body is not BlockVal blk)
            {
                BuiltinClasses.FinishImplementation(this, impl, target);
                Return(cf, impl);
                return;
            }

            // 这张表得换一个词法父:它由那趟建出来时接的是**接口定义**处,
            // 而实现块的自由名字该在**实现写在哪**解析。
            impl.Scope.Reparent(blk.CaptureScope);
            _top = new BlockExecFrame(blk.Block) { Parent = cf, Scope = impl.Scope };
            return;
        }

        BuiltinClasses.FinishImplementation(this, impl, target);
        Return(cf, impl);
    }

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

    private static bool Yes(RuntimeValue got, string label)
        => got is BoolVal b
            ? b.Value
            : throw new RuntimeException($"{label} 的函数要交回 bool，得到 {got.Type}", ErrorKind.Type);

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

    private void StepRejectStop(ControlFrame cf)
    {
        if (cf.Count == 0) { CallInto(cf, cf.Arg<RuntimeValue>(0, "RejectStop"), VoidVal.Instance); return; }
        Return(cf, cf.Result(0));
    }

    private void StepThen(ControlFrame cf)
    {
        var f = cf.Arg<RuntimeValue>(0, ">>");
        var g = cf.Arg<RuntimeValue>(1, ">>");
        var arg = cf.Arg<RuntimeValue>(2, ">>");
        if (cf.Count == 0) { CallInto(cf, f, arg); return; }
        if (cf.Count == 1) { CallInto(cf, g, cf.Result(0)); return; }
        Return(cf, cf.Result(1));
    }

    private void StepCompose(ControlFrame cf)
    {
        var original = cf.Arg<RuntimeValue>(0, "Compose");
        var block = cf.Arg<FunctionVal>(1, "Compose");
        var arg = cf.Arg<RuntimeValue>(2, "Compose");
        var isPrepend = cf.Arg<BoolVal>(3, "Compose").Value;
        if (isPrepend)
        {
            if (cf.Count == 0)
            {
                // prepend 的块**拿到调用 scope**:这样它能把 `reject` 之类塞进下一句
                // 要跑的那个体的作用域里(`(sc) => { sc.Define "reject" here }`)。
                // 拿不到调用 scope 的函数(控制帧、原生闭包、类)退回老规矩:
                // 块在自己捕获的那层跑,收一枚 `()`。
                var scope = CallScopeOf(original);
                var next = scope is null ? cf : cf with { State = new ScopeVal(scope) };
                CallInto(next, block, scope is null ? VoidVal.Instance : new ScopeVal(scope));
                return;
            }

            if (cf.Count == 1)
            {
                CallInto(cf, original, arg, (cf.State as ScopeVal)?.Inner);
                return;
            }

            Return(cf, cf.Result(1));
            return;
        }

        // append
        if (cf.Count == 0)
        {
            // 同样是那一层:块在后头跑,这时体已经跑完,它看得到体留下的东西。
            var scope = CallScopeOf(original);
            var next = scope is null ? cf : cf with { State = new ScopeVal(scope) };
            CallInto(next, original, arg, scope);
            return;
        }

        if (cf.Count == 1)
        {
            CallInto(cf, block, cf.State as ScopeVal is { } sv ? sv : VoidVal.Instance);
            return;
        }

        Return(cf, cf.Result(0));
    }

    // 这次调用会落在哪一层:能预先造出来的只有 lambda(它就是 `CaptureScope.Push()`,
    // 参数随后由 `CallInto` 定义进同一层);别的那几种调用那层要么在调用点、要么根本不存在。
    private static Scope? CallScopeOf(RuntimeValue fn) => fn switch
    {
        LambdaVal lam => lam.CaptureScope.Push(),
        BlockVal b => b.CaptureScope.Push(),
        _ => null,
    };

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

        cf.Scope.Assign(cf.Arg<StringVal>(2, "CallAssign").Value, cf.Result(0));
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

}
