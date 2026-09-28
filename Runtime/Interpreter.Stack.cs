namespace Ravel.Runtime;

/// <summary>显式持久帧栈求值器:Step() 循环逐帧推进,C# 栈恒平,递归深度=帧链长度。
/// 本文件只放推进循环本身(取帧/推帧/返回);节点求值见 Interpreter.Nodes.cs,
/// 调用分派见 Interpreter.Call.cs,控制帧见 Interpreter.Control.cs,模块加载见 Interpreter.Modules.cs。</summary>
public partial class Interpreter
{
    /// <summary>调用栈最多列几层——深递归时帧链可能有几十万层</summary>
    private const int MaxTrace = 12;

    private Frame _top = null!;
    private RuntimeValue _result = VoidVal.Instance;
    private Scope _rootScope = null!;

    /// <summary>顶层入口:整个程序一个块根帧(帧链包含剩余语句,callcc 续延才能完整恢复)。scope 用 _rootScope(ravel 模块切换会改它)</summary>
    private RuntimeValue RunStack(Program p)
    {
        _rootScope = _global;
        // 根块的 Source/位置要带上,否则报错时调用栈最后一层是"0:0"
        _top = new BlockExecFrame(new BlockExpr(p.Statements) { Line = 1, Column = 1, Source = p.Source })
        {
            Scope = _rootScope
        };
        _result = VoidVal.Instance;
        while (_top != null) StepOnce();
        return _result;
    }

    /// <summary>切换作用域:顶层改 _rootScope,块内改最近的 BlockExecFrame 的 scope(ravel 模块用)</summary>
    private void SetAmbientScope(Scope scope)
    {
        var f = _top;
        while (f is not BlockExecFrame && f.Parent != null) f = f.Parent;
        f.Scope = scope;
        if (f.Parent == null) _rootScope = scope;
    }

    /// <summary>`instance` 的解析:**这一次调用在服务谁**。找不到返回 null(由调用点报错)。
    ///
    /// 从 `start`(读 `instance` 那一帧的调用点作用域)沿词法链往外找一遍,再沿帧链一层层往外、
    /// 每帧从它自己的 scope 起走词法链 —— 取最近一层激活格里的接收者。
    ///
    /// 两段各管一半:**词法**那一半管"槽体自己,以及槽体里造的闭包"(留存下来的闭包靠它记住
    /// 自己属于谁 —— 从前实现身上只有一格共享的 `instance`,留存值会后漂);**帧链**那一半管
    /// "槽体里调用的辅助函数"(它的词法链上没有激活格,但调用链上有)。
    ///
    /// 只认 `impl$active` 就是**这个实现**的激活格(认号不认对象:副本带的是同一枚号,
    /// 见 `BuiltinClasses.ImplIdMember`):跨实现边界时宁可报「没有正在被服务的实例」,
    /// 也不能静默绑到别人的接收者上。</summary>
    internal ObjectVal? ActiveInstance(Scope? start, int implId)
    {
        for (var s = start; s != null; s = s.Parent)
            if (ActiveAt(s, implId) is { } lexical) return lexical;

        for (var f = _top; f != null; f = f.Parent)
            for (var s = f.Scope; s != null; s = s.Parent)
                if (ActiveAt(s, implId) is { } dynamic) return dynamic;

        return null;
    }

    /// <summary>这一层是不是"这个实现的"激活格:两格都在、且 `impl$active` 就是这枚号。</summary>
    private static ObjectVal? ActiveAt(Scope s, int implId)
        => s.LookupField(BuiltinClasses.InstanceActiveMember) is { } vr
           && s.LookupField(BuiltinClasses.ImplActiveMember)?.Value is IntVal id && id.Value == implId
            ? (ObjectVal)vr.Value
            : null;

