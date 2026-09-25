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
            case ControlKind.Compose: StepCompose(cf); break;
            case ControlKind.ClassOp: StepClassOp(cf); break;
            case ControlKind.CallAssign: StepCallAssign(cf); break;
            case ControlKind.CallReturn: StepCallReturn(cf); break;
            case ControlKind.CtorApply: StepCtorApply(cf); break;
        }
    }

    private void StepWith(ControlFrame cf)
    {
        var obj = cf.Arg<RuntimeValue>(0, "with");
        var body = cf.Arg<BlockVal>(1, "with");
        if (cf.Count == 0)
        {
            var copy = obj switch
            {
                ObjectVal ov => RuntimeType.CopyObject(ov),
                ListVal lv => new ListVal([.. lv.Elements]),
                SetVal sv => new SetVal([.. sv.Elements]),
                DictVal dv => new DictVal(new Dictionary<string, RuntimeValue>(dv.Entries)),
                _ => obj
            };
            var newCf = cf with { State = copy };
            var bodyScope = copy is ObjectVal ov2 ? ov2.Scope.Push() : body.Scope.Push();
            _top = new BlockExecFrame(body.Block) { Parent = newCf, Scope = bodyScope };
            return;
        }

        Return(cf, cf.State);
    }

    /// <summary>callcc:把「捕获点之后要算的东西」作为续延交给 lambda。
    /// 续延被调用时由 CallInto 负责切回捕获点(见 ContinuationVal 分支)。</summary>
    private void StepCallCC(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            CallInto(cf, cf.Arg<RuntimeValue>(0, "callcc"), new ContinuationVal(cf));
            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>using/ravel 的模块加载。模块体执行期间要一直把它记在 `_loading` 里,
    /// 循环引用才检测得到——所以 push 在这里、pop 在块跑完那一步(State 存路径,顺带当已 push 的凭据)。
    /// 注意 pop 依赖帧的正常收尾:若 callcc 把续延甩过这个帧,模块体会留在 `_loading` 里不再摘掉,
    /// 之后真去引它会被误报成循环引用。模块体里用 callcc 逃生是极罕见的写法,先按简单的来。</summary>
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
            _top = new BlockExecFrame(Parser.ParseBlock(code)) { Parent = cf, Scope = cf.Scope };
            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>函数交替:先试 f,类型不匹配则试 g</summary>
    private void StepAlternate(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var f = cf.Arg<RuntimeValue>(0, "|");
            var g = cf.Arg<RuntimeValue>(1, "|");
            var arg = cf.Arg<RuntimeValue>(2, "|");
            try
            {
                CallInto(cf, f, arg);
            }
            catch (TypeMismatchException)
            {
                CallInto(cf, g, arg);
            }

            return;
        }

        Return(cf, cf.Result(0));
    }

    /// <summary>类实例化:沿祖先链(顶祖先→自身)依次跑每层类体(只跑 Body,不跑各层 init),
    /// 再从最具体类往上找第一个有 init 的层,只调那一个。所有层的字段平铺在同一个 instanceScope,
    /// ObjectVal 与 this 在第一个类体执行前就绑好(ClassType = 最终子类)。
    /// 阶段由 Count 推进:0=建 scope/绑 this/推第一层,(0,N)=推第 Count 层,N=调 init,&gt;N=返回对象。
    /// 只允许 CallTypeInto 构造本帧(它保证 Count==0、State==VoidVal),别处复用会破坏 State 形状假设。</summary>
    private void StepClassInit(ControlFrame cf)
    {
        var type = cf.Arg<TypeVal>(0, "class").Value;
        var arg = cf.Arg<RuntimeValue>(1, "class");
        var bodies = RuntimeType.CollectBodies(type); // 顶祖先 → 自身,≥ 1 层

        if (cf.Count == 0)
        {
            var instanceScope = new Scope(type.Body!.Scope);
            var obj = new ObjectVal(type, instanceScope);
            instanceScope.Define("this", type, obj);
            _top = new BlockExecFrame(bodies[0].Block)
            {
                Parent = cf with { State = obj },
                Scope = instanceScope
            };
            return;
        }

        var inst = (ObjectVal)cf.State; // Count > 0 起 State 恒为 ObjectVal

        // 第 Count 层刚跑完 → 推下一层(同一个 instanceScope)
        if (cf.Count < bodies.Count)
        {
            _top = new BlockExecFrame(bodies[cf.Count].Block) { Parent = cf, Scope = inst.Scope };
            return;
        }

        if (cf.Count == bodies.Count)
        {
            // 构造器就是实例作用域里名为 init 的那个:各层平铺在同一 scope,
            // 子类的 init 覆盖父类的,所以直接找名字 = 只调最具体层声明的那个
            var init = inst.Scope.LookupField("init")?.Value as FunctionVal
                       ?? throw new RuntimeException($"类型 {type.DisplayName} 没有构造器（init）");
            CallInto(cf, init, arg);
            return;
        }

        // Count 超过层数:init 已返回(用 > 而非 == :callcc 续延重入可能把 Count 顶过头)。
        // Results 里前面几项是各层类体的返回值,最后一项才是 init 的——所以取 Last 而不是 Result(0)。
        // init 若还返回函数(多参构造器只喂了一部分)就交出半成品,让它继续收参数,和普通函数一样柯里化
        Return(cf, cf.Last is FunctionVal rest && rest.IsClosure ? new PartialCtor(inst, rest) : inst);
    }

    /// <summary>半成品构造器继续收参数:喂给 init 的剩余部分,应用完才交出对象</summary>
    private void StepCtorApply(ControlFrame cf)
    {
        var target = cf.Arg<ObjectVal>(2, "CtorApply");
        if (cf.Count == 0)
        {
            CallInto(cf, cf.Arg<FunctionVal>(0, "CtorApply"), cf.Arg<RuntimeValue>(1, "CtorApply"));
            return;
        }

        Return(cf, cf.Result(0) is FunctionVal rest && rest.IsClosure ? new PartialCtor(target, rest) : target);
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
                _top = new BlockExecFrame(block.Block) { Parent = cf, Scope = block.Scope.Push() };
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
            _top = new BlockExecFrame(block.Block) { Parent = cf, Scope = block.Scope.Push() };
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
                       ?? throw new RuntimeException($"对象没有运算符 '{op}'");
            CallInto(cf, impl, arg);
            return;
        }

        Return(cf, cf.Result(0));
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

    /// <summary>找实例里这个符号的运算符实现。符号就是成员名(`+ := f` 定义的就是 `+`),
    /// 各层类体平铺在同一个实例 scope、子类覆盖父类,所以只有一个,查一层就够。</summary>
    private static FunctionVal? FindClassOperator(Scope scope, string symbol)
        => scope.LookupField(symbol)?.Value as FunctionVal;
}
