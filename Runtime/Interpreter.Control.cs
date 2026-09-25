namespace Ravel.Runtime;

/// <summary>控制帧状态机:每个控制内建一个 ControlFrame,Args 存已收集的块/参数,
/// Results 与 State 记录推进进度(约定:Args 全程不变,进度看 Count/State)。</summary>
public partial class Interpreter
{
    private void StepControl(ControlFrame cf)
    {
        switch (cf.Kind)
        {
            case ControlKind.While: StepWhile(cf); break;
            case ControlKind.If: StepIf(cf); break;
            case ControlKind.With: StepWith(cf); break;
            case ControlKind.Foreach: StepForeach(cf); break;
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

    private static BlockVal GetBlock(RuntimeValue v) => (BlockVal)v;

    private void StepWhile(ControlFrame cf)
    {
        var cond = GetBlock(cf.Args.At(0));
        var body = GetBlock(cf.Args.At(1));
        if (cf.Count == 0)
        {
            _top = new BlockExecFrame(cond.Block) { Parent = cf, Scope = cond.Scope.Push() };
            return;
        }

        if (cf.Count == 1)
        {
            if (cf.Result(0) is not BoolVal bv) throw new RuntimeException("while 条件必须是 bool");
            if (bv.Value)
                _top = new BlockExecFrame(body.Block) { Parent = cf, Scope = body.Scope.Push() };
            else
                Return(cf, cf.State);
            return;
        }

        // count 2:body 完成 → 循环
        var newCf = cf with { State = cf.Result(1), Results = RList<RuntimeValue>.Empty };
        _top = new BlockExecFrame(cond.Block) { Parent = newCf, Scope = cond.Scope.Push() };
    }

    private void StepIf(ControlFrame cf)
    {
        var c = GetBlock(cf.Args.At(0));
        var t = GetBlock(cf.Args.At(1));
        var e = GetBlock(cf.Args.At(2));
        if (cf.Count == 0)
        {
            _top = new BlockExecFrame(c.Block) { Parent = cf, Scope = c.Scope.Push() };
            return;
        }

        if (cf.Count == 1)
        {
            if (cf.Result(0) is not BoolVal b) throw new RuntimeException("if 条件必须是 bool");
            var branch = b.Value ? t : e;
            _top = new BlockExecFrame(branch.Block) { Parent = cf, Scope = branch.Scope.Push() };
            return;
        }

        Return(cf, cf.Result(1));
    }

    private void StepWith(ControlFrame cf)
    {
        var obj = cf.Args.At(0);
        var body = GetBlock(cf.Args.At(1));
        if (cf.Count == 0)
        {
            var copy = obj switch
            {
                ObjectVal ov => new ObjectVal(ov.ClassType, RuntimeType.CopyScope(ov.Scope)),
                ListVal lv => new ListVal([.. lv.Elements]),
                SetVal sv => new SetVal([.. sv.Elements]),
                DictVal dv => new DictVal(new Dictionary<string, RuntimeValue>(dv.Entries)),
                _ => obj
            };
            if (copy is ObjectVal copyObj)
                copyObj.Scope.DefineOrReplace("this", copyObj.ClassType, copyObj);
            var newCf = cf with { State = copy };
            var bodyScope = copy is ObjectVal ov2 ? ov2.Scope.Push() : body.Scope.Push();
            _top = new BlockExecFrame(body.Block) { Parent = newCf, Scope = bodyScope };
            return;
        }

        Return(cf, cf.State);
    }

    private void StepForeach(ControlFrame cf)
    {
        var items = ((ListVal)cf.Args.At(0)).Elements;
        var fn = cf.Args.At(1);
        if (cf.Count < items.Count)
        {
            CallInto(cf, fn, items[cf.Count]);
            return;
        }

        Return(cf, cf.Count == 0 ? VoidVal.Instance : cf.Last);
    }

    private void StepCallCC(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            CallccActive = true;
            var fn = cf.Args.At(0);
            var k = new ContinuationVal(cf);
            CallInto(cf, fn, k);
            return;
        }

        CallccActive = false;
        Return(cf, cf.Result(0));
    }

    /// <summary>多发续延:重跑局部 onDone(resumeRoot 消费 arg)到其完成,再把 arg 返回给 k 的调用者</summary>
    private void ResumeContinuation(ContinuationVal k, RuntimeValue arg)
    {
        var resumeRoot = k.Captured.Parent;
        if (resumeRoot == null)
        {
            _top = null!;
            _result = arg;
            return;
        }

        _resumeStopNode = (resumeRoot as NodeFrame)?.Node;
        _resumeCaller = _top.Parent;
        _resumeValue = arg;
        _top = resumeRoot.WithResult(arg);
    }

    private void StepUsing(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var path = ((StringVal)cf.Args.At(0)).Value;
            var ast = LoadModuleAst(path);
            if (ast == null)
            {
                Return(cf, VoidVal.Instance);
                return;
            }

            _top = new BlockExecFrame(ast) { Parent = cf, Scope = cf.Scope };
            return;
        }

        Return(cf, VoidVal.Instance);
    }

