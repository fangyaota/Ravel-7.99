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
                // 函数/类对象不是"带字段的数据":`with` 对它们等于原样(块跑在调用点的词法作用域里)。
                // 这条必须排在 ObjectVal 之前 —— FunctionVal 现在**也是** ObjectVal。
                FunctionVal => obj,
                // 容器有各自的浅拷贝法,也得排在 ObjectVal 之前(它们现在也是 ObjectVal)。
                // 成员表要跟着副本走(和 CopyObject 一个道理):块里 `tag = …` 那种赋值
                // 找的是副本的成员表,给空表的话会报"无法给未定义变量赋值"。
                ListVal lv => new ListVal([.. lv.Elements], BuiltinClasses.CopyScope(lv.Scope)),
                SetVal sv => new SetVal([.. sv.Elements], BuiltinClasses.CopyScope(sv.Scope)),
                DictVal dv => new DictVal(new Dictionary<string, RuntimeValue>(dv.Entries),
                    BuiltinClasses.CopyScope(dv.Scope)),
                ObjectVal ov => BuiltinClasses.CopyObject(ov),
                _ => obj
            };
            var newCf = cf with { State = copy };
            // 只有**真的拷出了副本**的那些,块才跑在副本的成员表里;其余(原子值、函数、上面没列的)
            // 跑在 body 自己的捕获作用域里。判据不能写 `copy is ObjectVal` —— 函数也是 ObjectVal,
            // 那样会把块的作用域换成函数自己的成员表。
            var bodyScope = !ReferenceEquals(copy, obj) && copy is ObjectVal ov2
                ? ov2.Scope.Push()
                : body.CaptureScope.Push();
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

    /// <summary>类实例化:沿祖先链(顶祖先→自身)依次跑每层类体(只跑 Body,不跑各层 init),
    /// 再从最具体类往上找第一个有 init 的层,只调那一个。所有层的字段平铺在同一个 instanceScope,
    /// ObjectVal 与 this 在第一个类体执行前就绑好(ClassType = 最终子类)。
    /// 阶段由 Count 推进:0=建 scope/绑 this/推第一层,(0,N)=推第 Count 层,N=调 init,&gt;N=返回对象。
    /// 只允许 CallTypeInto 构造本帧(它保证 Count==0、State==VoidVal),别处复用会破坏 State 形状假设。</summary>
    private void StepClassInit(ControlFrame cf)
    {
        var type = cf.Arg<ClassVal>(0, "class");
        var arg = cf.Arg<RuntimeValue>(1, "class");
        var bodies = BuiltinClasses.CollectBodies(type); // 顶祖先 → 自身,≥ 1 层

        if (cf.Count == 0)
        {
            var instanceScope = new Scope(type.ClassBody!.CaptureScope);
            // 造出来的东西是类还是实例,取决于**被实例化的那个类**是不是 `type` 的子类
            // (走 parent 原型链,不是元类链):
            //   `type { body }` → `type <: type` 自反 → 造出来的就是类对象(ClassVal)
            //   `MyMeta := class type {…}` → MyMeta <: type → 同上
            //   `C := class {…}` 的实例 → C 的 parent 链是 `C → object`,不含 type → 普通实例
            // 注意这只定下**中间对象**的类型:构造最终交出的是 init 的返回值(见下面 Return),
            // 所以只有"init 以 this 收尾"的用户类/元类才真把这个对象交出去。
            ObjectVal obj = type.IsAssignableTo(BuiltinClasses.Type)
                ? new ClassVal(type, instanceScope)
                : new ObjectVal(type, instanceScope);
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
            // 调用点用**实例作用域**:init 要能看见 `this` 和各层类体落的成员。
            // 内置类的默认建类函数(NativeClosure)正是靠这个把 parent/block 装到 self 上。
            CallInto(cf with { Scope = inst.Scope }, init, arg);
            return;
        }

        // Count 超过层数:init 已返回(用 > 而非 == :callcc 续延重入可能把 Count 顶过头)。
        // Results 里前面几项是各层类体的返回值,最后一项才是 init 的——所以取 Last 而不是 Result(0)。
        // init 若还返回函数(多参构造器只喂了一部分)就交出半成品,让它继续收参数,和普通函数一样柯里化
        // **构造交出 init 的返回值** —— 不再无条件交出实例。
        // init 约定以 `this` 收尾,所以普通类拿到的还是实例;
        // 而元类的 init 可以建出一个类再交出来(或返回别的什么)。
        // init 若还返回函数(多参构造器只喂了一部分)就交出半成品,和普通函数一样柯里化。
        Return(cf, cf.Last is FunctionVal rest && rest.IsClosure ? new PartialCtor(inst, rest) : cf.Last);
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

        // 和 StepClassInit 一个规则:交出 init 的返回值(半成品构造器继续柯里化)
        Return(cf, cf.Result(0) is FunctionVal rest && rest.IsClosure ? new PartialCtor(target, rest) : cf.Result(0));
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