    private void StepOnce()
    {
        CurrentScope = _top.Scope;
        try
        {
            switch (_top)
            {
                case NodeFrame nf: StepNode(nf); break;
                case BlockExecFrame bf: StepBlockExec(bf); break;
                case ControlFrame cf: StepControl(cf); break;
            }
        }
        catch (RuntimeException ex)
        {
            // 只在最内层处理一次:补过位置之后 Located 为真,外层不再覆盖成更外侧的位置
            if (ex.Located) throw;

            Locate(ex, _top);
            if (HandToRavelHandler(ex)) return;   // 交给 Ravel 的 handler,异常到此为止
            throw;                                // 没人接 → 冒泡给 CLI 打报告
        }
        catch (SyntaxException sx)
        {
            // `eval`/`using` 会在求值中途碰到语法错误。对 Ravel 层来说那只是一次
            // 「操作失败」,该和运行时错误一样能被 Ex.try 接住——否则 eval 一段用户输入
            // 就能把程序打死(Ravel 的 try 只认 RuntimeException)。
            // 位置用解析器给的那个(比当前帧准),调用栈照常补上。
            var ex = new RuntimeException(sx.Message)
            {
                File = sx.Spot.File, Line = sx.Spot.Line, Column = sx.Spot.Column,
                Trace = StackTraceOf(_top),
            };
            if (HandToRavelHandler(ex)) return;
            throw;                                // 没人接 → 原样冒泡,CLI 按语法错误渲染
        }
    }

    /// <summary>拍一份**模块加载状态**快照:加载栈 `_loading`(谁正在加载)。
    ///
    /// 引擎只管它自己这两样(`_loading` / `_loaded`)—— **handler 栈不归它管**:那是库的状态,
    /// 它就在 Ravel 里那个 list 上,库自己拍、自己还原(见 predefined.rav 的 `callcc`)。
    ///
    /// 为什么需要拍:续延一调用就把当前帧链整个丢掉,"跑完收尾去退 `_loading`"那一步再也不会执行 ——
    /// 从**模块体**里逃出去,那个模块会永远停在 `_loading` 里,之后正常 `using` 被误报
    /// 「检测到循环引用」,而且它已记进 `_loaded`,于是 `using` 变成**静默空操作**、定义缺着。
    ///
    /// 快照只收 `_loading`,**不拷 `_loaded`**:被中断的加载在 <see cref="RestoreLoading"/> 那侧
    /// 按"此刻还在 `_loading` 里、快照里没有"算出来(否则每次 `callcc` 都要拷一遍已加载表)。
    /// 空是常态(绝大多数 `callcc` 都不在模块体里跑),那时给一个共享的空表,不分配。</summary>
    internal RuntimeValue SnapshotLoading()
    {
        if (_loading.Count == 0) return EmptyLoading;
        return new ListVal([.. _loading.Select(m => (RuntimeValue)new StringVal(m))]);
    }

    private static readonly ListVal EmptyLoading = new([]);

    /// <summary>把模块加载状态还原成快照那一份,并把"被这次还原甩掉的加载"(此刻还在 `_loading` 里、
    /// 快照里没有的)从 `_loaded` 里摘掉 —— 不摘的话那个模块停在"loaded 一半"上,之后 `using` 只是
    /// 静默地什么都不做。摘掉之后下次 `using` 从头再来。</summary>
    internal RuntimeValue RestoreLoading(RuntimeValue snap)
    {
        if (snap is not ListVal loading) return VoidVal.Instance;
        if (loading.Elements.Count == 0 && _loading.Count == 0) return VoidVal.Instance;   // 两边都空:没什么可还原

        var keep = new List<string>();
        foreach (var e in loading.Elements)
            if (e is StringVal m) keep.Add(m.Value);

        foreach (var m in _loading)
            if (!keep.Contains(m)) _loaded.Remove(m);

        _loading.Clear();
        for (var i = keep.Count - 1; i >= 0; i--) _loading.Push(keep[i]);

        return VoidVal.Instance;
    }

    /// <summary>Ravel 那边注册的"错误交给谁"的钩子(库启动时注册一次,见 predefined.rav 的 `onError`)。
    /// 引擎**不认识** handler 栈:它只把这个函数调起来,由库决定有没有人接、没人接怎么办 ——
    /// 于是"异常处理"整套也住在库里,和 `while` / `try` / `callcc` 一样。</summary>
    private FunctionVal? _errorHook;

    /// <summary>这一次交给钩子的那个异常:库"没人接"时调 `System.Unhandled`,由这里**原样**再抛出去
    /// (位置与调用栈都保持一模一样 —— `Located` 已经是真,不会再被覆盖成库里的位置)。</summary>
    private RuntimeException? _handed;