    private void StepEval(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            var code = ((StringVal)cf.Args.At(0)).Value;
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
            var f = cf.Args.At(0);
            var g = cf.Args.At(1);
            var arg = cf.Args.At(2);
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
        var type = ((TypeVal)cf.Args.At(0)).Value;
        var arg = cf.Args.At(1);
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
                       ?? throw new RuntimeException($"类型 {type.Name} 没有构造器（init）");
            CallInto(cf, init, arg);
            return;
        }

        // Count 超过层数:init 已返回(用 > 而非 == :callcc 续延重入可能把 Count 顶过头)。
        // Results 里前面几项是各层类体的返回值,最后一项才是 init 的——所以取 Last 而不是 Result(0)。
        // init 若还返回函数(多参构造器只喂了一部分)就交出半成品,让它继续收参数,和普通函数一样柯里化
        Return(cf, cf.Last is FunctionVal rest ? new PartialCtor(inst, rest) : inst);
    }

    /// <summary>半成品构造器继续收参数:喂给 init 的剩余部分,应用完才交出对象</summary>
    private void StepCtorApply(ControlFrame cf)
    {
        var target = (ObjectVal)cf.Args.At(2);
        if (cf.Count == 0)
        {
            CallInto(cf, cf.Args.At(0), cf.Args.At(1));
            return;
        }

        Return(cf, cf.Result(0) is FunctionVal rest ? new PartialCtor(target, rest) : target);
    }

    /// <summary>prepend/append 合成:先跑块再调原函数,或先调原函数再跑块</summary>
    private void StepCompose(ControlFrame cf)
    {
        var original = cf.Args.At(0);
        var block = GetBlock(cf.Args.At(1));
        var arg = cf.Args.At(2);
        var isPrepend = ((BoolVal)cf.Args.At(3)).Value;
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

    /// <summary>类运算符:动态找实例 operatorX 字段(沿 parent 链)并成 | 交替,调并函数</summary>
    private void StepClassOp(ControlFrame cf)
    {
        var self = (ObjectVal)cf.Args.At(0);
        var arg = cf.Args.At(1);
        var op = ((StringVal)cf.Args.At(2)).Value;
        if (cf.Count == 0)
        {
            var ops = CollectOperators(self.Scope, op);
            if (ops == null) throw new RuntimeException($"对象没有运算符 '{op[8..]}'");
            CallInto(cf, ops, arg);
            return;
        }

        Return(cf, cf.Result(0));
    }

    private void StepCallAssign(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            CallInto(cf, cf.Args.At(0), cf.Args.At(1));
            return;
        }

        cf.Scope.Assign(((StringVal)cf.Args.At(2)).Value, cf.Result(0));
        Return(cf, cf.Result(0));
    }

    private void StepCallReturn(ControlFrame cf)
    {
        if (cf.Count == 0)
        {
            CallInto(cf, cf.Args.At(0), cf.Args.At(1));
            return;
        }

        Return(cf, cf.Args.At(2));
    }

    /// <summary>收集实例 scope 里所有带 attr 的函数,用 | 交替组合(多 operatorX 重载自动试下一个)。
    /// 继承来的运算符也在同一 scope 里(各层类体平铺),所以扫一层就够。</summary>
    private static FunctionVal? CollectOperators(Scope scope, string attr)
    {
        FunctionVal? result = null;
        foreach (var kv in scope.Variables)
        {
            if (kv.Value.HasAttr(attr) && kv.Value.Value is FunctionVal fn)
                result = result == null
                    ? fn
                    : new ControlFunction(ControlKind.Alternate, 1, RList<RuntimeValue>.Empty.Add(result).Add(fn));
        }

        return result;
    }
}