    /// <summary>把冒泡上来的错误交给库注册的钩子。没注册钩子就返回 false(照旧冒泡给 CLI 打报告)。</summary>
    private bool HandToRavelHandler(RuntimeException ex)
    {
        if (_errorHook is not { } hook) return false;

        _handed = ex;
        // 钩子体内一般会 escape 回 try 的 callcc,所以 sink 取冒泡点即可
        CallInto(_top.Parent ?? _top, hook, new ExceptionVal(ex.Message));
        return true;
    }

    /// <summary>`System.Unhandled e`:库在"没人接"时调它 —— 把引擎这次交出去的那个异常原样抛出。</summary>
    private RuntimeValue Unhandled(RuntimeValue e)
    {
        var ex = _handed;
        _handed = null;
        throw ex ?? new RuntimeException(e is ExceptionVal ev ? ev.Message : Show(e));
    }

    /// <summary>出错位置:当前正在求值的节点;它没有位置(控制帧代表「一次内建调用」而不是
    /// 源码上的某个点,合成的块也一样)就沿帧链往上找最近一个有位置的。
    /// 不加这个回退的话,`using` 找不到文件、类没有 init 这类错误全都报不出位置——
    /// `Locate` 只补一次,而 StepOnce 不是递归的,没有第二层来兜底。</summary>
    private static (int Line, int Column) ErrorSpot(Frame top)
    {
        for (var f = top; f != null; f = f.Parent)
        {
            // 控制帧自己没位置,但它记着发起它的那个节点 —— 用它更准
            if (f is ControlFrame { CallSite: { } site } && site.Line > 0)
                return (site.Line, site.Column);

            var (line, col) = FrameSpot(f);
            if (line > 0) return (line, col);
        }

        return (0, 0);
    }

    /// <summary>给运行时错误补上位置和 Ravel 层调用栈。帧链本身就是调用栈,沿它收集即可。</summary>
    private static RuntimeException Locate(RuntimeException ex, Frame top)
    {
        (ex.Line, ex.Column) = ErrorSpot(top);
        ex.File = NearestSource(top);
        ex.Trace = StackTraceOf(top);
        return ex;
    }

    /// <summary>沿帧链收集 Ravel 层调用栈。块帧才代表一次「调用」,节点帧只是栈帧内部的步骤</summary>
    private static List<string> StackTraceOf(Frame top)
    {
        var trace = new List<string>();
        for (var f = top; f != null; f = f.Parent)
        {
            if (f is not BlockExecFrame bf) continue;
            if (trace.Count >= MaxTrace) continue;
            var (line, col) = FrameSpot(bf);
            if (line == 0) continue;          // 没有位置的块(合成的)不入栈
            trace.Add(bf.Block.Source is { } src ? $"{ErrorReport.ShortPath(src)}:{line}:{col}" : $"{line}:{col}");
        }

        return trace;
    }

    /// <summary>最近一个有源文件名的块——那就是「出错时在哪个文件里」</summary>
    private static string? NearestSource(Frame top)
    {
        for (var f = top; f != null; f = f.Parent)
            if (f is BlockExecFrame { Block.Source: { } src }) return src;
        return null;
    }

    /// <summary>帧在源码里的位置。块帧用块本身的位置(≈ 函数定义处)</summary>
    private static (int Line, int Column) FrameSpot(Frame f) => f switch
    {
        NodeFrame nf => (nf.Node.Line, nf.Node.Column),
        BlockExecFrame bf => (bf.Block.Line, bf.Block.Column),
        _ => (0, 0),
    };

    /// <summary>完成帧:把结果交给父帧(父空=顶层完成)</summary>
    private void Return(Frame f, RuntimeValue v)
    {
        _top = f.Parent == null ? null! : f.Parent.WithResult(v);
        _result = f.Parent == null ? v : _result;
    }

    private void PushChild(Frame parent, AstNode child) => _top = new NodeFrame(child) { Parent = parent, Scope = parent.Scope };

    // ======================== 块执行 ========================

    private void StepBlockExec(BlockExecFrame bf)
    {
        if (bf.Count < bf.Block.Statements.Count)
        {
            _top = new NodeFrame(bf.Block.Statements[bf.Count]) { Parent = bf, Scope = bf.Scope };
            return;
        }

        Return(bf, bf.Count == 0 ? VoidVal.Instance : bf.Last);
    }
}
